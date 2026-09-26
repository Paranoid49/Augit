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

    /// <summary>
    /// 单次 `terminal/read` 最多返回的字符数。
    ///
    /// 真机实测（`docs/ui-compliance.md` §3.2 第 15 条）：一次把最多 4 MB 的积压全部搬过桥，
    /// 在约 4.8 MB 输出之后终端会**永久停止更新**；分批返回后客户端连续轮询即可追平，不丢内容。
    /// </summary>
    private const int MaximumTerminalChunkLength = 128 * 1024;

    /// <summary>
    /// 裁剪的摊销余量：超过"上限 + 余量"才裁剪，并一次裁回上限。
    ///
    /// 每次追加都裁（`Remove(0, 超出量)`）会让**每 4 KB 追加都 memmove 整个 4 MB 缓冲区**，
    /// 真机 ~4.8 MB 洪泛时读取线程长期卡在 <see cref="OnTerminalOutput"/> 里 →
    /// pty 管道写满 → Shell 阻塞在写 → 终端永久冻结（而 CPU 只花在宿主上、Shell 侧 CPU 不增长，
    /// 这正是探针第 367 轮的读数）。留出余量后裁剪次数下降约两个数量级。
    /// </summary>
    private const int TerminalTrimSlackLength = 512 * 1024;

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
    private readonly WriteOperationTracker _writes = new();
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

    /// <summary>
    /// 窗口命令（<c>window/*</c>）的处理入口，由外壳窗口在构造后注入。
    /// 桥接层不认识 HWND，窗口命令不属于工作区或 Git 能力，因此用注入而不是在这里判断平台。
    /// </summary>
    internal Func<string, JsonElement, object?>? WindowCommandHandler { get; set; }

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
            "git/detect" => await ReadGitDetectionAsync(cancellationToken),
            // 创建仓库是写操作（权威 `GitInit` 也走后台任务），因此并入写操作通道。
            "git/init" => await RunWriteAsync(ct => InitRepositoryAsync(parameters, ct), cancellationToken),
            "git/history" => await ReadHistoryAsync(parameters, cancellationToken),
            "git/last-commit-message" => await ReadLastCommitMessageBridgeAsync(cancellationToken),
            "git/blame" => await ReadBlameAsync(parameters, cancellationToken),
            "git/file-history" => await ReadFileHistoryAsync(parameters, cancellationToken),
            "git/commit" => await RunWriteAsync(ct => ReadCommitAsync(parameters, ct), cancellationToken),
            "git/commit-create" => await CreateCommitAsync(parameters, cancellationToken),
            "git/push" => await RunWriteAsync(ct => PushAsync(parameters, ct), cancellationToken),
            "git/checkout" => await RunWriteAsync(ct => CheckoutAsync(parameters, ct), cancellationToken),
            // Smart Checkout 同样是写操作（临时 stash → 检出 → 恢复），并入同一通道。
            "git/checkout-smart" => await RunWriteAsync(ct => SmartCheckoutAsync(parameters, ct), cancellationToken),
            "git/branch" => await RunWriteAsync(ct => BranchAsync(parameters, ct), cancellationToken),
            "git/tag" => await RunWriteAsync(ct => TagAsync(parameters, ct), cancellationToken),
            "git/fetch" => await FetchAsync(parameters, cancellationToken),
            "git/unpushed" => await ReadUnpushedAsync(cancellationToken),
            "git/remote-write" => await RunWriteAsync(ct => WriteRemoteAsync(parameters, ct), cancellationToken),
            "git/diff" => await ReadDiffAsync(parameters, cancellationToken),
            "git/rollback" => await RunWriteAsync(ct => RollbackAsync(parameters, ct), cancellationToken),
            "git/reset" => await RunWriteAsync(ct => ResetAsync(parameters, ct), cancellationToken),
            "git/remotes" => await ReadRemotesAsync(cancellationToken),
            "git/references" => await ReadReferencesAsync(cancellationToken),
            "git/branches-mine" => await ReadMyBranchesAsync(cancellationToken),
            "git/authors" => await ReadAuthorsAsync(cancellationToken),
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
            "git/conflict-save" => await RunWriteAsync(ct => SaveConflictAsync(parameters, ct), cancellationToken),
            "terminal/start" => await StartTerminalAsync(parameters, cancellationToken),
            "terminal/read" => ReadTerminal(parameters),
            "terminal/status" => ReadTerminalStatus(),
            "terminal/write" => await WriteTerminalAsync(parameters, cancellationToken),
            "terminal/resize" => ResizeTerminal(parameters),
            "terminal/stop" => await StopTerminalAsync(),
            "git/clone" => await RunWriteAsync(ct => CloneAsync(parameters, ct), cancellationToken),
            "workspace/open" => OpenWorkspace(parameters),
            "write/cancel" => CancelWrite(),
            "workspace/pick" => PickWorkspace(),
            "workspace/changes" => ReadWorkspaceChanges(),
            "search/files" => await SearchFilesAsync(parameters, cancellationToken),
            "search/text" => await SearchTextAsync(parameters, cancellationToken),
            "external/launch" => await LaunchExternalAsync(parameters, cancellationToken),
            "clipboard/write" => WriteClipboard(parameters),
            "settings/read" => await ReadSettingsAsync(cancellationToken),
            "settings/write" => await WriteSettingsAsync(parameters, cancellationToken),
            "session/write" => await WriteSessionAsync(parameters, cancellationToken),
            _ when method.StartsWith("window/", StringComparison.Ordinal) => DispatchWindow(method, parameters),
            _ => throw new BridgeValidationException($"未知的宿主方法：{method}"),
        };
    }

    /// <summary>
    /// 窗口命令转交外壳窗口。
    /// 桥接层只有工作区与 Git 能力，不认识 HWND；无 caption 之后，视觉稿自绘的
    /// 最小化/最大化/关闭按钮必须真的能关闭窗口，因此这些方法必须一并可用。
    /// </summary>
    private object? DispatchWindow(string method, JsonElement parameters)
        => WindowCommandHandler is { } handler
            ? handler(method, parameters)
            : throw new BridgeValidationException($"当前没有外壳窗口，无法执行：{method}");

    /// <summary>
    /// 弹出系统文件夹选择框（视觉稿「打开工作区」页的「选择目录…」）。
    /// 取消时如实返回 <c>picked:false</c>，界面不因此关闭窗口。
    /// </summary>
    private static object PickWorkspace()
    {
        WorkspacePickResult result = WorkspacePicker.Pick(FolderPicker.Pick);
        return new
        {
            available = true,
            picked = result.Outcome == WorkspacePickOutcome.Picked,
            path = result.Path,
            reason = result.Reason,
        };
    }

    /// <summary>
    /// 打开工作区（产品规格 §2）：同目录激活已有窗口，不同目录启动新窗口。
    /// 目录选择由外壳负责（网页层只提交路径），路径校验在 <see cref="WorkspaceOpener"/> 里。
    /// </summary>
    /// <summary>
    /// 执行一条写命令：交给跟踪器串接取消令牌，被取消时如实回话（不假装成功）。
    /// 取消是否生效由命令本身决定——跟踪器只负责触发令牌与登记"当前写操作"。
    /// </summary>
    private async Task<object?> RunWriteAsync(
        Func<CancellationToken, Task<object?>> action,
        CancellationToken cancellationToken)
    {
        bool cancelled = false;
        object? result = await _writes
            .RunAsync(action, flag => cancelled = flag, cancellationToken)
            .ConfigureAwait(false);
        return cancelled
            ? new { available = true, cancelled = true, reason = "操作已取消。" }
            : result;
    }

    /// <summary>取消进行中的写操作（规格 §9.3）。没有在途操作时明确回话，界面据此说明。</summary>
    private object CancelWrite()
    {
        bool cancelled = _writes.Cancel();
        return new
        {
            available = true,
            cancelled,
            reason = cancelled ? null : "当前没有可取消的操作。",
        };
    }

    private object OpenWorkspace(JsonElement parameters)
    {
        string path = GetString(parameters, "path")
            ?? throw new ArgumentException("workspace/open 需要 path 参数。");
        WorkspaceOpenResult result = WorkspaceOpener.Open(
            _workspaceRoot,
            path,
            WorkspaceWindowRegistry.TryActivate,
            LaunchWorkspaceWindow);
        return new
        {
            available = true,
            opened = result.Outcome.ToString().ToLowerInvariant(),
            reason = result.Reason,
        };
    }

    /// <summary>为该目录启动一个新的 Augit 窗口（当前程序 + 该目录）。</summary>
    private static bool LaunchWorkspaceWindow(string workspacePath)
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable)
        {
            return false;
        }

        return ExternalProgramLauncher.OpenAugitWorkspace(executable, workspacePath).IsSuccess;
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
        // 图片正文的位图数据：渲染层把 live.document.dataUrl 直接当作 <img src>。
        // 只有真正解码成功的图片才返回；过大与解码失败保持不返回，界面继续走信息态
        // （体积与解码边界由既有状态机覆盖，不在这里另设阈值）。
        string? dataUrl = null;
        if (result.Status == DocumentReadStatus.ImageReady)
        {
            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            dataUrl = $"data:{ImageMime(result.Classification.Kind)};base64,{Convert.ToBase64String(bytes)}";
        }

        // JSON 的"格式化"正文与错误行列（规格 §7.4）。
        // `JsonDisplayFormatter` 此前**只有单元测试在调用**，桥接层从未把结果发给界面：
        // 真机上点"格式化"看到的其实是原文，格式错误时也没有错误条、没有禁用"格式化"。
        // 这里把它接上：格式有效给出重新序列化的正文（两空格缩进、保持属性顺序），
        // 格式无效给出**从 1 开始的行号与按 Unicode 标量计的列号**。
        string? formatted = null;
        long? jsonErrorLine = null;
        long? jsonErrorColumn = null;
        if (result.Status == DocumentReadStatus.TextReady
            && result.Classification.Kind == DocumentKind.Json
            && result.Text is { } jsonText)
        {
            JsonDisplayResult display = JsonDisplayFormatter.Format(jsonText);
            if (display.IsValid)
            {
                formatted = display.DisplayText;
            }
            else
            {
                jsonErrorLine = display.ErrorLine;
                jsonErrorColumn = display.ErrorColumn;
            }
        }

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
            // 只读预览读了前多少字节（权威警告里的 `{1}` = `getPreviewLimit(extension)`）。
            previewBytes = result.PreviewBytes,
            // 只有文本视图才有编码与磁盘换行事实；图片与非法 UTF-8 等状态不显示编码，
            // 避免让用户以为文件是文本。只读预览**仍是文本视图**（权威的大文件编辑器照样显示编码），
            // 因此它与正常文本同样带上这两项。
            encoding = result.Status is DocumentReadStatus.TextReady or DocumentReadStatus.TextPreview
                ? TextEncodingName
                : null,
            lineEndings = result.Status is DocumentReadStatus.TextReady or DocumentReadStatus.TextPreview
                ? DescribeLineEndings(result.LineEndings)
                : null,
            dataUrl,
            formatted,
            jsonError = jsonErrorLine is { } errorLine
                ? (object?)new { line = errorLine, column = jsonErrorColumn }
                : null,
        };
    }

    /// <summary>图片 MIME；与 DocumentKind 的图片取值一一对应，供 data: URL 使用。</summary>
    private static string ImageMime(DocumentKind kind) => kind switch
    {
        DocumentKind.Png => "image/png",
        DocumentKind.Jpeg => "image/jpeg",
        DocumentKind.Bmp => "image/bmp",
        DocumentKind.Gif => "image/gif",
        DocumentKind.WebP => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Git 探测结果（规格 §7.17：Git 分类要提供 git.exe 路径、**检测结果**和**最低版本说明**）。
    /// 只读一次缓存的运行时信息，不启动任何命令。
    /// </summary>
    private async Task<object?> ReadGitDetectionAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, _) = await ResolveGitAsync(cancellationToken);
        return new
        {
            available = runtime.IsAvailable,
            path = runtime.ExecutablePath,
            version = runtime.Version is { } version
                ? string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Patch}")
                : null,
            minimumVersion = string.Create(
                CultureInfo.InvariantCulture,
                $"{GitVersion.MinimumSupported.Major}.{GitVersion.MinimumSupported.Minor}"),
            reason = runtime.UnavailableReason,
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

    /// <summary>
    /// 读取上一次提交信息（规格 §7.6：Amend 勾选后回填提交信息）。
    /// 语义（是否适用于工作区、无 HEAD 时的稳定失败）由 <see cref="IGitCommitService"/> 负责，
    /// 这里只做整形与结果映射 —— 与其它 git 桥接方法同一口径。
    /// </summary>
    private async Task<object?> ReadLastCommitMessageBridgeAsync(CancellationToken cancellationToken)
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

        GitCommitService commit = new(runtime);
        GitCommitMessageResult result = await commit.ReadLastCommitMessageAsync(repository, cancellationToken);
        return new
        {
            available = true,
            isRepository = true,
            message = result.Message,
            reason = result.ErrorMessage,
        };
    }

    /// <summary>
    /// 创建 Git 仓库（权威 `GitInit`，`plugins/git4idea/backend/src/actions/GitInit.java`）。
    ///
    /// 确认时机照权威 `:66-74`：**只有目标已在 Git 下**才需要问一次
    /// （`GitUtil.isUnderGit(root)` ＋ Yes/No ＋ 警告图标，文案 `init.warning.already.under.git` 带目标目录）；
    /// 不是仓库时**没有任何确认**，直接初始化。因此：
    /// - 目标不是仓库 ⇒ 直接 `git init`；
    /// - 目标是仓库且未带 `confirm` ⇒ 回 `requiresConfirmation`，界面问过之后带 `confirm:true` 再调用；
    ///   此时不再重复执行（`git init` 对已有仓库本就是空操作，结论等同"已初始化"）。
    ///
    /// 成败都走既有写操作通道：失败回可读原因（权威给的是带 Git 错误输出的错误通知），
    /// 成功后让 Git 解析与状态缓存失效——缓存里还存着"这个目录不是仓库"。
    /// </summary>
    private async Task<object?> InitRepositoryAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string path = (GetString(parameters, "path") ?? _workspaceRoot).Trim();
        if (path.Length == 0)
        {
            throw new ArgumentException("git/init 的 path 不能为空。");
        }

        bool confirmed = GetBool(parameters, "confirm") ?? false;
        (GitRuntimeInfo runtime, _) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable)
        {
            return new { available = false, reason = runtime.UnavailableReason };
        }

        if (!Directory.Exists(path))
        {
            return new { available = false, reason = $"目标目录不存在：{path}" };
        }

        GitRepositoryService repositories = new(runtime);
        GitRepositoryOperationResult inspection = await repositories.InspectAsync(path, cancellationToken);
        if (!inspection.IsSuccess || inspection.Repository is null)
        {
            return new { available = false, reason = inspection.ErrorMessage ?? "无法检查目标目录。" };
        }

        if (inspection.Repository.IsRepository)
        {
            return confirmed
                ? new { available = true, initialized = true, alreadyUnderGit = true, path }
                : new { available = true, initialized = false, requiresConfirmation = true, path };
        }

        GitRepositoryOperationResult result = await repositories.InitializeAsync(path, cancellationToken);
        if (!result.IsSuccess)
        {
            return new { available = false, reason = result.ErrorMessage };
        }

        // 新仓库出现后，缓存里的解析结果（"不是仓库"）与状态都必须作废。
        _gitResolution = null;
        InvalidateStatusCache();
        return new { available = true, initialized = true, alreadyUnderGit = false, path };
    }

    /// <summary>
    /// 读取提交历史（规格 §7.8）。
    ///
    /// 可选筛选与权威的日志筛选一一对应：`message`（提交信息子串）、`hash`（哈希前缀，
    /// 命中即短路其余筛选）、`author`、`since`/`until`（ISO-8601 时间）、`branch`（引用名，
    /// `HEAD` 亦可）、`path`（仓库内相对路径）、`rangeExclusive`/`rangeInclusive`（范围筛选，
    /// 即 `git log <exclusive>..<inclusive>`，两端必须同时给出）。界面只送它真正设置了的项，未设置即不过滤。
    /// </summary>
    private async Task<object?> ReadHistoryAsync(JsonElement parameters, CancellationToken cancellationToken)
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
            new GitHistoryRequest(Page: 0, PageSize: 100, Filter: new GitHistoryFilter(
                Message: GetString(parameters, "message"),
                Hash: GetString(parameters, "hash"),
                Author: GetString(parameters, "author"),
                Since: GetDate(parameters, "since"),
                Until: GetDate(parameters, "until"),
                Branch: GetString(parameters, "branch"),
                FilePath: GetString(parameters, "path"),
                RangeExclusive: GetString(parameters, "rangeExclusive"),
                RangeInclusive: GetString(parameters, "rangeInclusive"),
                Branches: GetStringList(parameters, "branches") is { Count: > 0 } multipleBranches
                    ? multipleBranches
                    : null,
                Authors: GetStringList(parameters, "authors") is { Count: > 0 } multipleAuthors
                    ? multipleAuthors
                    : null,
                // 「按路径筛选」可以同时给出多个路径（权威 `VcsLogStructureFilter` 就是一组文件）。
                Paths: GetStringList(parameters, "paths") is { Count: > 0 } multiplePaths
                    ? multiplePaths
                    : null)),
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
                // 「与当前分支比较」复用**文件历史列表**的行渲染（同一套 `history-columns`／`history-rows`），
                // 因此这里与 `git/file-history` 一样带上作者列需要的两组身份（权威
                // `FileHistoryPanelImpl.AuthorColumnInfo` 的 `*` 与 `, via …`）。
                authorEmail = entry.AuthorEmail,
                committerName = entry.CommitterName,
                committerEmail = entry.CommitterEmail,
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

    /// <summary>
    /// Reset 当前分支到目标提交（规格 §7.11：三种模式必须在同一页面解释 HEAD、索引与工作区的影响，
    /// Hard 使用危险确认；界面已确认，这里只做参数校验与执行）。
    /// </summary>
    private async Task<object?> ResetAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string target = (GetString(parameters, "target") ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            return new { available = true, reset = false, reason = "请输入 Reset 目标提交。" };
        }

        string modeText = GetString(parameters, "mode") ?? "Hard";
        if (!Enum.TryParse(modeText, ignoreCase: true, out GitResetMode mode))
        {
            return new { available = true, reset = false, reason = "Reset 模式无效，请选择 Soft、Mixed 或 Hard。" };
        }

        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitActionResult result = await new GitWorkspaceStateService(runtime)
            .ResetAsync(repository, target, mode, cancellationToken)
            .ConfigureAwait(false);
        // Reset 会移动 HEAD 或改动工作区：状态缓存必须失效，界面随后重新读取真实仓库状态。
        InvalidateStatusCache();
        if (!result.IsSuccess)
        {
            return new { available = true, reset = false, reason = result.ErrorMessage };
        }

        return new
        {
            available = true,
            reset = true,
            target,
            mode = mode.ToString(),
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
            GetBool(parameters, "includeIgnoredFiles") ?? false)
        {
            // 「继续搜索」用分页表达（权威 `UsageViewManagerImpl.showTooManyUsagesWarningLater` 的 Continue
            // 是让同一次搜索继续跑完）：一页仍然最多 `MaximumTextResults` 条 ——
            // 单次桥接响应必须保持在 WebView2 可靠传输的规模内，未在这里放开的更大页没有任何好处。
            SkipResults = Math.Max(0, GetInt(parameters, "offset") ?? 0),
            MaximumResults = Math.Clamp(
                GetInt(parameters, "limit") ?? SearchOptions.MaximumTextResults,
                1,
                SearchOptions.MaximumTextResults),
        };
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
            // 分支面板「显示标签」（权威 `git.branches.show.tags`，默认 true）。
            showGitBranchesTags = settings.ShowGitBranchesTags,
            // 分支面板「按目录分组」（权威 `git.branches.group.by.directory`，默认 true）。
            groupBranchesByDirectory = settings.GroupBranchesByDirectory,
            // 大文件只读预览的警告横幅「不再显示」（权威 `PropertiesComponent` 的应用级开关）。
            hideLargeFileWarning = settings.HideLargeFileWarning,
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
            // 分支面板「显示标签」：来自分支面板设置弹层的复选行，不给值时保留原值。
            ShowGitBranchesTags = GetBool(parameters, "showGitBranchesTags") ?? current.ShowGitBranchesTags,
            // 分支面板「按目录分组」：同一个设置弹层的复选行，不给值时保留原值。
            GroupBranchesByDirectory = GetBool(parameters, "groupBranchesByDirectory") ?? current.GroupBranchesByDirectory,
            // 大文件只读预览的「不再显示」：来自预览横幅的动作，不给值时保留原值。
            HideLargeFileWarning = GetBool(parameters, "hideLargeFileWarning") ?? current.HideLargeFileWarning,
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
            // 只保留最近一段的输出，避免长时间运行后内存无界增长。
            // 用"上限 + 余量"再裁（见 TerminalTrimSlackLength）：逐次裁剪是二次成本。
            if (ShouldTrimTerminalBuffer(_terminalBuffer.Length, MaximumTerminalBufferLength, TerminalTrimSlackLength))
            {
                TrimTerminalBuffer(_terminalBuffer, ref _terminalOffset, MaximumTerminalBufferLength);
            }
        }
    }

    /// <summary>
    /// 终端输出缓冲区的裁剪：超过上限时丢掉最旧的一段。
    /// 读取偏移量必须**同步减去被裁掉的字符数**，否则偏移会永久大于缓冲区长度，
    /// `ReadTerminal` 的 <c>Math.Clamp</c> 会把起点夹到末尾，终端从此再也读不到输出
    /// （此前写成 <c>_terminalOffset - (buffer.Length - Maximum)</c>，而 Remove 之后该项已变成 0）。
    /// </summary>
    /// <summary>是否该裁剪：超过"上限 + 余量"才裁，用于把裁剪成本摊销掉。</summary>
    internal static bool ShouldTrimTerminalBuffer(int length, int maximumLength, int slack)
        => length > maximumLength + slack;

    internal static void TrimTerminalBuffer(StringBuilder buffer, ref long offset, int maximumLength)
    {
        if (buffer.Length <= maximumLength)
        {
            return;
        }

        int removed = buffer.Length - maximumLength;
        buffer.Remove(0, removed);
        offset = Math.Max(0, offset - removed);
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
        // 客户端可以要求更小的分段；缺省用 128 KB。上限夹在 1 KB 到整个缓冲区之间。
        int maximum = (int)Math.Clamp(
            GetLong(parameters, "maximumLength") ?? MaximumTerminalChunkLength,
            1024,
            MaximumTerminalBufferLength);
        lock (_terminalGate)
        {
            long start = Math.Clamp(offset, 0, _terminalBuffer.Length);
            int backlog = (int)(_terminalBuffer.Length - start);
            int length = Math.Min(backlog, maximum);
            string chunk = _terminalBuffer.ToString((int)start, length);
            return new
            {
                available = _terminal is not null,
                running = _terminal?.IsRunning ?? false,
                exited = _terminalExited,
                exitCode = _terminalExitCode,
                // 报告**本段末尾**而不是缓冲区末尾：偏移必须与真正返回的字符数一致，
                // 否则分批返回时客户端会跳过还没拿到的那一段。
                offset = start + length,
                // 还剩多少没读：客户端可以据此连续轮询追平（也便于真机探针判断积压）。
                pending = backlog - length,
                // 后台读取循环里"投递输出"失败的原因（诊断用；null 表示没有失败）。
                notifyError = _terminal?.LastNotifyError,
                // 输出管道是否已读到 EOF（读取循环结束）：真机探针据此区分
                // "循环已退出"与"Shell 阻塞写满管道"这两种机制。
                outputEnded = _terminal?.OutputEnded ?? false,
                data = chunk,
            };
        }
    }

    /// <summary>
    /// 终端会话状态（规格 §7.16：关闭运行中终端前必须先确认）。
    /// 只有用户点了关闭才会查一次前台进程，因此不进轮询路径。
    /// </summary>
    private object ReadTerminalStatus()
    {
        lock (_terminalGate)
        {
            bool available = _terminal is not null;
            return new
            {
                available,
                running = _terminal?.IsRunning ?? false,
                foreground = available && _terminal!.HasForegroundProcess,
                exited = _terminalExited,
                exitCode = _terminalExitCode,
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
        return new
        {
            available = true,
            ok = result.IsSuccess,
            reason = result.ErrorMessage,
            session = SessionPayload(result.Session),
        };
    }

    /// <summary>
    /// 操作会话本身的投影（`git/operation`、`git/operation-action` 与 Smart Checkout 共用）：
    /// 界面据此显示会话类型、可用动作与冲突文件。
    /// </summary>
    private static object? SessionPayload(GitOperationSession? session)
    {
        return session is null ? null : new
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

    /// <summary>
    /// 「我的分支」的判据数据（权威 `ShowMyBranchesAction` → `BranchesDashboardUtil`：
    /// 分支的独占提交非空且全部由当前 Git 用户提交）。
    ///
    /// 只在用户打开该开关时调用（权威的 `showOnlyMy` 是 `observable(false)` 的会话状态，
    /// 不持久化；这里同样只回结果、不写任何设置）。
    /// </summary>
    private async Task<object?> ReadMyBranchesAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, author = (string?)null, mine = Array.Empty<string>() };
        }

        GitMyBranchesResult result = await new GitReferenceService(runtime)
            .ReadMyBranchesAsync(repository!, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new
            {
                available = false,
                reason = result.ErrorMessage,
                author = (string?)null,
                mine = Array.Empty<string>(),
            };
        }

        return new
        {
            available = true,
            author = result.Author,
            mine = result.Mine,
        };
    }

    /// <summary>
    /// 提交历史里的作者集合（权威 `VcsLogUserResolver`／`GitUserRegistry`）：供「按用户筛选」的弹层列表。
    /// 只在用户打开该弹层时调用；只回历史里出现过的人。
    /// </summary>
    private async Task<object?> ReadAuthorsAsync(CancellationToken cancellationToken)
    {
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!IsUsable(repository, runtime))
        {
            return new { available = false, authors = Array.Empty<object>() };
        }

        GitAuthorsResult result = await new GitHistoryService(runtime)
            .ReadAuthorsAsync(repository!, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return new { available = false, reason = result.ErrorMessage, authors = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            authors = result.Authors.Select(author => new { name = author.Name, email = author.Email }),
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
    /// 「包含未跟踪文件」照权威 `GitStashDialog.kt` 的 `Include &untracked` 复选处理：
    /// **默认不勾选**（`JBCheckBox` 未选中、且不持久化上次选择），界面把勾选状态显式送进来；
    /// 缺省（没有该参数）同样按**不包含**处理，与对话框的默认值保持一致。
    /// </summary>
    private async Task<object?> CreateStashAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string? message = GetString(parameters, "message");
        bool keepIndex = GetBool(parameters, "keepIndex") ?? false;
        bool includeUntracked = GetBool(parameters, "includeUntracked") ?? false;
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
            IReadOnlyList<string> overwritePaths = ParseCheckoutOverwritePaths(result.ErrorMessage);
            return new
            {
                available = true,
                switched = false,
                reason = result.ErrorMessage,
                // 权威的 Smart Checkout 对话框就建立在 git 的这条错误上
                // （`GitCheckoutOperation.smartCheckoutOrNotify`，`GitCheckoutOperation.java:367-395`），
                // 受影响文件由 `GitLocalChangesWouldBeOverwrittenDetector` 解析同一份文本得到
                // （`GitMessageWithFilesDetector.java:45-60`）。界面据此决定是否给出 Smart Checkout。
                overwriteRisk = overwritePaths.Count > 0,
                overwritePaths,
            };
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
    /// 解析 git 的"本地改动／未跟踪文件会被检出覆盖"错误，取出受影响文件。
    ///
    /// 与权威 `GitLocalChangesWouldBeOverwrittenDetector` 同一口径（`:37-44,88-105`）：
    /// 起始行是 "Your local changes to the following files would be overwritten by …" 或
    /// "The following untracked working tree files would be overwritten by …"；
    /// 文件是随后的**缩进行**（以制表符或两个空格开头），列表在第一个非缩进行（"Please …"／"Aborting"）结束；
    /// 老格式把多个文件挤在一行并以两个空格开头 ⇒ 按空白拆开（权威 `getRelativeFilePaths()` 的同名处理）。
    /// </summary>
    internal static IReadOnlyList<string> ParseCheckoutOverwritePaths(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return [];
        }

        List<string> paths = [];
        bool collecting = false;
        foreach (string line in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!collecting)
            {
                collecting = line.Contains("would be overwritten by", StringComparison.Ordinal)
                    && (line.Contains("Your local changes to the following files", StringComparison.Ordinal)
                        || line.Contains("The following untracked working tree files", StringComparison.Ordinal));
                continue;
            }

            bool indented = line.StartsWith('\t') || line.StartsWith("  ", StringComparison.Ordinal);
            if (!indented)
            {
                break;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('\t'))
            {
                // 新格式：一行一个文件，文件名里的空格原样保留。
                paths.Add(trimmed);
            }
            else
            {
                // 老格式："  file1 dir/file2 dir/sub/file3"。
                paths.AddRange(trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Smart Checkout（权威 `GitBrancher.checkout` 的 "smart checkout" 分支：
    /// `GitCheckoutOperation.smartCheckoutOrNotify` → `smartCheckout()` → `GitPreservingProcess`，
    /// `GitCheckoutOperation.java:367-395,505-524`）。
    ///
    /// 语义是 **stash → 检出 → 恢复**（`GitPreservingProcess` 保存改动、执行检出、再恢复），
    /// 服务层已完整实现（含临时 stash 的标记与冲突后的续做，`GitOperationService.SmartCheckoutAsync`）。
    /// 这里只做接线，并把**同一套会话投影**（`SessionPayload`）回给界面：恢复失败即冲突会话，
    /// 临时 stash 保留，界面据此说明"改动还在、可从冲突会话继续"。
    ///
    /// 界面契约（权威 `GitSmartOperationDialog`，`GitSmartOperationDialog.java:36-125`，
    /// 文案 `GitBundle.properties:426-433,1356-1357`）：写入被拒时给出的三个选择是
    /// **Smart Checkout**（`smart.operation.dialog.smart.operation.name`）、
    /// **Force Checkout**（`checkout.operation.force.checkout`）与 **Don't Checkout**
    /// （`smart.operation.dialog.don.t.operation.name`，且是默认焦点）。Augit 的产品规格只定义了
    /// Smart Checkout（`product-spec.md:122`）与"取消"（`ux-spec.md:830`），Force Checkout 会丢弃
    /// 本地改动、属**新增能力**，按边界不实现 ⇒ 登记差异。
    /// </summary>
    private async Task<object?> SmartCheckoutAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string name = GetString(parameters, "name")
            ?? throw new ArgumentException("git/checkout-smart 需要 name 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitAdvancedOperationResult result = await new GitOperationService(runtime)
            .SmartCheckoutAsync(repository, name, cancellationToken)
            .ConfigureAwait(false);
        // 改动已 stash／恢复、HEAD 可能已换：状态缓存必须作废，界面随后读取真实状态。
        InvalidateStatusCache();
        return new
        {
            available = true,
            switched = result.IsSuccess,
            ok = result.IsSuccess,
            reason = result.ErrorMessage,
            session = SessionPayload(result.Session),
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
    ///
    /// 带 `branch`／`branches` 时改为**只取这些分支**（分支面板的「更新选中分支」）：
    /// 权威 `UpdateSelectedBranchAction` → `GitBranchActionsUtil.updateBranches()`
    /// （`plugins/git4idea/backend/src/ui/branch/GitBranchActionsUtil.kt:62-101`）对**选中的每个**
    /// 非当前分支用 `"$remoteBranchName:$localBranchName"` 这个 refspec 直接快进本地分支；
    /// 远端与远端分支由宿主从该分支自己的跟踪配置读，不采信界面传来的推断值。
    /// 多选时逐个取，第一个失败即返回并把分支名带进原因。
    /// </summary>
    private async Task<object?> FetchAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string? remoteName = GetString(parameters, "remote");
        string? branchName = GetString(parameters, "branch");
        List<string> branches = GetStringList(parameters, "branches");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitRemoteService remoteService = new(runtime);
        if (branches.Count > 0)
        {
            foreach (string name in branches)
            {
                GitRemoteOperationResult one = await remoteService
                    .FetchBranchAsync(repository, name, cancellationToken)
                    .ConfigureAwait(false);
                InvalidateStatusCache();
                if (!one.IsSuccess)
                {
                    return new
                    {
                        available = true,
                        fetched = false,
                        reason = $"{name}：{one.ErrorMessage}",
                    };
                }
            }

            return new
            {
                available = true,
                fetched = true,
                branches,
            };
        }

        GitRemoteOperationResult result = string.IsNullOrWhiteSpace(branchName)
            ? await remoteService.FetchAsync(repository, remoteName, cancellationToken).ConfigureAwait(false)
            : await remoteService.FetchBranchAsync(repository, branchName.Trim(), cancellationToken).ConfigureAwait(false);
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
        string? name = GetString(parameters, "name");
        List<string> names = GetStringList(parameters, "names");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitReferenceService references = new(runtime);
        if (action == "delete")
        {
            // 引用树多选（权威 `DeleteBranchAction.delete()`：对选中集逐个走 `GitDeleteBranchOperation`）：
            // `names`（数组）优先，兼容单值 `name`；返回被删掉与被拒的分支，界面据此说明影响。
            List<string> targets = names.Count > 0
                ? [.. names]
                : string.IsNullOrWhiteSpace(name) ? [] : [name];
            if (targets.Count == 0)
            {
                throw new ArgumentException("git/branch 的 delete 需要 name 或 names 参数。");
            }

            return await DeleteBranchesAsync(
                references,
                repository,
                targets,
                GetBool(parameters, "force") ?? false,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("git/branch 需要 name 参数。");
        }

        GitActionResult result = action switch
        {
            // 权威 `Git.CreateNewBranch.FromCommit`（`GitCreateNewBranchFromCommitAction`）：日志右键菜单
            // 是从**选中的那个提交**起分支，而不是 HEAD ⇒ `startPoint` 由界面传入（留空仍取当前引用）。
            "create" => await references.CreateBranchAsync(
                repository,
                name,
                GetString(parameters, "startPoint"),
                cancellationToken).ConfigureAwait(false),
            "rename" => await references.RenameBranchAsync(
                repository,
                GetString(parameters, "from") ?? string.Empty,
                name,
                cancellationToken).ConfigureAwait(false),
            // delete 在方法开头单独处理（多选时逐个删并把被拒的分支带回界面，见 DeleteBranchesAsync）。
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
    /// 逐个删除分支（权威 `DeleteBranchAction.delete()`：对选中集里的每个本地分支走同一套
    /// `GitDeleteBranchOperation` —— 先 `git branch -d` 由 Git 拒绝未完全合并并给出原因，
    /// 用户确认丢弃后再 `-D`）。
    ///
    /// 返回被删掉与被拒的分支：界面据此说明影响、决定是否再问一次，并在部分失败时如实报告。
    /// </summary>
    private async Task<object?> DeleteBranchesAsync(
        GitReferenceService references,
        GitRepositorySnapshot repository,
        IReadOnlyList<string> names,
        bool force,
        CancellationToken cancellationToken)
    {
        List<string> deleted = [];
        List<(string Name, string Reason)> refused = [];
        foreach (string name in names)
        {
            GitActionResult result = await references
                .DeleteBranchAsync(repository, name, force, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                deleted.Add(name);
            }
            else
            {
                refused.Add((name, result.ErrorMessage ?? "删除失败。"));
            }
        }

        InvalidateStatusCache();
        return new
        {
            available = true,
            changed = deleted.Count > 0,
            deleted,
            refused = refused.Select(item => new { name = item.Name, reason = item.Reason }),
            reason = refused.Count > 0 ? refused[0].Reason : null,
        };
    }

    /// <summary>
    /// 标签的创建与删除（权威 `GitTagDialog` / `GitDeleteTagOperation`）。
    ///
    /// 创建只支持"指向某个版本"这一种形态：`target` 为空时由 Git 取当前 HEAD；
    /// `message` 非空即建附注标签（`-a`），与权威对话框的"标签信息"字段对应。
    /// </summary>
    private async Task<object?> TagAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        string action = GetString(parameters, "action")
            ?? throw new ArgumentException("git/tag 需要 action 参数。");
        string name = GetString(parameters, "name")
            ?? throw new ArgumentException("git/tag 需要 name 参数。");
        (GitRuntimeInfo runtime, GitRepositorySnapshot? repository) = await ResolveGitAsync(cancellationToken);
        if (!runtime.IsAvailable || repository is null || repository.Kind != GitRepositoryKind.WorkingTree)
        {
            return new { available = false, reason = "当前目录不是带工作区的 Git 仓库。" };
        }

        GitReferenceService references = new(runtime);
        GitActionResult result = action switch
        {
            "create" => await references.CreateTagAsync(
                repository,
                name,
                GetString(parameters, "target"),
                GetString(parameters, "message"),
                cancellationToken).ConfigureAwait(false),
            "delete" => await references.DeleteLocalTagAsync(repository, name, cancellationToken).ConfigureAwait(false),
            _ => throw new BridgeValidationException($"未知的标签动作：{action}"),
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

        // 可选 `revision`：在**上一修订**上重新标注（权威 `AnnotatePreviousRevisionAction`
        // → `PreviousFileRevisionProvider.getPreviousRevision(lineNumber)`）。服务层本来就支持该参数，
        // 此前桥接把它硬编码成 null ⇒ 界面只能标当前工作区。
        string? revision = GetString(parameters, "revision");
        GitHistoryService history = new(runtime);
        GitBlameResult result = await history.ReadBlameAsync(repository, relative, revision, cancellationToken);
        if (!result.IsSuccess || result.Lines is not { } lines)
        {
            return new { available = false, reason = result.ErrorMessage, lines = Array.Empty<object>() };
        }

        return new
        {
            available = true,
            path = relative,
            revision,
            lines = lines.Select(line => new
            {
                number = line.LineNumber,
                hash = line.CommitHash[..Math.Min(7, line.CommitHash.Length)],
                fullHash = line.CommitHash,
                author = line.AuthorName,
                // 槽位里显示的是**短日期**（权威注释栏就是短日期），而悬停提示里的 `Date:` 用
                // `DateFormatUtil.formatDateTime`（日期 **＋ 时间**，`DateFormatUtil.java:120-124`；
                // `GitFileAnnotation.java:193` 的 `commit.description.tooltip.date`）⇒ 这里多回一个
                // 日期时间字段给提示用。格式与仓库里其它日期时间串一致（`yyyy/M/d H:mm`，本地时区）。
                date = line.AuthorDate.ToLocalTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture),
                dateTime = line.AuthorDate.ToLocalTime().ToString("yyyy/M/d H:mm", CultureInfo.InvariantCulture),
                summary = line.Summary,
                content = line.Content,
                // 「标注上一修订」用的上一修订（`git blame --line-porcelain` 的 `previous` 头）；
                // 空串表示该行在更早的修订里不存在（权威此时把该动作隐藏）。
                previousRevision = line.PreviousRevision,
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
                // 作者列的值与 tooltip 需要邮箱与提交者：权威 `FileHistoryPanelImpl.AuthorColumnInfo`
                // 用「作者 ≠ 提交者」决定值后的 `*`，tooltip 追加 `, via {提交者} <{邮箱}>`。
                authorEmail = entry.AuthorEmail,
                committerName = entry.CommitterName,
                committerEmail = entry.CommitterEmail,
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

    /// <summary>
    /// 读取"字符串或字符串数组"形式的参数（引用树多选后的多目标动作）。
    /// 数组里只取非空字符串项；缺失或类型不对时返回空表（调用方据此区分"没给"与"给了空表"）。
    /// </summary>
    private static List<string> GetStringList(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty(name, out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? single = element.GetString();
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// 读取可选的 ISO-8601 时间参数（`git/history` 的 `since`/`until`）。
    ///
    /// 给了值但解析不出来时**报错**而不是当作"没有这个筛选"：静默忽略会让界面显示
    /// 一份看起来正常、实际没有按时间筛过的历史。
    /// </summary>
    private static DateTimeOffset? GetDate(JsonElement parameters, string name)
    {
        string? text = GetString(parameters, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset value))
        {
            throw new ArgumentException($"git/history 的 {name} 参数不是有效的时间。");
        }

        return value;
    }

    private static string Serialize(long id, object? result, string? error)
    {
        return JsonSerializer.Serialize(
            error is null ? new Response(id, result, null) : new Response(id, null, error),
            PayloadOptions);
    }

    private sealed record Response(long Id, object? Result, string? Error);
}
