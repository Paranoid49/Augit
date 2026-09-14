using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitRepositoryService : IGitRepositoryService
{
    private readonly GitCommandRunner _runner;

    public GitRepositoryService(GitRuntimeInfo runtime)
        : this(runtime, new GitCommandRunner())
    {
    }

    internal GitRepositoryService(GitRuntimeInfo runtime, GitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        Runtime = runtime;
        _runner = runner;
    }

    public GitRuntimeInfo Runtime { get; }

    public async Task<GitRepositoryOperationResult> InspectAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveExistingDirectory(workspacePath, out string? fullPath, out string? validationError))
        {
            return GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitCommandResult gitDirectoryResult = await RunQueryAsync(
            fullPath!,
            ["rev-parse", "--path-format=absolute", "--absolute-git-dir"],
            cancellationToken).ConfigureAwait(false);
        if (!gitDirectoryResult.IsSuccess)
        {
            if (IsNotRepository(gitDirectoryResult.ErrorMessage))
            {
                return GitRepositoryOperationResult.Success(GitRepositorySnapshot.PlainDirectory(fullPath!));
            }

            return ToFailure(gitDirectoryResult);
        }

        string gitDirectory = NormalizeOutputPath(gitDirectoryResult.StandardOutput);
        GitCommandResult bareResult = await RunQueryAsync(
            fullPath!,
            ["rev-parse", "--is-bare-repository"],
            cancellationToken).ConfigureAwait(false);
        if (!bareResult.IsSuccess)
        {
            return ToFailure(bareResult);
        }

        bool isBare = bareResult.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        GitCommandResult commonDirectoryResult = await RunQueryAsync(
            fullPath!,
            ["rev-parse", "--path-format=absolute", "--git-common-dir"],
            cancellationToken).ConfigureAwait(false);
        if (!commonDirectoryResult.IsSuccess)
        {
            return ToFailure(commonDirectoryResult);
        }

        string commonDirectory = NormalizeOutputPath(commonDirectoryResult.StandardOutput);
        if (isBare)
        {
            GitRepositorySnapshot bareRepository = new(
                GitRepositoryKind.BareRepository,
                fullPath!,
                null,
                gitDirectory,
                commonDirectory,
                DetectOperation(gitDirectory),
                false);
            return GitRepositoryOperationResult.Success(bareRepository);
        }

        GitCommandResult rootResult = await RunQueryAsync(
            fullPath!,
            ["rev-parse", "--path-format=absolute", "--show-toplevel"],
            cancellationToken).ConfigureAwait(false);
        if (!rootResult.IsSuccess)
        {
            return ToFailure(rootResult);
        }

        GitCommandResult conflictsResult = await RunQueryAsync(
            fullPath!,
            ["ls-files", "--unmerged", "-z"],
            cancellationToken).ConfigureAwait(false);
        if (!conflictsResult.IsSuccess)
        {
            return ToFailure(conflictsResult);
        }

        GitRepositorySnapshot workingTree = new(
            GitRepositoryKind.WorkingTree,
            fullPath!,
            NormalizeOutputPath(rootResult.StandardOutput),
            gitDirectory,
            commonDirectory,
            DetectOperation(gitDirectory),
            conflictsResult.StandardOutput.Length > 0);
        return GitRepositoryOperationResult.Success(workingTree);
    }

    public async Task<GitRepositoryOperationResult> InitializeAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        GitRepositoryOperationResult before = await InspectAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess)
        {
            return before;
        }

        if (before.Repository!.IsRepository)
        {
            return GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前目录已经属于 Git 仓库。",
                before.Repository);
        }

        GitCommandResult result = await _runner.RunAsync(
            Runtime.ExecutablePath!,
            before.Repository.WorkspacePath,
            ["init"],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        GitRepositoryOperationResult actualState = await InspectWithoutCancellationAsync(before.Repository.WorkspacePath).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return ToFailure(result, actualState.Repository);
        }

        return actualState.IsSuccess && actualState.Repository?.IsRepository == true
            ? actualState
            : GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.CommandFailed,
                actualState.ErrorMessage ?? "Git 初始化完成后未识别到仓库。",
                actualState.Repository);
    }

    public async Task<GitRepositoryOperationResult> CloneAsync(
        string source,
        string destinationPath,
        int? depth = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "克隆地址不能为空。");
        }

        if (depth is <= 0)
        {
            return GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "浅克隆深度必须是正整数。");
        }

        if (!TryResolveCloneDestination(destinationPath, out string? fullDestination, out string? parent, out string? validationError))
        {
            return GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        List<string> arguments = ["clone"];
        if (depth is int cloneDepth)
        {
            arguments.Add("--depth");
            arguments.Add(cloneDepth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        arguments.Add("--");
        arguments.Add(source);
        arguments.Add(fullDestination!);
        GitCommandResult result = await _runner.RunAsync(
            Runtime.ExecutablePath!,
            parent!,
            arguments,
            GitCommandMode.Network,
            cancellationToken).ConfigureAwait(false);
        GitRepositoryOperationResult? actualState = Directory.Exists(fullDestination)
            ? await InspectWithoutCancellationAsync(fullDestination).ConfigureAwait(false)
            : null;
        if (!result.IsSuccess)
        {
            return ToFailure(result, actualState?.Repository);
        }

        return actualState is { IsSuccess: true, Repository.IsRepository: true }
            ? actualState
            : GitRepositoryOperationResult.Failure(
                GitOperationFailureKind.CommandFailed,
                actualState?.ErrorMessage ?? "Git 克隆完成后未识别到仓库。",
                actualState?.Repository);
    }

    private Task<GitCommandResult> RunQueryAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(
            Runtime.ExecutablePath!,
            workingDirectory,
            arguments,
            GitCommandMode.LocalQuery,
            cancellationToken);
    }

    private async Task<GitRepositoryOperationResult> InspectWithoutCancellationAsync(string workspacePath)
    {
        return await InspectAsync(workspacePath, CancellationToken.None).ConfigureAwait(false);
    }

    internal static GitOperationKind DetectOperation(string gitDirectory)
    {
        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge"))
            || Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")))
        {
            return GitOperationKind.Rebase;
        }

        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")))
        {
            return GitOperationKind.Merge;
        }

        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD")))
        {
            return GitOperationKind.CherryPick;
        }

        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD")))
        {
            return GitOperationKind.Revert;
        }

        string sequencerTodo = Path.Combine(gitDirectory, "sequencer", "todo");
        if (File.Exists(sequencerTodo))
        {
            try
            {
                string firstAction = File.ReadLines(sequencerTodo).FirstOrDefault() ?? string.Empty;
                return firstAction.StartsWith("revert ", StringComparison.Ordinal)
                    ? GitOperationKind.Revert
                    : GitOperationKind.CherryPick;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return File.Exists(Path.Combine(gitDirectory, "BISECT_LOG"))
            ? GitOperationKind.Bisect
            : GitOperationKind.None;
    }

    private static bool TryResolveExistingDirectory(
        string workspacePath,
        out string? fullPath,
        out string? errorMessage)
    {
        fullPath = null;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            errorMessage = "工作区路径不能为空。";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(workspacePath);
            if (!Directory.Exists(fullPath))
            {
                errorMessage = "工作区目录不存在。";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            errorMessage = "工作区路径无效或不可访问。";
            return false;
        }
    }

    private static bool TryResolveCloneDestination(
        string destinationPath,
        out string? fullDestination,
        out string? parent,
        out string? errorMessage)
    {
        fullDestination = null;
        parent = null;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            errorMessage = "克隆目标路径不能为空。";
            return false;
        }

        try
        {
            fullDestination = Path.GetFullPath(destinationPath);
            parent = Path.GetDirectoryName(fullDestination);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                errorMessage = "克隆目标的上级目录不存在。";
                return false;
            }

            if (File.Exists(fullDestination))
            {
                errorMessage = "克隆目标已被文件占用。";
                return false;
            }

            if (Directory.Exists(fullDestination) && Directory.EnumerateFileSystemEntries(fullDestination).Any())
            {
                errorMessage = "克隆目标目录必须为空。";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            errorMessage = "克隆目标路径无效或不可访问。";
            return false;
        }
    }

    private static bool IsNotRepository(string errorMessage)
    {
        return errorMessage.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOutputPath(string output)
    {
        return Path.GetFullPath(output.Trim());
    }

    private static GitRepositoryOperationResult ToFailure(
        GitCommandResult result,
        GitRepositorySnapshot? repository = null)
    {
        return GitRepositoryOperationResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage,
            repository);
    }
}
