using System.Globalization;
using System.Text;
using Augit.Core.Search;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Search;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Augit.Core.Documents;
using Augit.Core.Files;
using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Shell;

/// <summary>
/// 网页层与 C# 能力层之间的消息桥。网页发送 <c>{ id, method, params }</c>，
/// 这里返回 <c>{ id, result }</c> 或 <c>{ id, error }</c>。
/// </summary>
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
        catch (Exception exception)
        {
            return Serialize(id, null, exception.Message);
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
            "git/diff" => await ReadDiffAsync(parameters, cancellationToken),
            "git/remotes" => await ReadRemotesAsync(cancellationToken),
            "git/references" => await ReadReferencesAsync(cancellationToken),
            "git/stashes" => await ReadStashesAsync(cancellationToken),
            "git/worktrees" => await ReadWorktreesAsync(cancellationToken),
            "git/conflicts" => await ReadConflictsAsync(cancellationToken),
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
            "settings/read" => await ReadSettingsAsync(cancellationToken),
            "settings/write" => await WriteSettingsAsync(parameters, cancellationToken),
            _ => throw new InvalidOperationException($"未知的宿主方法：{method}"),
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
        IReadOnlyList<WorkspaceEntry> entries = WorkspaceDirectoryService.EnumerateChildren(fullPath);
        return new
        {
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
                staged = file.HasStagedChanges,
                workingTree = file.HasWorkingTreeChanges,
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
            projectPanelWidth = sameWorkspace ? settings.ToolWindows.ProjectPanelWidth : null,
            bottomPanelHeight = sameWorkspace ? settings.ToolWindows.BottomPanelHeight : null,
        };
    }

    /// <summary>
    /// 写入设置。只接受已知字段：未知字段被忽略，字号等数值先做范围校验，
    /// 避免把非法值写进设置文件。
    /// </summary>
    private static async Task<object?> WriteSettingsAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        SettingsStore store = new();
        ApplicationSettings current = await store.LoadAsync(cancellationToken);
        ApplicationSettings updated = current with
        {
            Theme = GetString(parameters, "theme") ?? current.Theme,
            TextFontFamily = GetString(parameters, "textFontFamily") ?? current.TextFontFamily,
            MonospaceFontFamily = GetString(parameters, "monospaceFontFamily") ?? current.MonospaceFontFamily,
            FontSize = ClampFontSize(GetDouble(parameters, "codeFontSize")) ?? current.FontSize,
            TextFontSize = ClampFontSize(GetDouble(parameters, "fontSize")) ?? current.UiFontSize,
            GitExecutablePath = GetString(parameters, "gitExecutablePath") ?? current.GitExecutablePath,
            TerminalShell = GetString(parameters, "terminalShell") ?? current.TerminalShell,
            TerminalCustomCommand = GetString(parameters, "terminalCustomCommand") ?? current.TerminalCustomCommand,
        };
        await store.SaveAsync(updated, cancellationToken);
        return new { saved = true, theme = updated.Theme, fontSize = updated.UiFontSize };
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
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, rows = Array.Empty<object>(), lines = Array.Empty<object>() };
        }

        GitStatusResult status = await ReadStatusCachedAsync(runtime, repository!, cancellationToken);
        GitChangedFile? changed = status.Snapshot?.Files
            .FirstOrDefault(file => string.Equals(file.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
        if (changed is null)
        {
            return new { available = false, reason = "该文件当前没有改动。", rows = Array.Empty<object>(), lines = Array.Empty<object>() };
        }

        GitDiffService diffService = new(runtime);
        GitDiffResult result = await diffService.CreateAsync(
            repository!,
            changed,
            new GitDiffOptions(ignoreWhitespace),
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

        GitWorkspaceStateService states = new(runtime);
        GitStashListResult result = await states.ReadStashesAsync(repository!, cancellationToken);
        if (!result.IsSuccess || result.Stashes is not { } list)
        {
            return new { available = false, reason = result.ErrorMessage, stashes = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            stashes = list.Select(stash => new
            {
                reference = stash.Reference,
                message = stash.Message,
                branch = stash.Branch,
                subject = stash.Subject,
                date = stash.Date.ToLocalTime()
                    .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
            }),
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

        GitWorktreeService worktrees = new(runtime);
        GitWorktreeListResult result = await worktrees.ReadAsync(repository!, cancellationToken);
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
        GitExecutableLocator locator = new();
        GitRuntimeInfo runtime = await locator.ResolveAsync(null, cancellationToken);
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
            throw new InvalidOperationException($"路径越出工作区：{relativePath}");
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
