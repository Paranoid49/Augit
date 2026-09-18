using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Augit.Core.Documents;
using Augit.Core.Files;
using Augit.Core.Git;
using Augit.Core.Search;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Search;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.Shell;

/// <summary>
/// 网页层与 C# 能力层之间的消息桥。网页发送 <c>{ id, method, params }</c>，
/// 这里返回 <c>{ id, result }</c> 或 <c>{ id, error }</c>。
/// </summary>
/// <summary>
/// 本应用主动抛出的校验失败。消息是写好的中文提示，可以直接回传给界面；
/// 与「意外异常」区分开，避免把实现细节暴露出去。
/// </summary>
internal sealed class BridgeValidationException(string message) : Exception(message);

internal sealed class ShellBridge : IDisposable
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaximumTerminalBufferLength = 4 * 1024 * 1024;

    private readonly Lock _statusGate = new();
    private GitStatusResult? _cachedStatus;
    private long _cachedStatusAt;
    private readonly Lock _changeGate = new();
    private readonly HashSet<string> _pendingWorkspaceChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _terminalGate = new();
    private WorkspaceFileWatcher? _workspaceWatcher;
    private GitMetadataWatcher? _gitWatcher;
    private bool _pendingGitMetadataChange;
    private readonly StringBuilder _terminalBuffer = new();
    private readonly string _workspaceRoot;
    private ConPtyTerminalSession? _terminal;
    private long _terminalOffset;
    private bool _terminalExited;
    private int _terminalExitCode;

    private readonly Action<string, object>? _notify;

    public ShellBridge(string workspaceRoot, Action<string, object>? notify = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _notify = notify;
    }

    public string WorkspaceRoot => _workspaceRoot;

    public async Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken)
    {
        long id = 0;
        try
        {
            JsonElement root = ParseRequest(requestJson);
            if (root.TryGetProperty("id", out JsonElement idElement))
            {
                id = idElement.GetInt64();
            }

            string method = root.TryGetProperty("method", out JsonElement methodElement)
                ? methodElement.GetString() ?? string.Empty
                : string.Empty;
            JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement)
                ? paramsElement
                : default;
            object? result = await DispatchAsync(method, parameters, cancellationToken);
            return Serialize(id, result, null);
        }
        catch (Exception exception) when (exception is ArgumentException or BridgeValidationException)
        {
            // 校验类失败：消息是本应用自己写的中文提示，可以直接给用户看。
            return Serialize(id, null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // 文件系统失败：.NET 的原始消息是英文且含实现细节，换成可读说明。
            // 具体原因（例如只读、被占用）由各方法在返回值里给出。
            return Serialize(id, null, "无法访问文件或目录，请检查权限或占用情况。");
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException)
        {
            return Serialize(id, null, "请求或响应不是合法的 JSON。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return Serialize(id, null, "当前操作不受支持或状态不允许。");
        }
        catch (Exception exception)
        {
            // 兜底：不回传原始消息，避免把实现细节暴露到界面。
            return Serialize(id, null, $"内部错误：{exception.GetType().Name}");
        }
    }

    /// <summary>
    /// WebView2 的 WebMessageAsJson 在网页发送字符串时返回「被 JSON 编码的字符串」，
    /// 形如 "{\"id\":1,...}"，直接解析根节点是 String 而非 Object，因此需要再解一层。
    /// </summary>
    private static JsonElement ParseRequest(string requestJson)
    {
        JsonElement root;
        using (JsonDocument document = JsonDocument.Parse(requestJson))
        {
            root = document.RootElement.Clone();
        }

        if (root.ValueKind == JsonValueKind.String && root.GetString() is { Length: > 0 } inner)
        {
            using JsonDocument nested = JsonDocument.Parse(inner);
            return nested.RootElement.Clone();
        }

        return root;
    }

    private async Task<object?> DispatchAsync(string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        return method switch
        {
            "workspace/info" => WorkspaceInfo(),
            "workspace/list" => ListDirectory(parameters),
            "document/read" => await ReadDocumentAsync(parameters, cancellationToken),
            "git/status" => await ReadStatusAsync(cancellationToken),
            "git/history" => await ReadHistoryAsync(cancellationToken),
            "git/blame" => await ReadBlameAsync(parameters, cancellationToken),
            "git/file-history" => await ReadFileHistoryAsync(parameters, cancellationToken),
            "git/commit" => await ReadCommitAsync(parameters, cancellationToken),
            "git/commit-create" => await CreateCommitAsync(parameters, cancellationToken),
            "git/push" => await PushAsync(parameters, cancellationToken),
            "git/checkout" => await CheckoutAsync(parameters, cancellationToken),
            "git/branch" => await BranchAsync(parameters, cancellationToken),
            "git/fetch" => await FetchAsync(parameters, cancellationToken),
            "git/unpushed" => await ReadUnpushedAsync(cancellationToken),
            "git/remote-write" => await WriteRemoteAsync(parameters, cancellationToken),
            "git/diff" => await ReadDiffAsync(parameters, cancellationToken),
            "git/rollback" => await RollbackAsync(parameters, cancellationToken),
            "git/remotes" => await ReadRemotesAsync(cancellationToken),
            "git/references" => await ReadReferencesAsync(cancellationToken),
            "git/stashes" => await ReadStashesAsync(cancellationToken),
            "git/stash" => await CreateStashAsync(parameters, cancellationToken),
            "git/stash-write" => await WriteStashAsync(parameters, cancellationToken),
            "git/stash-content" => await ReadStashContentAsync(parameters, cancellationToken),
            "git/worktrees" => await ReadWorktreesAsync(cancellationToken),
            "git/worktree-write" => await WriteWorktreeAsync(parameters, cancellationToken),
            "git/worktree-removal" => await ReadWorktreeRemovalAsync(parameters, cancellationToken),
            "git/worktree-remove" => await RemoveWorktreeAsync(parameters, cancellationToken),
            "git/operation" => await InspectOperationAsync(cancellationToken),
            "git/operation-action" => await RunOperationActionAsync(parameters, cancellationToken),
            "git/conflicts" => await ReadConflictsAsync(cancellationToken),
            "git/conflict-accept" => await AcceptConflictSideAsync(parameters, cancellationToken),
            "git/conflict-load" => await LoadConflictAsync(parameters, cancellationToken),
            "git/conflict-save" => await SaveConflictAsync(parameters, cancellationToken),
            "terminal/start" => await StartTerminalAsync(parameters, cancellationToken),
            "terminal/read" => ReadTerminal(parameters),
            "terminal/write" => await WriteTerminalAsync(parameters, cancellationToken),
            "terminal/resize" => ResizeTerminal(parameters),
            "terminal/stop" => await StopTerminalAsync(),
            "git/clone" => await CloneAsync(parameters, cancellationToken),
            "workspace/changes" => ReadWorkspaceChanges(),
            "search/files" => await SearchFilesAsync(parameters, cancellationToken),
            "search/text" => await SearchTextAsync(parameters, cancellationToken),
            "external/launch" => await LaunchExternalAsync(parameters, cancellationToken),
            "clipboard/write" => WriteClipboard(parameters),
            "settings/read" => await ReadSettingsAsync(cancellationToken),
            "settings/write" => await WriteSettingsAsync(parameters, cancellationToken),
            "session/write" => await WriteSessionAsync(parameters, cancellationToken),
            _ => throw new BridgeValidationException($"未知的宿主方法：{method}"),
        };
    }

    private object WorkspaceInfo()
    {
        WorkspaceValidationResult validation = WorkspaceDirectoryService.ValidateRoot(_workspaceRoot);
        return new
        {
            root = _workspaceRoot,
            name = Path.GetFileName(_workspaceRoot.TrimEnd(Path.DirectorySeparatorChar)),
            valid = validation.IsValid,
            error = validation.ErrorMessage,
        };
    }

    private object ListDirectory(JsonElement parameters)
    {
        string relative = GetString(parameters, "path") ?? string.Empty;
        string fullPath = ResolveInsideWorkspace(relative);
        // 工作区可能在运行中被删除或断开（例如网络盘）。此时返回可读的失败结果，
        // 而不是把 .NET 的英文路径异常抛给界面。
        if (!Directory.Exists(fullPath))
        {
            return new
            {
                path = relative,
                available = false,
                reason = "目录不存在或无法访问，请确认工作区仍然存在。",
                entries = Array.Empty<object>(),
            };
        }

        IReadOnlyList<WorkspaceEntry> entries = WorkspaceDirectoryService.EnumerateChildren(fullPath);
        return new
        {
            available = true,
            path = relative,
            entries = entries.Select(entry => new
            {
                name = entry.Name,
                path = ToRelative(entry.FullPath),
                isDirectory = entry.IsDirectory,
                canExpand = entry.CanExpand,
                isReparsePoint = entry.IsReparsePoint,
            }),
        };
    }

    private async Task<object?> ReadDocumentAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("document/read 需要 path 参数。");
        string fullPath = ResolveInsideWorkspace(relative);
        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(_workspaceRoot, fullPath, cancellationToken);
        return new
        {
            path = relative,
            name = Path.GetFileName(relative),
            fullPath = result.ResolvedPath,
            workspaceName = Path.GetFileName(_workspaceRoot.TrimEnd(Path.DirectorySeparatorChar)),
            status = result.Status.ToString(),
            kind = result.Classification.Kind.ToString(),
            typeName = result.Classification.TypeName,
            fileSize = result.FileSize,
            text = result.Text,
            pixelWidth = result.PixelWidth,
            pixelHeight = result.PixelHeight,
            message = result.Message,
            // 只有成功读取的普通文本才有编码与磁盘换行事实；图片、过大与
            // 非法 UTF-8 等状态不显示编码，避免让用户以为文件是文本。
            encoding = result.Status == DocumentReadStatus.TextReady ? TextEncodingName : null,
            lineEndings = result.Status == DocumentReadStatus.TextReady
                ? DescribeLineEndings(result.LineEndings)
                : null,
        };
    }

    private async Task<object?> ReadStatusAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable)
        {
            return new { available = false, reason = runtime.UnavailableReason };
        }

        if (repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = true, isRepository = false, reason = "该目录不是带工作区的 Git 仓库。" };
        }

        GitStatusResult status = await ReadStatusCachedAsync(runtime, repository!, cancellationToken);
        if (!status.IsSuccess || status.Snapshot is not { } snapshot)
        {
            return new { available = true, isRepository = true, reason = status.ErrorMessage };
        }

        return new
        {
            available = true,
            isRepository = true,
            repositoryRoot = repository.RepositoryRoot,
            operation = repository.Operation.ToString(),
            hasConflicts = repository.HasConflicts,
            branch = snapshot.CurrentBranch,
            isDetached = snapshot.IsDetached,
            headCommit = snapshot.HeadCommit,
            files = snapshot.Files.Select(file => new
            {
                path = file.RelativePath,
                name = Path.GetFileName(file.RelativePath),
                directory = (Path.GetDirectoryName(file.RelativePath) ?? string.Empty).Replace('\\', '/'),
                group = file.Group.ToString(),
                kind = file.Kind.ToString(),
                // 重命名的原路径：界面需要它说明「从哪个文件改名而来」。
                original = file.OriginalRelativePath,
                staged = file.HasStagedChanges,
                workingTree = file.HasWorkingTreeChanges,
            }),
        };
    }

    /// <summary>
    /// 新建 Worktree（规格 §7.11）。
    /// 目标目录与分支的合法性由 Git 判断；失败回传最新实际状态，不伪造成功。
    /// </summary>
    private async Task<object?> WriteWorktreeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string destination = GetString(parameters, "destination")
            ?? throw new ArgumentException("git/worktree-write 需要 destination 参数。");
        string branch = GetString(parameters, "branch")
            ?? throw new ArgumentException("git/worktree-write 需要 branch 参数。");
        string? newBranch = GetString(parameters, "newBranch");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitActionResult result = await new GitWorktreeService(runtime)
            .CreateAsync(repository!, destination, branch, newBranch, cancellationToken)
            .ConfigureAwait(false);
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, changed = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            changed = true,
            branch = result.ActualStatus?.CurrentBranch,
        };
    }

    /// <summary>
    /// 待推送提交（规格 §7.12 的 Push 预览）。
    /// 没有上游时返回可读说明而不是失败，界面据此保留「定义远端」入口并禁用推送。
    /// </summary>
    private async Task<object?> ReadUnpushedAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, commits = Array.Empty<object>(), upstream = (string?)null };
        }

        GitUnpushedResult result = await new GitHistoryService(runtime)
            .ReadUnpushedAsync(repository!, maximum: 200, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Commits is not { } commits)
        {
            return new
            {
                available = true,
                ready = false,
                reason = result.ErrorMessage,
                commits = Array.Empty<object>(),
                upstream = (string?)null,
            };
        }

        return new
        {
            available = true,
            ready = true,
            // 上游由 git 的 @{u} 解析结果决定；界面不再从分支引用里另推一份，
            // 避免「有上游」与「预览失败」两个来源互相矛盾。
            upstream = result.Upstream,
            commits = commits.Select(commit => new
            {
                fullHash = commit.FullHash,
                hash = commit.ShortHash,
                author = commit.AuthorName,
                date = commit.AuthorDate.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                subject = commit.Subject,
            }),
        };
    }

    private async Task<object?> ReadHistoryAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable)
        {
            return new { available = false, reason = runtime.UnavailableReason };
        }

        if (repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "该目录不是带工作区的 Git 仓库。" };
        }

        GitHistoryService history = new(runtime);
        GitHistoryResult result = await history.ReadPageAsync(
            repository,
            new GitHistoryRequest(Page: 0, PageSize: 100),
            cancellationToken);
        if (!result.IsSuccess || result.Page is not { } page)
        {
            return new { available = true, isRepository = true, reason = result.ErrorMessage };
        }

        // HEAD 由历史条目自带的引用标记给出，避免为取 HEAD 再启动一次 git 进程。
        string? head = page.Entries
            .FirstOrDefault(entry => entry.References.Any(reference => reference.IsHead))
            ?.FullHash;
        return new
        {
            available = true,
            isRepository = true,
            head,
            hasNextPage = page.HasNextPage,
            commits = page.Entries.Select(entry => new
            {
                hash = entry.ShortHash,
                fullHash = entry.FullHash,
                subject = entry.Subject,
                author = entry.AuthorName,
                date = entry.AuthorDate.ToLocalTime()
                    .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                graph = entry.Graph,
                parents = entry.ParentHashes.Select(parent => parent[..Math.Min(7, parent.Length)]).ToArray(),
                references = entry.References.Select(reference => reference.Name).ToArray(),
            }),
        };
    }

    /// <summary>
    /// 克隆仓库。目标目录必须为空或不存在：宁可在调用 Git 之前明确失败，
    /// 也不要让 Git 在半途报出难以理解的错误。
    /// 深度为空表示完整克隆。
    /// </summary>
    private async Task<object?> CloneAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string source = (GetString(parameters, "source") ?? string.Empty).Trim();
        string destination = (GetString(parameters, "destination") ?? string.Empty).Trim();
        int? depth = GetInt(parameters, "depth");
        if (source.Length == 0)
        {
            return new { available = false, field = "source", reason = "请填写仓库地址。" };
        }

        if (destination.Length == 0)
        {
            return new { available = false, field = "destination", reason = "请选择目标目录。" };
        }

        if (depth is { } value && value < 1)
        {
            return new { available = false, field = "depth", reason = "浅克隆深度必须是正整数。" };
        }

        string fullDestination;
        try
        {
            fullDestination = Path.GetFullPath(destination);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new { available = false, field = "destination", reason = "目标目录路径不合法。" };
        }

        if (Directory.Exists(fullDestination) && Directory.EnumerateFileSystemEntries(fullDestination).Any())
        {
            return new { available = false, field = "destination", reason = "目标目录不为空，请换一个目录。" };
        }

        (GitRuntimeInfo runtime, _) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable)
        {
            return new { available = false, reason = "未找到可用的 Git。" };
        }

        GitRepositoryService repositories = new(runtime);
        GitRepositoryOperationResult result = await repositories.CloneAsync(
            source,
            fullDestination,
            depth,
            cancellationToken);
        return new
        {
            available = result.IsSuccess,
            reason = result.ErrorMessage,
            path = result.IsSuccess ? fullDestination : null,
        };
    }

    /// <summary>
    /// 快速打开：按文件名搜索，最多 100 项。
    /// 上限、超时与取消语义都由搜索服务负责，这里只做参数整形与结果映射。
    /// </summary>
    /// <summary>
    /// 回滚一个改动文件（规格 §10.4）。
    /// 文件身份以**宿主自己的状态快照**为准，不采信页面传来的 kind：
    /// 页面状态可能已经过期，而回滚对已跟踪文件是"恢复到 HEAD"、对未跟踪/新增文件是
    /// "移入回收站"，用错身份会做出用户没要求的破坏性操作。
    /// </summary>
    private async Task<object?> RollbackAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string path = GetString(parameters, "path")
            ?? throw new ArgumentException("git/rollback 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitStatusResult status = await ReadStatusCachedAsync(runtime, repository, cancellationToken);
        if (!status.IsSuccess || status.Snapshot is not { } snapshot)
        {
            return new { available = true, rolledBack = false, reason = status.ErrorMessage ?? "无法读取当前改动列表。" };
        }

        string normalized = path.Replace('\\', '/');
        GitChangedFile? file = snapshot.Files.FirstOrDefault(
            item => string.Equals(item.RelativePath.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return new { available = true, rolledBack = false, reason = "该文件已不在改动列表中，可能已被外部处理。" };
        }

        GitActionResult result = await new GitWorkspaceStateService(runtime)
            .RollbackAsync(repository, file, cancellationToken)
            .ConfigureAwait(false);
        // 回滚改变了工作区：状态缓存必须失效，界面随后重新读取真实仓库状态。
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, rolledBack = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            rolledBack = true,
            path = file.RelativePath,
            // 未跟踪或新增文件是被移入回收站，不是从 HEAD 恢复：界面据此给出准确说明。
            recycled = file.Group == GitChangeGroup.UnversionedFiles
                || file.Kind is GitChangeKind.Untracked or GitChangeKind.Added,
        };
    }

    private async Task<object?> SearchFilesAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string query = GetString(parameters, "query") ?? string.Empty;
        RipgrepSearchService search = new(RipgrepPath);
        FileSearchResultSet result = await search.SearchFilesWithStatusAsync(
            _workspaceRoot,
            query,
            cancellationToken);
        return new
        {
            available = true,
            timedOut = result.IsTimedOut,
            cancelled = result.IsCancelled,
            notice = result.Notice,
            matches = result.Matches.Select(match => new
            {
                path = match.RelativePath,
                name = Path.GetFileName(match.RelativePath),
                directory = (Path.GetDirectoryName(match.RelativePath) ?? string.Empty).Replace('\\', '/'),
            }),
        };
    }

    /// <summary>全仓搜索：按内容搜索，最多 1000 项并标记截断。</summary>
    private async Task<object?> SearchTextAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string query = GetString(parameters, "query") ?? string.Empty;
        SearchOptions options = new(
            query,
            GetBool(parameters, "matchCase") ?? false,
            GetBool(parameters, "matchWholeWord") ?? false,
            GetBool(parameters, "useRegularExpression") ?? false,
            GetBool(parameters, "includeIgnoredFiles") ?? false);
        RipgrepSearchService search = new(RipgrepPath);
        TextSearchResult result = await search.SearchTextAsync(_workspaceRoot, options, cancellationToken);
        return new
        {
            available = true,
            truncated = result.IsTruncated,
            timedOut = result.IsTimedOut,
            cancelled = result.IsCancelled,
            notice = result.Notice,
            matches = result.Matches.Select(match => new
            {
                path = match.RelativePath,
                name = Path.GetFileName(match.RelativePath),
                directory = (Path.GetDirectoryName(match.RelativePath) ?? string.Empty).Replace('\\', '/'),
                line = match.LineNumber,
                column = match.ColumnNumber,
                text = match.LineText,
            }),
        };
    }

    /// <summary>
    /// 读取并清空累积的文件系统变化。
    /// 网页层轮询这个入口：宿主不主动推送，桥接保持单一的请求/应答形状。
    /// </summary>
    private object ReadWorkspaceChanges()
    {
        EnsureWatchers();
        string[] files;
        bool gitMetadata;
        lock (_changeGate)
        {
            files = [.. _pendingWorkspaceChanges];
            _pendingWorkspaceChanges.Clear();
            gitMetadata = _pendingGitMetadataChange;
            _pendingGitMetadataChange = false;
        }

        return new
        {
            available = true,
            files,
            gitMetadata,
        };
    }

    /// <summary>
    /// 惰性建立文件系统监视：只有界面开始轮询时才创建，
    /// 避免不使用的工作区也常驻监视句柄。
    /// </summary>
    private void EnsureWatchers()
    {
        if (_workspaceWatcher is not null)
        {
            return;
        }

        lock (_changeGate)
        {
            if (_workspaceWatcher is not null)
            {
                return;
            }

            try
            {
                _workspaceWatcher = new WorkspaceFileWatcher(_workspaceRoot);
                _workspaceWatcher.Changed += OnWorkspaceFilesChanged;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 监视不可用不影响其它功能：界面只是不会自动刷新。
                _workspaceWatcher = null;
            }
        }
    }

    private void OnWorkspaceFilesChanged(object? sender, FileChangeBatchEventArgs eventArgs)
    {
        InvalidateStatusCache();
        string[] batch;
        lock (_changeGate)
        {
            foreach (string path in eventArgs.Paths)
            {
                _pendingWorkspaceChanges.Add(path);
            }

            batch = [.. _pendingWorkspaceChanges];
        }

        // 主动推送，网页层无需轮询；读取时仍会返回同一批次，两者幂等。
        _notify?.Invoke("workspace-changed", new { files = batch, gitMetadata = false });
    }

    /// <summary>
    /// 注册 Git 元数据监视。与工作区监视分开：`.git` 变化需要刷新状态与历史，
    /// 普通文件变化只需要刷新受影响的那一个文件。
    /// </summary>
    private void EnsureGitWatcher(GitRepositorySnapshot repository)
    {
        if (_gitWatcher is not null || repository.GitDirectory is null)
        {
            return;
        }

        try
        {
            _gitWatcher = new GitMetadataWatcher(repository);
            _gitWatcher.Changed += (_, _) =>
            {
                InvalidateStatusCache();
                lock (_changeGate)
                {
                    _pendingGitMetadataChange = true;
                }

                _notify?.Invoke("workspace-changed", new { files = Array.Empty<string>(), gitMetadata = true });
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _gitWatcher = null;
        }
    }

    public void Dispose()
    {
        _workspaceWatcher?.Dispose();
        _gitWatcher?.Dispose();
    }

    /// <summary>
    /// 读取 Git 状态，并在极短窗口内复用结果。
    /// 状态是冲突检测与差异读取的共同前置步骤，一次界面刷新会重复请求它；
    /// 缓存窗口很短（见 StatusCacheLifetime），文件系统变化会立即使其失效，
    /// 因此不会让界面看到过期状态。
    /// </summary>
    private async Task<GitStatusResult> ReadStatusCachedAsync(
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken)
    {
        lock (_statusGate)
        {
            if (_cachedStatus is not null
                && Environment.TickCount64 - _cachedStatusAt < StatusCacheLifetimeMs)
            {
                return _cachedStatus;
            }
        }

        GitStatusService service = new(runtime);
        GitStatusResult status = await service.ReadAsync(repository!, cancellationToken);
        if (status.IsSuccess)
        {
            lock (_statusGate)
            {
                _cachedStatus = status;
                _cachedStatusAt = Environment.TickCount64;
            }
        }

        return status;
    }

    /// <summary>让状态缓存失效；任何文件系统变化都必须调用它。</summary>
    private void InvalidateStatusCache()
    {
        lock (_statusGate)
        {
            _cachedStatus = null;
        }
    }

    /// <summary>
    /// 用系统程序打开仓库内的路径：在资源管理器中定位、在外部终端打开（规格 §5.4 项目树菜单）。
    /// </summary>
    /// <remarks>
    /// **必须**校验路径位于当前工作区内：这个方法最终会启动进程，
    /// 网页层若能传入任意路径就等于获得了任意程序启动能力。
    /// 越界一律拒绝，并如实给出原因。
    /// </remarks>
    private Task<object?> LaunchExternalAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string action = GetString(parameters, "action")
            ?? throw new BridgeValidationException("external/launch 需要 action 参数。");
        string relative = GetString(parameters, "path")
            ?? throw new BridgeValidationException("external/launch 需要 path 参数。");

        // 必须走 SafeLocalPathResolver 而不是自己拼路径 + IsWithin：
        // 后者只比较**字面**路径，工作区内的符号链接（junction/symlink）指向外部时会被放行，
        // 于是"在外部终端打开"会把终端开在工作区之外。该解析器逐段解析 reparse point
        // 并核对目标是否仍在工作区内，且已有测试覆盖（WorkspaceDirectoryServiceTests）。
        string candidate = Path.GetFullPath(Path.Combine(
            _workspaceRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        string? fullPath = SafeLocalPathResolver.ResolveWithinWorkspace(_workspaceRoot, candidate);
        if (fullPath is null)
        {
            return Task.FromResult<object?>(new
            {
                launched = false,
                reason = "只能打开当前工作区内已存在的路径。",
            });
        }

        ExternalLaunchResult result = action switch
        {
            // 规格 §7.5/§7.14：无法在三栏里处理的文件交给系统默认程序。
            "open" => ExternalProgramLauncher.OpenWithDefaultApplication(fullPath),
            // 规格 §7.10：Worktree 在自己的 Augit 窗口里打开（当前程序 + 该目录）。
            "augit" => Environment.ProcessPath is { Length: > 0 } executable
                ? ExternalProgramLauncher.OpenAugitWorkspace(executable, fullPath)
                : ExternalLaunchResult.Failure("无法确定当前程序路径。"),
            "reveal" => ExternalProgramLauncher.RevealInExplorer(fullPath),
            "terminal" => ExternalProgramLauncher.OpenExternalTerminal(fullPath),
            _ => ExternalLaunchResult.Failure("不支持的外部打开方式。"),
        };
        return Task.FromResult<object?>(new
        {
            launched = result.IsSuccess,
            reason = result.ErrorMessage,
        });
    }

    /// <summary>
    /// 把文本写入系统剪贴板（规格 §5.4 项目树菜单的"复制路径"）。
    /// </summary>
    /// <remarks>
    /// 由宿主执行而不是网页层：WebView2 默认不授予 `ClipboardApiRequested`，
    /// 页面里 `navigator.clipboard.writeText` 会静默失败（无头 Chromium 里实测为 null）。
    /// Win32 调用的具体实现放在基础设施层的 <see cref="ClipboardText"/>，那里可被测试覆盖。
    /// </remarks>
    private static object WriteClipboard(JsonElement parameters)
    {
        string text = GetString(parameters, "text") ?? string.Empty;
        ClipboardWriteResult result = ClipboardText.TrySetText(text);
        return new
        {
            copied = result.IsSuccess,
            reason = result.ErrorMessage,
        };
    }

    /// <summary>读取当前设置。</summary>
    private async Task<object?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        SettingsStore store = new();
        ApplicationSettings settings = await store.LoadAsync(cancellationToken);
        bool sameWorkspace = string.Equals(
            settings.LastWorkspace,
            _workspaceRoot,
            StringComparison.OrdinalIgnoreCase);
        return new
        {
            theme = settings.Theme,
            textFontFamily = settings.TextFontFamily,
            monospaceFontFamily = settings.MonospaceFontFamily,
            fontSize = settings.UiFontSize,
            codeFontSize = settings.FontSize,
            gitExecutablePath = settings.GitExecutablePath,
            terminalShell = settings.TerminalShell,
            terminalCustomCommand = settings.TerminalCustomCommand,
            recentWorkspaces = settings.RecentWorkspaces,
            // 已保存的面板尺寸：只在该尺寸属于当前工作区时下发，
            // 否则会把另一个工作区的布局套到今天打开的目录上。
            // 会话恢复数据只在同一工作区内有意义：跨工作区恢复会试图打开别的仓库里的文件。
            openFiles = sameWorkspace ? settings.OpenFiles : [],
            activeFile = sameWorkspace ? settings.ActiveFile : null,
            // 界面上的树用**工作区相对路径**，因此读取时相对化；写入时再还原成绝对路径。
            expandedDirectories = sameWorkspace ? RelativeDirectories(settings.ExpandedDirectories) : [],
            projectPanelWidth = sameWorkspace ? settings.ToolWindows.ProjectPanelWidth : null,
            bottomPanelHeight = sameWorkspace ? settings.ToolWindows.BottomPanelHeight : null,
        };
    }

    /// <summary>
    /// 写入设置。只接受已知字段：未知字段被忽略，字号等数值先做范围校验，
    /// 避免把非法值写进设置文件。
    /// </summary>
    /// <summary>
    /// 写入会话恢复数据（上次打开的文件与当前文件，规格 §6.7）。
    ///
    /// 单独一条通道而不是复用 settings/write：后者会失效 Git 解析与状态缓存，
    /// 而"关掉一个标签"不该让界面重新查一遍 Git（§6.1 局部更新与性能要求）。
    /// </summary>
    private async Task<object?> WriteSessionAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        SettingsStore store = new();
        ApplicationSettings current = await store.LoadAsync(cancellationToken);
        string[]? directories = GetStringArray(parameters, "expandedDirectories");
        // Normalize 会去重、限量并把 ActiveFile 校正到列表之内。
        ApplicationSettings updated = current with
        {
            OpenFiles = GetStringArray(parameters, "openFiles") ?? current.OpenFiles,
            ActiveFile = GetString(parameters, "activeFile") ?? current.ActiveFile,
            // 目录展开层级由界面按工作区相对路径发来，这里还原成绝对路径，
            // 由设置层校验"必须落在工作区内"（相对路径交给 Path.GetFullPath 会被
            // 解析到进程当前目录，那是错的）。
            ExpandedDirectories = directories is null
                ? current.ExpandedDirectories
                : [.. directories.Select(path => Path.GetFullPath(Path.Combine(_workspaceRoot, path)))],
            // 设置层只在 LastWorkspace 与本次工作区一致时才保留会话数据，
            // 因此这里必须记下当前工作区，否则展开层级下次启动会被清空。
            LastWorkspace = _workspaceRoot ?? current.LastWorkspace,
        };
        await store.SaveAsync(updated, cancellationToken);
        return new { saved = true };
    }

    /// <summary>
    /// 把设置里保存的目录展开层级转成界面使用的工作区相对路径（正斜杠）。
    /// 越界或无法相对化的条目直接丢弃——它是界面状态，宁可少恢复一层。
    /// </summary>
    private string[] RelativeDirectories(IEnumerable<string> directories)
    {
        List<string> values = [];
        foreach (string directory in directories)
        {
            string relative;
            try
            {
                relative = Path.GetRelativePath(_workspaceRoot, directory);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (relative.Length > 0 && relative != "." && !relative.StartsWith("..", StringComparison.Ordinal))
            {
                values.Add(relative.Replace('\\', '/'));
            }
        }

        return [.. values];
    }

    /// <summary>读取字符串数组参数；缺失或不是数组时返回 null（表示"本次不修改"）。</summary>
    private static string[]? GetStringArray(JsonElement parameters, string name)
    {
        if (!parameters.TryGetProperty(name, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
        }

        return [.. values];
    }

    private async Task<object?> WriteSettingsAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        SettingsStore store = new();
        ApplicationSettings current = await store.LoadAsync(cancellationToken);
        ApplicationSettings updated = current with
        {
            Theme = NormalizeTheme(GetString(parameters, "theme")) ?? current.Theme,
            TextFontFamily = GetString(parameters, "textFontFamily") ?? current.TextFontFamily,
            MonospaceFontFamily = GetString(parameters, "monospaceFontFamily") ?? current.MonospaceFontFamily,
            FontSize = ClampFontSize(GetDouble(parameters, "codeFontSize")) ?? current.FontSize,
            TextFontSize = ClampFontSize(GetDouble(parameters, "fontSize")) ?? current.UiFontSize,
            GitExecutablePath = GetString(parameters, "gitExecutablePath") ?? current.GitExecutablePath,
            // 枚举型字段只接受已知取值：写入未知值会让界面显示与运行时行为不一致
            // （例如主题存成 "Purple"，界面照存，运行时却按深色回退）。
            TerminalShell = NormalizeTerminalShell(GetString(parameters, "terminalShell")) ?? current.TerminalShell,
            TerminalCustomCommand = GetString(parameters, "terminalCustomCommand") ?? current.TerminalCustomCommand,
        };
        await store.SaveAsync(updated, cancellationToken);
        // 设置写入后让 Git 解析缓存与状态缓存失效：
        // 用户可能刚修正了 git.exe 路径，若沿用上一次「未找到」的结果，
        // Git 会一直保持不可用直到重启（实测确实如此）。
        if (!string.Equals(current.GitExecutablePath, updated.GitExecutablePath, StringComparison.Ordinal))
        {
            _gitResolution = null;
        }

        InvalidateStatusCache();
        return new { saved = true, theme = updated.Theme, fontSize = updated.UiFontSize };
    }

    /// <summary>主题只接受三个已知取值；未知取值一律忽略，保留原值。</summary>
    private static string? NormalizeTheme(string? theme)
    {
        if (theme is null)
        {
            return null;
        }

        return theme.Equals("System", StringComparison.OrdinalIgnoreCase) ? "System"
            : theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light"
            : theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark"
            : null;
    }

    /// <summary>终端 Shell 只接受已登记的标识；未知取值一律忽略，保留原值。</summary>
    private static string? NormalizeTerminalShell(string? shell)
    {
        if (shell is null)
        {
            return null;
        }

        return shell is TerminalShellIds.WindowsPowerShell
            or TerminalShellIds.PowerShell7
            or TerminalShellIds.CommandPrompt
            or TerminalShellIds.GitBash
            or TerminalShellIds.Wsl
            or TerminalShellIds.Custom
            ? shell
            : null;
    }

    private static double? ClampFontSize(double? value)
    {
        return value is { } size && size is >= 9 and <= 40 ? size : null;
    }

    private static double? GetDouble(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : null;
    }

    /// <summary>
    /// 启动内置终端。终端是独占的单会话资源：已有会话时先结束再启动，
    /// 避免留下孤儿 Shell 进程。
    /// </summary>
    private async Task<object?> StartTerminalAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        await StopTerminalAsync();
        int columns = GetInt(parameters, "columns") ?? 80;
        int rows = GetInt(parameters, "rows") ?? 24;
        SettingsStore store = new();
        ApplicationSettings settings = await store.LoadAsync(cancellationToken);
        TerminalLaunchResult launch = TerminalShellResolver.Resolve(settings, _workspaceRoot);
        if (!launch.IsSuccess || launch.LaunchInfo is not { } launchInfo)
        {
            return new { available = false, reason = launch.ErrorMessage };
        }

        ConPtyTerminalSession session = ConPtyTerminalSession.Start(
            launchInfo,
            _workspaceRoot,
            Math.Clamp(columns, 20, 500),
            Math.Clamp(rows, 5, 200));
        lock (_terminalGate)
        {
            _terminal = session;
            _terminalBuffer.Clear();
        }

        session.OutputReceived += OnTerminalOutput;
        session.Exited += OnTerminalExited;
        return new
        {
            available = true,
            shellId = launchInfo.ShellId,
            displayName = launchInfo.DisplayName,
        };
    }

    private void OnTerminalOutput(object? sender, string data)
    {
        lock (_terminalGate)
        {
            _terminalBuffer.Append(data);
            // 只保留最近 2000 行对应的上限，避免长时间运行后内存无界增长。
            if (_terminalBuffer.Length > MaximumTerminalBufferLength)
            {
                _terminalBuffer.Remove(0, _terminalBuffer.Length - MaximumTerminalBufferLength);
                _terminalOffset = Math.Max(0, _terminalOffset - (_terminalBuffer.Length - MaximumTerminalBufferLength));
            }
        }
    }

    private void OnTerminalExited(object? sender, int exitCode)
    {
        lock (_terminalGate)
        {
            _terminalExited = true;
            _terminalExitCode = exitCode;
        }
    }

    /// <summary>增量读取终端输出：网页层按偏移量轮询，避免重复传输。</summary>
    private object ReadTerminal(JsonElement parameters)
    {
        long offset = GetLong(parameters, "offset") ?? 0;
        lock (_terminalGate)
        {
            long start = Math.Clamp(offset, 0, _terminalBuffer.Length);
            string chunk = _terminalBuffer.ToString((int)start, (int)(_terminalBuffer.Length - start));
            return new
            {
                available = _terminal is not null,
                running = _terminal?.IsRunning ?? false,
                exited = _terminalExited,
                exitCode = _terminalExitCode,
                offset = _terminalBuffer.Length,
                data = chunk,
            };
        }
    }

    private async Task<object?> WriteTerminalAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string data = GetString(parameters, "data") ?? string.Empty;
        ConPtyTerminalSession? session;
        lock (_terminalGate)
        {
            session = _terminal;
        }

        if (session is null || !session.IsRunning)
        {
            return new { available = false };
        }

        await session.WriteAsync(data, cancellationToken);
        return new { available = true };
    }

    private object ResizeTerminal(JsonElement parameters)
    {
        int columns = GetInt(parameters, "columns") ?? 80;
        int rows = GetInt(parameters, "rows") ?? 24;
        ConPtyTerminalSession? session;
        lock (_terminalGate)
        {
            session = _terminal;
        }

        if (session is not null && session.IsRunning)
        {
            session.Resize(Math.Clamp(columns, 20, 500), Math.Clamp(rows, 5, 200));
        }

        return new { available = session is not null };
    }

    private async Task<object?> StopTerminalAsync()
    {
        ConPtyTerminalSession? session;
        lock (_terminalGate)
        {
            session = _terminal;
            _terminal = null;
        }

        if (session is null)
        {
            return new { available = false };
        }

        session.OutputReceived -= OnTerminalOutput;
        session.Exited -= OnTerminalExited;
        await session.StopAsync();
        session.Dispose();
        lock (_terminalGate)
        {
            _terminalExited = true;
        }

        return new { available = true };
    }

    /// <summary>读取当前冲突会话与冲突文件列表。</summary>
    /// <summary>
    /// 读取当前 Git 操作会话（规格 §7.13）：操作类型、是否进行中、冲突文件、当前步骤
    /// 与实际可用动作。宿主本来就实现了这套判定，但此前桥接没有暴露，
    /// 界面因此既看不到会话、也无法继续/跳过/中止。
    /// </summary>
    private async Task<object?> InspectOperationAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitAdvancedOperationResult result = await new GitOperationService(runtime)
            .InspectAsync(repository, cancellationToken)
            .ConfigureAwait(false);
        return OperationPayload(result);
    }

    /// <summary>
    /// 执行操作会话动作（继续 / 跳过 / 中止，规格 §7.13）。
    /// 可用性由宿主判定：不支持的动作直接拒绝，界面只显示真实可用的动作。
    /// </summary>
    private async Task<object?> RunOperationActionAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string name = GetString(parameters, "action")
            ?? throw new ArgumentException("git/operation-action 需要 action 参数。");
        if (!Enum.TryParse(name, ignoreCase: true, out GitOperationAction action))
        {
            throw new ArgumentException($"未知的操作会话动作：{name}。");
        }

        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitAdvancedOperationResult result = await new GitOperationService(runtime)
            .ExecuteActionAsync(repository, action, cancellationToken)
            .ConfigureAwait(false);
        // 会话动作会改写工作区与 HEAD：缓存必须失效，界面随后读取真实状态（§9.3）。
        InvalidateStatusCache();
        return OperationPayload(result);
    }

    /// <summary>操作会话的对外形状；宿主判定可用动作，界面只显示真实可用的那些。</summary>
    private static object OperationPayload(GitAdvancedOperationResult result)
    {
        GitOperationSession? session = result.Session;
        return new
        {
            available = true,
            ok = result.IsSuccess,
            reason = result.ErrorMessage,
            session = session is null ? null : new
            {
                kind = session.Kind.ToString(),
                inProgress = session.IsInProgress,
                hasConflicts = session.HasConflicts,
                branch = session.CurrentBranch,
                canContinue = session.CanContinue,
                canSkip = session.CanSkip,
                canAbort = session.CanAbort,
                supportsContinue = session.SupportsContinue,
                currentStep = session.CurrentStep,
                totalSteps = session.TotalSteps,
                conflicts = session.ConflictFiles.Select(file => new
                {
                    path = file.RelativePath,
                    hasAncestor = file.HasAncestor,
                    hasYours = file.HasYours,
                    hasTheirs = file.HasTheirs,
                }).ToArray(),
            },
        };
    }

    /// <summary>
    /// 整侧接受一个冲突文件（规格 §7.14 最后一条）：二进制、非法 UTF-8 与超限文件
    /// 无法逐块合并，只能整侧取一侧。语义与三栏里的"接受左侧/右侧"**不同**——
    /// 那三个按钮是结果区的一次可撤销编辑（页面侧），走的是 `git/conflict-save`。
    /// </summary>
    private async Task<object?> AcceptConflictSideAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/conflict-accept 需要 path 参数。");
        string sideName = GetString(parameters, "side")
            ?? throw new ArgumentException("git/conflict-accept 需要 side 参数。");
        if (!Enum.TryParse(sideName, ignoreCase: true, out GitConflictSide side))
        {
            throw new ArgumentException($"未知的冲突侧：{sideName}。");
        }

        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitConflictMutationResult result = await new GitConflictService(runtime)
            .AcceptSideAsync(repository!, relative, side, cancellationToken)
            .ConfigureAwait(false);
        // 整侧接受改写了工作区：缓存失效，界面随后读取真实状态。
        InvalidateStatusCache();
        return new
        {
            available = result.IsSuccess,
            reason = result.ErrorMessage,
            hasConflicts = result.Session?.HasConflicts,
        };
    }

    private async Task<object?> ReadConflictsAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, files = Array.Empty<object>(), operation = "None" };
        }

        GitStatusResult status = await ReadStatusCachedAsync(runtime, repository!, cancellationToken);
        if (!status.IsSuccess || status.Snapshot is not { } snapshot)
        {
            return new { available = false, reason = status.ErrorMessage, files = Array.Empty<object>() };
        }

        // 仓库已确认可用，此时建立元数据监视（.git 变化触发状态与历史刷新）。
        EnsureGitWatcher(repository!);

        return new
        {
            available = true,
            operation = repository!.Operation.ToString(),
            hasConflicts = repository.HasConflicts,
            files = snapshot.Files
                .Where(file => file.Kind == GitChangeKind.Unmerged)
                .Select(file => new
                {
                    path = file.RelativePath,
                    name = Path.GetFileName(file.RelativePath),
                    directory = (Path.GetDirectoryName(file.RelativePath) ?? string.Empty).Replace('\\', '/'),
                }),
        };
    }

    /// <summary>读取单个冲突文件的三栏内容与冲突块。</summary>
    private async Task<object?> LoadConflictAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/conflict-load 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false };
        }

        GitConflictService conflicts = new(runtime);
        GitConflictLoadResult result = await conflicts.LoadAsync(repository!, relative, cancellationToken);
        if (!result.IsSuccess || result.Document is not { } document)
        {
            return new { available = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            path = relative,
            contentKind = document.ContentKind.ToString(),
            yoursLabel = document.YoursLabel,
            theirsLabel = document.TheirsLabel,
            yoursText = document.YoursText,
            theirsText = document.TheirsText,
            resultText = document.ResultText,
            operation = document.Operation.ToString(),
            version = document.FileVersion is { } version
                ? new { length = version.Length, sha256 = version.Sha256, lastWriteUtc = version.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) }
                : null,
            blocks = document.Blocks.Select(block => new
            {
                start = block.Start,
                length = block.Length,
                yours = block.YoursText,
                ancestor = block.AncestorText,
                theirs = block.TheirsText,
            }),
        };
    }

    /// <summary>写入冲突解决结果。版本不一致时拒绝保存，避免覆盖外部改动。</summary>
    private async Task<object?> SaveConflictAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/conflict-save 需要 path 参数。");
        string resultText = GetString(parameters, "resultText") ?? string.Empty;
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitConflictService conflicts = new(runtime);
        GitConflictLoadResult loaded = await conflicts.LoadAsync(repository!, relative, cancellationToken);
        if (!loaded.IsSuccess || loaded.Document?.FileVersion is not { } version)
        {
            return new { available = false, reason = loaded.ErrorMessage ?? "无法读取冲突文件版本。" };
        }

        GitConflictMutationResult saved = await conflicts.SaveResolvedAsync(
            repository!,
            new GitConflictSaveRequest(relative, resultText, version, loaded.Document.Operation),
            cancellationToken);
        return new
        {
            available = saved.IsSuccess,
            reason = saved.ErrorMessage,
            hasConflicts = saved.Session?.HasConflicts,
            conflictCount = saved.Session?.ConflictFiles.Count,
        };
    }

    /// <summary>
    /// 读取工作区中某个文件的差异。
    /// 返回结构化行而不是原始补丁：网页层要按「旧行 / 行号槽 / 新行」三列渲染，
    /// 让解析与分栏规则留在核心层，两侧保持一致。
    /// </summary>
    private async Task<object?> ReadDiffAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/diff 需要 path 参数。");
        bool ignoreWhitespace = GetBool(parameters, "ignoreWhitespace") ?? false;
        // 引用比较（规格 §7.9）传入具体基准；工作区 Diff 不传，默认对比 HEAD。
        string? revision = GetString(parameters, "revision");
        // 历史比较（规格 §7.8）传入提交版本：此时两侧都取自 Git，
        // 左侧由宿主解析为该提交的父提交，调用方不必自己推导。
        string? commit = GetString(parameters, "commit");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, rows = Array.Empty<object>(), lines = Array.Empty<object>() };
        }

        GitChangedFile? changed;
        string baseRevision;
        string? targetRevision = null;
        if (!string.IsNullOrWhiteSpace(commit))
        {
            // 父版本解析放在基础设施层：GitCommandRunner 是内部类型，
            // 外壳不能直接驱动 Git 命令。
            string? parent = await new GitHistoryService(runtime).ResolveParentRevisionAsync(
                repository!.RepositoryRoot!,
                commit,
                cancellationToken);
            if (parent is null)
            {
                return new
                {
                    available = false,
                    reason = "无法解析该提交的父版本。",
                    rows = Array.Empty<object>(),
                    lines = Array.Empty<object>(),
                };
            }

            baseRevision = parent;
            targetRevision = commit;
            // 历史比较的文件不必出现在改动列表里，直接按路径构造比较对象。
            changed = new GitChangedFile(relative, null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true);
        }
        else if (!string.IsNullOrWhiteSpace(revision))
        {
            // 引用比较的目标文件不必出现在当前改动列表里——它比较的是
            // 「该引用与工作区」的差异，因此直接按路径构造比较对象。
            baseRevision = revision;
            changed = new GitChangedFile(relative, null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true);
        }
        else
        {
            baseRevision = "HEAD";
            GitStatusResult status = await ReadStatusCachedAsync(runtime, repository!, cancellationToken);
            changed = status.Snapshot?.Files
                .FirstOrDefault(file => string.Equals(file.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
            if (changed is null)
            {
                return new { available = false, reason = "该文件当前没有改动。", rows = Array.Empty<object>(), lines = Array.Empty<object>() };
            }
        }

        GitDiffService diffService = new(runtime);
        GitDiffResult result = await diffService.CreateAsync(
            repository!,
            changed,
            new GitDiffOptions(ignoreWhitespace, baseRevision, targetRevision),
            cancellationToken);
        if (!result.IsSuccess || result.Document is not { } document)
        {
            return new
            {
                available = false,
                reason = result.ErrorMessage,
                rows = Array.Empty<object>(),
                lines = Array.Empty<object>(),
            };
        }

        if (!document.HasTextDiff)
        {
            return new
            {
                available = true,
                path = document.RelativePath,
                status = document.Status.ToString(),
                rows = Array.Empty<object>(),
                lines = Array.Empty<object>(),
            };
        }

        IReadOnlyList<GitDiffLine> lines = GitUnifiedDiffParser.Parse(document.UnifiedPatch!);
        IReadOnlyList<GitSideBySideRow> rows = GitUnifiedDiffParser.ToSideBySide(lines);
        // 超大差异只回传前若干行：整份补丁可能有上万行，全量下发既拖慢渲染也没有阅读价值。
        const int maximumRows = 2000;
        bool truncated = rows.Count > maximumRows;
        if (truncated)
        {
            rows = rows.Take(maximumRows).ToArray();
        }
        return new
        {
            available = true,
            path = document.RelativePath,
            status = document.Status.ToString(),
            oldSize = document.OldSize,
            newSize = document.NewSize,
            truncated,
            lines = lines.Select(line => new
            {
                kind = line.Kind.ToString(),
                oldLine = line.OldLineNumber,
                newLine = line.NewLineNumber,
                text = line.Text,
            }),
            rows = rows.Select(row => new
            {
                oldLine = row.OldLineNumber,
                oldText = row.OldText,
                oldChanges = row.OldChanges.Select(span => new { start = span.Start, length = span.Length }),
                newLine = row.NewLineNumber,
                newText = row.NewText,
                newChanges = row.NewChanges.Select(span => new { start = span.Start, length = span.Length }),
                kind = row.Kind.ToString(),
            }),
        };
    }

    /// <summary>
    /// 换行格式的界面用名。规格 §4.1 要求 LF / CRLF / CR / 混合换行 / 无换行；
    /// 未识别到换行符时返回空串，界面据此不显示该字段。
    /// </summary>
    /// <summary>
    /// 状态缓存的有效期。只覆盖「同一次界面刷新内的重复请求」这一窗口；
    /// 用户可见的数据变化都会通过文件系统监视使缓存失效。
    /// </summary>
    private const int StatusCacheLifetimeMs = 300;

    /// <summary>随包分发的 ripgrep；缺失时搜索功能明确失败，不静默降级。</summary>
    private static string RipgrepPath => Path.Combine(AppContext.BaseDirectory, "tools", "rg.exe");

    /// <summary>Augit 的普通文件查看器只读取 UTF-8（可带 BOM），因此文本编码固定为 UTF-8。</summary>
    private const string TextEncodingName = "UTF-8";

    private static string DescribeLineEndings(DocumentLineEndings endings) => endings switch
    {
        DocumentLineEndings.Lf => "LF",
        DocumentLineEndings.CrLf => "CRLF",
        DocumentLineEndings.Cr => "CR",
        DocumentLineEndings.Mixed => "混合换行",
        DocumentLineEndings.None => "无换行",
        _ => string.Empty,
    };

    private static bool? GetBool(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out JsonElement element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
    }

    /// <summary>读取远端列表。</summary>
    private async Task<object?> ReadRemotesAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, remotes = Array.Empty<object>() };
        }

        GitRemoteService remotes = new(runtime);
        GitRemoteListResult result = await remotes.ReadRemotesAsync(repository!, cancellationToken);
        if (!result.IsSuccess || result.Remotes is not { } list)
        {
            return new { available = false, reason = result.ErrorMessage, remotes = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            remotes = list.Select(remote => new
            {
                name = remote.Name,
                fetchUrl = remote.FetchUrl,
                pushUrl = remote.PushUrl,
            }),
        };
    }

    /// <summary>读取分支与标签。</summary>
    private async Task<object?> ReadReferencesAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, branches = Array.Empty<object>(), tags = Array.Empty<object>() };
        }

        GitReferenceService references = new(runtime);
        GitReferenceResult result = await references.ReadAsync(repository!, cancellationToken);
        if (!result.IsSuccess || result.Snapshot is not { } snapshot)
        {
            return new
            {
                available = false,
                reason = result.ErrorMessage,
                branches = Array.Empty<object>(),
                tags = Array.Empty<object>(),
            };
        }

        return new
        {
            available = true,
            branches = snapshot.Branches.Select(branch => new
            {
                name = branch.Name,
                isRemote = branch.IsRemote,
                isCurrent = branch.IsCurrent,
                upstream = branch.Upstream,
                commitHash = branch.CommitHash,
                subject = branch.Subject,
            }),
            tags = snapshot.Tags.Select(tag => new
            {
                name = tag.Name,
                commitHash = tag.CommitHash,
                isAnnotated = tag.IsAnnotated,
            }),
        };
    }

    /// <summary>读取 Stash 列表。</summary>
    private async Task<object?> ReadStashesAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, stashes = Array.Empty<object>() };
        }

        return await ProjectStashesAsync(runtime, repository!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建 Stash（规格 §5.3：支持 stash；视觉稿的对话框给"消息"与"保留索引状态"）。
    ///
    /// 未跟踪文件默认一并暂存：产品规格把未跟踪文件视为工作区改动的一部分
    /// （Smart Checkout 的影响说明也是"已跟踪文件和未跟踪文件"），视觉稿的对话框没有
    /// 关闭这个行为的开关，因此不给界面一个"看起来可以选、实际没有第二种结果"的控件。
    /// </summary>
    private async Task<object?> CreateStashAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string? message = GetString(parameters, "message");
        bool keepIndex = GetBool(parameters, "keepIndex") ?? false;
        bool includeUntracked = GetBool(parameters, "includeUntracked") ?? true;
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        // 先记下现有条数：Git 在"没有可暂存的改动"时**可能返回成功却不创建任何 Stash**
        // （实测：返回码 0、列表不变）。只认"列表是否真的多了一条"，
        // 否则界面会报告创建成功、而用户什么也没得到（规格 §10.2：不假装成功）。
        (bool beforeOk, _, IReadOnlyList<GitStashInfo> before) =
            await ReadStashListAsync(runtime, repository!, cancellationToken).ConfigureAwait(false);
        GitActionResult result = await new GitWorkspaceStateService(runtime)
            .StashWithOptionsAsync(repository!, message, includeUntracked, keepIndex, cancellationToken)
            .ConfigureAwait(false);
        InvalidateStatusCache();
        (bool afterOk, string? afterReason, IReadOnlyList<GitStashInfo> after) =
            await ReadStashListAsync(runtime, repository!, cancellationToken).ConfigureAwait(false);

        bool created = result.IsSuccess && beforeOk && afterOk && after.Count > before.Count;
        return new
        {
            available = true,
            ok = created,
            reason = created
                ? null
                : (result.ErrorMessage
                    ?? (beforeOk && afterOk ? "没有需要暂存的改动。" : afterReason)),
            // 两个分支都带上 reason 字段：匿名类型不同会让条件表达式无法推断类型。
            stashes = afterOk
                ? new { available = true, reason = (string?)null, stashes = after.Select(StashPayload).ToArray() }
                : new { available = false, reason = afterReason, stashes = Array.Empty<object>() },
        };
    }

    /// <summary>
    /// Stash 动作（规格 §7.11：应用、弹出、删除）。
    ///
    /// 引用必须由界面从真实列表里带回来：这里只接受 `stash@{n}` 形状，
    /// 任意 rev 或 `--all` 之类的选项都会被挡下（否则网页层等于拿到任意 Git 参数）。
    /// </summary>
    private async Task<object?> WriteStashAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string action = (GetString(parameters, "action") ?? string.Empty).ToLowerInvariant();
        if (action is not ("apply" or "pop" or "drop"))
        {
            throw new ArgumentException($"未知的 Stash 动作：{action}。");
        }

        string reference = GetString(parameters, "reference")
            ?? throw new ArgumentException("git/stash-write 需要 reference 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitWorkspaceStateService states = new(runtime);
        GitActionResult result = action switch
        {
            "apply" => await states.UnstashAsync(repository!, reference, keepStash: true, cancellationToken)
                .ConfigureAwait(false),
            "pop" => await states.UnstashAsync(repository!, reference, keepStash: false, cancellationToken)
                .ConfigureAwait(false),
            _ => await states.DeleteStashAsync(repository!, reference, cancellationToken).ConfigureAwait(false),
        };

        // 应用与弹出会改写工作区，删除只动 stash 列表；三种都让状态缓存失效，界面随后读真实状态。
        InvalidateStatusCache();
        return new
        {
            available = true,
            ok = result.IsSuccess,
            reason = result.ErrorMessage,
            stashes = await ProjectStashesAsync(runtime, repository!, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Stash 里的文件列表（规格 §7.11：详情里的"包含 N 个文件"）。</summary>
    private async Task<object?> ReadStashContentAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string reference = GetString(parameters, "reference")
            ?? throw new ArgumentException("git/stash-content 需要 reference 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitStashFilesResult result = await new GitWorkspaceStateService(runtime)
            .ReadStashFilesAsync(repository!, reference, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Files is not { } files)
        {
            return new { available = false, reason = result.ErrorMessage, files = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            files = files.Select(file => new { path = file.Path, status = file.Status }),
        };
    }

    /// <summary>读取 Stash 列表（强类型）；失败时给出可读原因。</summary>
    private static async Task<(bool IsSuccess, string? Reason, IReadOnlyList<GitStashInfo> List)> ReadStashListAsync(
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken)
    {
        GitStashListResult result = await new GitWorkspaceStateService(runtime)
            .ReadStashesAsync(repository, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess && result.Stashes is { } list
            ? (true, null, list)
            : (false, result.ErrorMessage, Array.Empty<GitStashInfo>());
    }

    /// <summary>Stash 列表投影（读取命令与写命令共用同一形状）。</summary>
    private static async Task<object?> ProjectStashesAsync(
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken)
    {
        (bool success, string? reason, IReadOnlyList<GitStashInfo> list) =
            await ReadStashListAsync(runtime, repository, cancellationToken).ConfigureAwait(false);
        return success
            ? new { available = true, stashes = list.Select(StashPayload).ToArray() }
            : new { available = false, reason, stashes = Array.Empty<object>() };
    }

    private static object StashPayload(GitStashInfo stash)
    {
        return new
        {
            reference = stash.Reference,
            message = stash.Message,
            branch = stash.Branch,
            subject = stash.Subject,
            date = stash.Date.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// 读取某个 Worktree 是否可安全移除（规格 §5.3：干净且对应窗口没有运行中的内置终端会话）。
    ///
    /// 界面据此**禁用**「移除…」并给出可发现的原因（§10.3），而不是先让用户点开再报错。
    /// </summary>
    private async Task<object?> ReadWorktreeRemovalAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string path = GetString(parameters, "path")
            ?? throw new ArgumentException("git/worktree-removal 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitWorktreeRemovalReadinessResult result = await new GitWorktreeService(runtime)
            .InspectRemovalReadinessAsync(repository!, path, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Readiness is not { } readiness)
        {
            return new { available = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            canRemove = readiness.CanRemove,
            isClean = readiness.IsClean,
            hasActiveTerminal = readiness.HasActiveTerminal,
            reason = readiness.Reason,
        };
    }

    /// <summary>安全移除 Worktree（规格 §5.3/§10.4）：守卫由服务强制执行，这里只回读真实列表。</summary>
    private async Task<object?> RemoveWorktreeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string path = GetString(parameters, "path")
            ?? throw new ArgumentException("git/worktree-remove 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitActionResult result = await new GitWorktreeService(runtime)
            .RemoveAsync(repository!, path, cancellationToken)
            .ConfigureAwait(false);
        InvalidateStatusCache();
        return new
        {
            available = true,
            ok = result.IsSuccess,
            reason = result.ErrorMessage,
            worktrees = await ProjectWorktreesAsync(runtime, repository!, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>读取 Worktree 列表。</summary>
    private async Task<object?> ReadWorktreesAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, worktrees = Array.Empty<object>() };
        }

        return await ProjectWorktreesAsync(runtime, repository!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ProjectWorktreesAsync(
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken)
    {
        GitWorktreeService worktrees = new(runtime);
        GitWorktreeListResult result = await worktrees.ReadAsync(repository, cancellationToken);
        if (!result.IsSuccess || result.Worktrees is not { } list)
        {
            return new { available = false, reason = result.ErrorMessage, worktrees = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            worktrees = list.Select(worktree => new
            {
                path = worktree.Path,
                branch = worktree.Branch,
                commitHash = worktree.CommitHash,
                isBare = worktree.IsBare,
                isDetached = worktree.IsDetached,
                isLocked = worktree.IsLocked,
                isPrunable = worktree.IsPrunable,
            }),
        };
    }

    /// <summary>读取单个提交的详情：正文与变更文件列表。</summary>
    /// <summary>
    /// 执行一次提交（规格 §7.6）。
    /// 只提交用户勾选的文件：先对本机 Git 补齐未跟踪文件，再以 --only 与选中路径提交磁盘内容，
    /// 不建立 Augit 自有索引事务，也不把未选中的既有暂存项带入提交。
    /// </summary>
    private async Task<object?> CreateCommitAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string message = GetString(parameters, "message") ?? string.Empty;
        bool amend = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("amend", out JsonElement amendElement)
            && amendElement.ValueKind == JsonValueKind.True;
        List<string> paths = [];
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("paths", out JsonElement pathsElement)
            && pathsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in pathsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } path)
                {
                    paths.Add(path);
                }
            }
        }

        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitCommitRequest request = new(paths, message, amend, GitCommitMessageSource.UserInput);
        GitCommitResult result = await new GitCommitService(runtime).CommitAsync(repository, request, cancellationToken);
        // 提交改变了 HEAD 与索引，状态与历史都必须重新读取，不能沿用缓存。
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, committed = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            committed = true,
            commitHash = result.CommitHash,
            // 提交后的真实状态：界面据此刷新改动列表，而不是自行推断。
            branch = result.ActualStatus?.CurrentBranch,
            remaining = result.ActualStatus?.Files.Count ?? 0,
        };
    }

    /// <summary>
    /// 推送当前分支（规格 §7.12）。网络操作不设自动超时，只响应用户主动取消。
    /// 未配置远端时如实返回原因，不伪造成功、不静默回退。
    /// </summary>
    private async Task<object?> PushAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string? remoteName = GetString(parameters, "remote");
        string? branchName = GetString(parameters, "branch");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitRemoteService remoteService = new(runtime);
        GitRemoteOperationResult result = await remoteService
            .PushAsync(repository, remoteName, branchName, cancellationToken)
            .ConfigureAwait(false);
        // 推送可能更新远端跟踪引用，重新读取状态而不是沿用缓存。
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, pushed = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            pushed = true,
            branch = result.ActualStatus?.CurrentBranch,
            remotes = result.ActualRemotes?.Count ?? 0,
        };
    }

    /// <summary>
    /// 检出分支、远端引用或标签（规格 §7.11）。
    ///
    /// 本地分支用 switch；远端引用在本地建同名分支并跟踪；标签与任意版本用 detach 检出。
    /// 工作区不干净时由 Git 自身拒绝，Augit 不代为清理，也不伪造成功。
    /// </summary>
    private async Task<object?> CheckoutAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string name = GetString(parameters, "name")
            ?? throw new ArgumentException("git/checkout 需要 name 参数。");
        string kind = GetString(parameters, "kind") ?? "branch";
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitReferenceService references = new(runtime);
        GitActionResult result = kind switch
        {
            "tag" => await references.CheckoutReferenceAsync(repository, name, cancellationToken).ConfigureAwait(false),
            // 远端引用（如 origin/feat）在本地建同名分支并跟踪：本地名取远端名之后的部分。
            "remote" => await references.CreateTrackingBranchAsync(
                repository,
                LocalBranchNameFor(name),
                name,
                cancellationToken).ConfigureAwait(false),
            _ => await references.SwitchBranchAsync(repository, name, cancellationToken).ConfigureAwait(false),
        };
        // 检出改变了 HEAD 与工作区，状态、历史与引用都必须重新读取。
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, switched = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            switched = true,
            detached = result.ActualStatus?.IsDetached ?? false,
            branch = result.ActualStatus?.CurrentBranch,
        };
    }

    /// <summary>
    /// 远端写操作（规格 §7.11 / §7.12）：新增、更新与删除。
    /// 名称与 URL 的合法性由 Git 判断，Augit 不自行放宽或收紧规则。
    /// </summary>
    private async Task<object?> WriteRemoteAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string action = GetString(parameters, "action")
            ?? throw new ArgumentException("git/remote-write 需要 action 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitRemoteService remotes = new(runtime);
        GitRemoteOperationResult result = action switch
        {
            "add" => await remotes.AddRemoteAsync(
                repository!,
                GetString(parameters, "name") ?? string.Empty,
                GetString(parameters, "fetchUrl") ?? string.Empty,
                GetString(parameters, "pushUrl"),
                cancellationToken).ConfigureAwait(false),
            "update" => await remotes.UpdateRemoteAsync(
                repository!,
                GetString(parameters, "currentName") ?? string.Empty,
                GetString(parameters, "name") ?? string.Empty,
                GetString(parameters, "fetchUrl") ?? string.Empty,
                GetString(parameters, "pushUrl"),
                cancellationToken).ConfigureAwait(false),
            "delete" => await remotes.DeleteRemoteAsync(
                repository!,
                GetString(parameters, "name") ?? string.Empty,
                cancellationToken).ConfigureAwait(false),
            _ => throw new BridgeValidationException($"未知的远端动作：{action}"),
        };
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, changed = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            changed = true,
            remotes = result.ActualRemotes?.Select(remote => new
            {
                name = remote.Name,
                fetchUrl = remote.FetchUrl,
                pushUrl = remote.PushUrl,
            }),
        };
    }

    /// <summary>
    /// 拉取远端引用（规格 §7.11「更新项目」）。
    /// 网络操作不设自动超时，只响应用户主动取消；不后台定时 fetch。
    /// </summary>
    private async Task<object?> FetchAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string? remoteName = GetString(parameters, "remote");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitRemoteOperationResult result = await new GitRemoteService(runtime)
            .FetchAsync(repository, remoteName, cancellationToken)
            .ConfigureAwait(false);
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, fetched = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            fetched = true,
            branch = result.ActualStatus?.CurrentBranch,
        };
    }

    /// <summary>
    /// 分支写操作（规格 §7.11）：新建与重命名。
    /// 只做 Git 接受的动作，名称校验、重名与非法字符都由 Git 给出原因。
    /// </summary>
    private async Task<object?> BranchAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string action = GetString(parameters, "action")
            ?? throw new ArgumentException("git/branch 需要 action 参数。");
        string name = GetString(parameters, "name")
            ?? throw new ArgumentException("git/branch 需要 name 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitReferenceService references = new(runtime);
        GitActionResult result = action switch
        {
            // 从当前 HEAD 新建；startPoint 留空即由 Git 取当前引用。
            "create" => await references.CreateBranchAsync(repository, name, null, cancellationToken).ConfigureAwait(false),
            "rename" => await references.RenameBranchAsync(
                repository,
                GetString(parameters, "from") ?? string.Empty,
                name,
                cancellationToken).ConfigureAwait(false),
            _ => throw new BridgeValidationException($"未知的分支动作：{action}"),
        };
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, changed = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            changed = true,
            branch = result.ActualStatus?.CurrentBranch,
        };
    }

    /// <summary>
    /// 由远端引用推导本地分支名：取第一个分隔符之后的部分（origin/feat → feat）。
    /// 不含分隔符时原样返回，交由 Git 校验并给出原因。
    /// </summary>
    private static string LocalBranchNameFor(string remoteReference)
    {
        int separator = remoteReference.IndexOf('/');
        return separator >= 0 && separator < remoteReference.Length - 1
            ? remoteReference[(separator + 1)..]
            : remoteReference;
    }

    private async Task<object?> ReadCommitAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string revision = GetString(parameters, "revision")
            ?? throw new ArgumentException("git/commit 需要 revision 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false };
        }

        GitHistoryService history = new(runtime);
        GitCommitDetailsResult result = await history.ReadCommitAsync(repository, revision, cancellationToken);
        if (!result.IsSuccess || result.Details is not { } details)
        {
            return new { available = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            hash = details.Commit.ShortHash,
            fullHash = details.Commit.FullHash,
            subject = details.Commit.Subject,
            author = details.Commit.AuthorName,
            date = details.Commit.AuthorDate.ToLocalTime()
                .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
            body = details.Body,
            files = details.Files.Select(file => new
            {
                path = file.RelativePath,
                name = Path.GetFileName(file.RelativePath),
                directory = (Path.GetDirectoryName(file.RelativePath) ?? string.Empty).Replace('\\', '/'),
                kind = file.Kind.ToString(),
                original = file.OriginalRelativePath,
            }),
        };
    }

    /// <summary>读取指定文件的逐行归属（Blame）。</summary>
    private async Task<object?> ReadBlameAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/blame 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, lines = Array.Empty<object>() };
        }

        GitHistoryService history = new(runtime);
        GitBlameResult result = await history.ReadBlameAsync(repository, relative, null, cancellationToken);
        if (!result.IsSuccess || result.Lines is not { } lines)
        {
            return new { available = false, reason = result.ErrorMessage, lines = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            path = relative,
            lines = lines.Select(line => new
            {
                number = line.LineNumber,
                hash = line.CommitHash[..Math.Min(7, line.CommitHash.Length)],
                fullHash = line.CommitHash,
                author = line.AuthorName,
                date = line.AuthorDate.ToLocalTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture),
                summary = line.Summary,
                content = line.Content,
            }),
        };
    }

    /// <summary>读取限定到某个路径的提交历史（文件历史）。</summary>
    private async Task<object?> ReadFileHistoryAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string relative = GetString(parameters, "path")
            ?? throw new ArgumentException("git/file-history 需要 path 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, commits = Array.Empty<object>() };
        }

        GitHistoryService history = new(runtime);
        GitHistoryResult result = await history.ReadPageAsync(
            repository,
            new GitHistoryRequest(Page: 0, PageSize: 100, Filter: new GitHistoryFilter(FilePath: relative)),
            cancellationToken);
        if (!result.IsSuccess || result.Page is not { } page)
        {
            return new { available = false, reason = result.ErrorMessage, commits = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            path = relative,
            commits = page.Entries.Select(entry => new
            {
                hash = entry.ShortHash,
                fullHash = entry.FullHash,
                subject = entry.Subject,
                author = entry.AuthorName,
                date = entry.AuthorDate.ToLocalTime()
                    .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
            }),
        };
    }

    /// <summary>
    /// 解析 Git 运行时并检查仓库，结果在本进程内复用。
    /// 两步各自都要启动 git 进程，重复执行会明显拖慢首次加载。
    /// </summary>
    private Task<(GitRuntimeInfo Runtime, GitRepositorySnapshot? Repository)>? _gitResolution;

    /// <summary>
    /// 解析 Git 运行时并检查仓库，结果在本进程内复用。
    /// 缓存的是 Task 而不是结果：并发的 Git 请求会共享同一次解析，
    /// 不会因为后到者等待前者的锁而阻塞（这曾导致 blame 永久挂起）。
    /// </summary>
    private Task<(GitRuntimeInfo Runtime, GitRepositorySnapshot? Repository)> ResolveGitAsync(
        CancellationToken cancellationToken)
    {
        return _gitResolution ??= ResolveGitCoreAsync(cancellationToken);
    }

    private async Task<(GitRuntimeInfo Runtime, GitRepositorySnapshot? Repository)> ResolveGitCoreAsync(
        CancellationToken cancellationToken)
    {
        // 读取设置里配置的 git.exe：此前这里传 null，导致「设置 git.exe」这一项
        // 在界面上可填可存但完全不起作用。配置为空时按 PATH 查找。
        string? configured = null;
        try
        {
            SettingsStore store = new();
            ApplicationSettings settings = await store.LoadAsync(cancellationToken);
            configured = settings.GitExecutablePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 设置读取失败不应阻止 Git 发现：退回按 PATH 查找。
        }

        GitExecutableLocator locator = new();
        GitRuntimeInfo runtime = await locator.ResolveAsync(configured, cancellationToken);
        if (!runtime.IsAvailable)
        {
            return (runtime, null);
        }

        GitRepositoryService repositories = new(runtime);
        GitRepositoryOperationResult inspection =
            await repositories.InspectAsync(_workspaceRoot, cancellationToken);
        return (runtime, inspection.IsSuccess ? inspection.Repository : null);
    }

    private static int? GetInt(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            ? element.GetInt32()
            : null;
    }

    private static long? GetLong(JsonElement parameters, string name)
    {
        return parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            ? element.GetInt64()
            : null;
    }

    private static bool IsUsable(GitRepositorySnapshot? repository, GitRuntimeInfo runtime)
    {
        return runtime.IsAvailable && repository is not null
            && repository.Kind == GitRepositoryKind.WorkingTree;
    }

    private string ResolveInsideWorkspace(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return _workspaceRoot;
        }

        string combined = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string root = _workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeValidationException($"路径越出工作区：{relativePath}");
        }

        return combined;
    }

    private string ToRelative(string fullPath)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, fullPath);
        return relative.Replace('\\', '/');
    }

    private static string? GetString(JsonElement parameters, string name)
    {
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private static string Serialize(long id, object? result, string? error)
    {
        return JsonSerializer.Serialize(
            error is null ? new Response(id, result, null) : new Response(id, null, error),
            PayloadOptions);
    }

    private sealed record Response(long Id, object? Result, string? Error);
}
