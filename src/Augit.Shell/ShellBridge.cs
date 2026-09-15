using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Augit.Core.Documents;
using Augit.Core.Files;
using Augit.Core.Git;
using Augit.Infrastructure.Files;
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

    private readonly string _workspaceRoot;
    private readonly SemaphoreSlim _gitGate = new(1, 1);
    private GitRuntimeInfo? _runtime;
    private GitRepositorySnapshot? _repository;

    public ShellBridge(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string WorkspaceRoot => _workspaceRoot;

    public void Dispose()
    {
        _gitGate.Dispose();
    }

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
            lineEndings = result.LineEndings.ToString(),
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

        GitStatusService service = new(runtime);
        GitStatusResult status = await service.ReadAsync(repository, cancellationToken);
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
    private async Task<(GitRuntimeInfo Runtime, GitRepositorySnapshot? Repository)> ResolveGitAsync(
        CancellationToken cancellationToken)
    {
        if (_runtime is not null)
        {
            return (_runtime, _repository);
        }

        await _gitGate.WaitAsync(cancellationToken);
        try
        {
            if (_runtime is null)
            {
                GitExecutableLocator locator = new();
                _runtime = await locator.ResolveAsync(null, cancellationToken);
            }

            if (_repository is null && _runtime.IsAvailable)
            {
                GitRepositoryService repositories = new(_runtime);
                GitRepositoryOperationResult inspection =
                    await repositories.InspectAsync(_workspaceRoot, cancellationToken);
                _repository = inspection.IsSuccess ? inspection.Repository : null;
            }

            return (_runtime, _repository);
        }
        finally
        {
            _gitGate.Release();
        }
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
