using System.Globalization;
using Augit.Core.Files;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitDiffService : IGitDiffService
{
    private const long MaximumSideBytes = 10L * 1024 * 1024;
    private const int MaximumDiffBytes = 20 * 1024 * 1024;
    private const string FullFileContextArgument = "--unified=10485760";
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
