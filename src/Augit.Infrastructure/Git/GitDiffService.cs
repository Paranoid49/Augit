using System.Globalization;
using Augit.Core.Files;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitDiffService : IGitDiffService
{
    private const long MaximumSideBytes = 10L * 1024 * 1024;
    private const int MaximumDiffBytes = 20 * 1024 * 1024;
    private const string FullFileContextArgument = "--unified=10485760";
    // Git 的空树对象：用它表示"该侧没有这个文件"，使新增/删除与首提交
    // 都能用同一条 diff 命令表达，不必为每种情况拼不同的参数。
    private const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    private static readonly IReadOnlySet<int> NoIndexSuccessExitCodes = new HashSet<int> { 1 };
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _queryRunner;
    private readonly GitCommandRunner _diffRunner;

    public GitDiffService(GitRuntimeInfo runtime)
        : this(
            runtime,
            new GitCommandRunner(),
            new GitCommandRunner(TimeSpan.FromSeconds(30), MaximumDiffBytes))
    {
    }

    internal GitDiffService(
        GitRuntimeInfo runtime,
        GitCommandRunner queryRunner,
        GitCommandRunner diffRunner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _queryRunner = queryRunner;
        _diffRunner = diffRunner;
    }

    public async Task<GitDiffResult> CreateAsync(
        GitRepositorySnapshot repository,
        GitChangedFile changedFile,
        GitDiffOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(changedFile);
        ArgumentNullException.ThrowIfNull(options);
        if (repository.Kind != GitRepositoryKind.WorkingTree || string.IsNullOrWhiteSpace(repository.RepositoryRoot))
        {
            return GitDiffResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "diff 只适用于具有工作区的 Git 仓库。");
        }

        if (!TryResolveChangedPath(
            repository.RepositoryRoot,
            changedFile.RelativePath,
            out string? currentPath))
        {
            return GitDiffResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "改动文件路径越过了仓库边界。");
        }

        // 历史比较（规格 §7.8）：两侧都取自 Git，不读取工作区文件。
        // 工作区 Diff 与引用比较仍走下面的「版本 ↔ 工作区」路径。
        if (options.IsRevisionComparison)
        {
            return await CreateRevisionComparisonAsync(
                repository.RepositoryRoot,
                changedFile,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        long newSize = File.Exists(currentPath) ? new FileInfo(currentPath).Length : 0;
        if (changedFile.Kind is not GitChangeKind.Deleted && !File.Exists(currentPath))
        {
            return GitDiffResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "文件状态已经变化，请刷新 Changes 后重试。");
        }

        (bool hasOldSide, long oldSize, GitDiffResult? oldSizeFailure) = await ReadOldSizeAsync(
            repository.RepositoryRoot,
            changedFile,
            cancellationToken).ConfigureAwait(false);
        if (oldSizeFailure is not null)
        {
            return oldSizeFailure;
        }

        if (oldSize > MaximumSideBytes || newSize > MaximumSideBytes)
        {
            return GitDiffResult.Success(new(
                GitDiffContentStatus.SideTooLarge,
                changedFile.RelativePath,
                changedFile.OriginalRelativePath,
                oldSize,
                newSize,
                null));
        }

        List<string> arguments = CreateDiffArguments(
            currentPath!,
            changedFile,
            options,
            hasOldSide);
        GitCommandResult result = hasOldSide
            ? await _diffRunner.RunAsync(
                _runtime.ExecutablePath!,
                repository.RepositoryRoot,
                arguments,
                GitCommandMode.LocalQuery,
                cancellationToken).ConfigureAwait(false)
            : await _diffRunner.RunWithAdditionalSuccessExitCodesAsync(
                _runtime.ExecutablePath!,
                repository.RepositoryRoot,
                arguments,
                GitCommandMode.LocalQuery,
                NoIndexSuccessExitCodes,
                cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        if (result.IsOutputTruncated)
        {
            return GitDiffResult.Success(new(
                GitDiffContentStatus.OutputTooLarge,
                changedFile.RelativePath,
                changedFile.OriginalRelativePath,
                oldSize,
                newSize,
                null));
        }

        GitDiffContentStatus status = IsBinaryPatch(result.StandardOutput)
            ? GitDiffContentStatus.Binary
            : GitDiffContentStatus.Ready;
        return GitDiffResult.Success(new(
            status,
            changedFile.RelativePath,
            changedFile.OriginalRelativePath,
            oldSize,
            newSize,
            result.StandardOutput));
    }

    /// <summary>
    /// 历史比较（规格 §7.8）：比较两个 Git 版本中同一路径的内容。
    /// </summary>
    /// <remarks>
    /// 与「版本 ↔ 工作区」的区别在于**右侧也来自 Git**，因此不能复用工作区路径的
    /// 存在性检查与大小统计。git 的空树对象哈希用于表示"该侧不存在"，
    /// 这样新增文件（父版本无此文件）与首提交（无父提交）都走同一条 diff 命令。
    /// </remarks>
    private async Task<GitDiffResult> CreateRevisionComparisonAsync(
        string repositoryRoot,
        GitChangedFile changedFile,
        GitDiffOptions options,
        CancellationToken cancellationToken)
    {
        string? baseRevision = await ResolveRevisionAsync(
            repositoryRoot,
            options.BaseRevision,
            cancellationToken).ConfigureAwait(false);
        if (baseRevision is null)
        {
            return GitDiffResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "无法解析比较的起始版本。");
        }

        string? targetRevision = await ResolveRevisionAsync(
            repositoryRoot,
            options.TargetRevision!,
            cancellationToken).ConfigureAwait(false);
        if (targetRevision is null)
        {
            return GitDiffResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "无法解析比较的目标版本。");
        }

        string path = changedFile.RelativePath;
        (bool hasOldSide, long oldSize, GitDiffResult? oldFailure) = await ReadBlobSizeAsync(
            repositoryRoot,
            baseRevision,
            path,
            cancellationToken).ConfigureAwait(false);
        if (oldFailure is not null)
        {
            return oldFailure;
        }

        (bool hasNewSide, long newSize, GitDiffResult? newFailure) = await ReadBlobSizeAsync(
            repositoryRoot,
            targetRevision,
            path,
            cancellationToken).ConfigureAwait(false);
        if (newFailure is not null)
        {
            return newFailure;
        }

        if (oldSize > MaximumSideBytes || newSize > MaximumSideBytes)
        {
            return GitDiffResult.Success(new(
                GitDiffContentStatus.SideTooLarge,
                path,
                changedFile.OriginalRelativePath,
                oldSize,
                newSize,
                null));
        }

        List<string> arguments =
        [
            "-c",
            "core.quotePath=false",
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--find-renames=50%",
            "--full-index",
            FullFileContextArgument,
        ];
        if (options.IgnoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        arguments.Add(hasOldSide ? baseRevision : EmptyTreeHash);
        arguments.Add(hasNewSide ? targetRevision : EmptyTreeHash);
        arguments.Add("--");
        arguments.Add(path);

        GitCommandResult result = await _diffRunner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            arguments,
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        if (result.IsOutputTruncated)
        {
            return GitDiffResult.Success(new(
                GitDiffContentStatus.OutputTooLarge,
                path,
                changedFile.OriginalRelativePath,
                oldSize,
                newSize,
                null));
        }

        GitDiffContentStatus status = IsBinaryPatch(result.StandardOutput)
            ? GitDiffContentStatus.Binary
            : GitDiffContentStatus.Ready;
        return GitDiffResult.Success(new(
            status,
            path,
            changedFile.OriginalRelativePath,
            oldSize,
            newSize,
            result.StandardOutput));
    }

    /// <summary>解析单个版本为提交哈希；解析失败返回 <c>null</c>。</summary>
    private async Task<string?> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-'))
        {
            return null;
        }

        // 空树对象不是提交，`^{commit}` 会解析失败；它是"该侧没有内容"的合法表示，
        // 由调用方（首提交的历史比较）显式给出，这里原样接受。
        if (string.Equals(revision, EmptyTreeHash, StringComparison.OrdinalIgnoreCase))
        {
            return EmptyTreeHash;
        }

        GitCommandResult result = await _queryRunner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim()
            : null;
    }

    /// <summary>
    /// 读取某个版本中路径的内容大小；该版本没有此文件时返回 <c>(false, 0, null)</c>。
    /// 只有真实的命令失败才返回失败结果。
    /// </summary>
    private async Task<(bool HasSide, long Size, GitDiffResult? Failure)> ReadBlobSizeAsync(
        string repositoryRoot,
        string revision,
        string relativePath,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await _queryRunner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            ["cat-file", "-s", $"{revision}:{relativePath}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            // 该版本里没有这个路径是正常情况（新增或删除），不是错误。
            return (false, 0, null);
        }

        if (!long.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long size)
            || size < 0)
        {
            return (
                false,
                0,
                GitDiffResult.Failure(
                    GitOperationFailureKind.CommandFailed,
                    "无法读取该版本中文件的大小。"));
        }

        return (true, size, null);
    }

    private async Task<(bool HasOldSide, long OldSize, GitDiffResult? Failure)> ReadOldSizeAsync(
        string repositoryRoot,
        GitChangedFile changedFile,
        CancellationToken cancellationToken)
    {
        if (changedFile.Kind is GitChangeKind.Added or GitChangeKind.Untracked)
        {
            return (false, 0, null);
        }

        string oldPath = changedFile.OriginalRelativePath ?? changedFile.RelativePath;
        GitCommandResult result = await _queryRunner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            ["cat-file", "-s", $"HEAD:{oldPath}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return (false, 0, Failure(result));
        }

        if (!long.TryParse(result.StandardOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long size)
            || size < 0)
        {
            return (
                false,
                0,
                GitDiffResult.Failure(
                    GitOperationFailureKind.CommandFailed,
                    "无法读取 HEAD 中文件的大小。"));
        }

        return (true, size, null);
    }

    private static List<string> CreateDiffArguments(
        string currentPath,
        GitChangedFile changedFile,
        GitDiffOptions options,
        bool hasOldSide)
    {
        List<string> arguments =
        [
            "-c",
            "core.quotePath=false",
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--find-renames=50%",
            "--full-index",
            FullFileContextArgument,
        ];
        if (options.IgnoreWhitespace)
        {
            arguments.Add("--ignore-all-space");
        }

        if (!hasOldSide)
        {
            arguments.Add("--no-index");
            arguments.Add("--");
            arguments.Add("/dev/null");
            arguments.Add(currentPath);
            return arguments;
        }

        // 左侧基准由调用方决定：工作区 Diff 是 HEAD，引用比较是具体引用。
        arguments.Add(string.IsNullOrWhiteSpace(options.BaseRevision) ? "HEAD" : options.BaseRevision);
        arguments.Add("--");
        if (!string.IsNullOrWhiteSpace(changedFile.OriginalRelativePath))
        {
            arguments.Add(changedFile.OriginalRelativePath);
        }

        arguments.Add(changedFile.RelativePath);
        return arguments;
    }

    private static bool TryResolveChangedPath(string repositoryRoot, string relativePath, out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            return false;
        }

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!WorkspacePathRules.IsWithin(repositoryRoot, candidate))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsBinaryPatch(string patch)
    {
        return patch.Contains("Binary files ", StringComparison.Ordinal)
            || patch.Contains("GIT binary patch", StringComparison.Ordinal);
    }

    private static GitDiffResult Failure(GitCommandResult result)
    {
        return GitDiffResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage);
    }
}
