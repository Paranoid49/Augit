using Augit.Core.Git;
using Augit.Infrastructure.Terminal;

namespace Augit.Infrastructure.Git;

public sealed class GitWorktreeService : IGitWorktreeService
{
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitStatusService _statusService;
    private readonly TerminalSessionRegistry _terminalSessions;

    public GitWorktreeService(GitRuntimeInfo runtime)
        : this(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 8 * 1024 * 1024),
            new GitStatusService(runtime),
            new TerminalSessionRegistry())
    {
    }

    internal GitWorktreeService(
        GitRuntimeInfo runtime,
        GitCommandRunner runner,
        GitStatusService statusService,
        TerminalSessionRegistry terminalSessions)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        ArgumentNullException.ThrowIfNull(terminalSessions);

        _runtime = runtime;
        _runner = runner;
        _statusService = statusService;
        _terminalSessions = terminalSessions;
    }

    public async Task<GitWorktreeListResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitWorktreeListResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            ["worktree", "list", "--porcelain", "-z"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return WorktreeFailure(result);
        }

        if (result.IsOutputTruncated
            || !TryParseWorktrees(
                result.StandardOutput,
                repositoryRoot!,
                out IReadOnlyList<GitWorktreeInfo>? worktrees))
        {
            return GitWorktreeListResult.Failure(
                GitOperationFailureKind.CommandFailed,
                result.IsOutputTruncated ? "Worktree 列表超过 8 MB，已停止读取。" : "无法解析 Worktree 列表。");
        }

        return GitWorktreeListResult.Success(worktrees!);
    }

    public async Task<GitActionResult> CreateAsync(
        GitRepositorySnapshot repository,
        string destinationPath,
        string sourceBranch,
        string? newBranch = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        if (!TryResolveDestination(destinationPath, out string? destination, out error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        string? resolvedSource = await ResolveRevisionAsync(
            repositoryRoot!,
            sourceBranch,
            cancellationToken).ConfigureAwait(false);
        if (resolvedSource is null)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Worktree 来源分支不存在或不是唯一提交。");
        }

        if (!string.IsNullOrWhiteSpace(newBranch))
        {
            GitCommandResult nameResult = await RunAsync(
                repositoryRoot!,
                ["check-ref-format", "--branch", newBranch],
                GitCommandMode.LocalQuery,
                cancellationToken).ConfigureAwait(false);
            if (!nameResult.IsSuccess || newBranch.StartsWith('-'))
            {
                return GitActionResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "新 Worktree 分支名称不符合 Git 规则。");
            }
        }

        List<string> arguments = ["worktree", "add"];
        if (!string.IsNullOrWhiteSpace(newBranch))
        {
            arguments.Add("-b");
            arguments.Add(newBranch);
        }

        arguments.Add(destination!);
        arguments.Add(sourceBranch);
        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            arguments,
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    public async Task<GitActionResult> RemoveAsync(
        GitRepositorySnapshot repository,
        string worktreePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        if (!TryResolveDestination(worktreePath, out string? resolvedPath, out error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitWorktreeListResult list = await ReadAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return GitActionResult.Failure(list.FailureKind, list.ErrorMessage!);
        }

        GitWorktreeInfo? target = list.Worktrees!.FirstOrDefault(
            worktree => PathEquals(worktree.Path, resolvedPath!));
        if (target is null)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "指定路径不是当前仓库登记的 Worktree。");
        }

        if (target.IsCurrent)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "不能从当前窗口移除正在使用的 Worktree。");
        }

        if (!Directory.Exists(target.Path))
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Worktree 目录不存在，不能执行安全移除。");
        }

        using TerminalSessionLease? removalGuard = _terminalSessions.TryAcquire(target.Path);
        if (removalGuard is null)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Worktree 对应的 Augit 窗口仍有运行中的内置终端会话，关闭终端后才能安全移除。");
        }

        GitCommandResult status = await RunAsync(
            target.Path,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=all"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            return GitActionResult.Failure(NormalizeFailure(status), status.ErrorMessage);
        }

        if (status.IsOutputTruncated || status.StandardOutput.Length > 0)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Worktree 存在本地改动或未跟踪文件，不能安全移除。");
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            ["worktree", "remove", target.Path],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    internal static bool TryParseWorktrees(
        string output,
        string currentRepositoryRoot,
        out IReadOnlyList<GitWorktreeInfo>? worktrees)
    {
        worktrees = null;
        List<GitWorktreeInfo> parsed = [];
        WorktreeBuilder? builder = null;
        foreach (string field in output.Split('\0'))
        {
            if (field.Length == 0)
            {
                if (builder is not null)
                {
                    if (!builder.TryBuild(currentRepositoryRoot, out GitWorktreeInfo? item))
                    {
                        return false;
                    }

                    parsed.Add(item!);
                    builder = null;
                }

                continue;
            }

            int separator = field.IndexOf(' ');
            string key = separator < 0 ? field : field[..separator];
            string value = separator < 0 ? string.Empty : field[(separator + 1)..];
            if (key == "worktree")
            {
                if (builder is not null)
                {
                    if (!builder.TryBuild(currentRepositoryRoot, out GitWorktreeInfo? previous))
                    {
                        return false;
                    }

                    parsed.Add(previous!);
                }

                builder = new() { Path = value };
                continue;
            }

            if (builder is null)
            {
                return false;
            }

            switch (key)
            {
                case "HEAD":
                    builder.CommitHash = value;
                    break;
                case "branch":
                    builder.Branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value[11..] : value;
                    break;
                case "bare":
                    builder.IsBare = true;
                    break;
                case "detached":
                    builder.IsDetached = true;
                    break;
                case "locked":
                    builder.IsLocked = true;
                    builder.LockReason = value.Length == 0 ? null : value;
                    break;
                case "prunable":
                    builder.IsPrunable = true;
                    builder.PruneReason = value.Length == 0 ? null : value;
                    break;
            }
        }

        if (builder is not null)
        {
            if (!builder.TryBuild(currentRepositoryRoot, out GitWorktreeInfo? final))
            {
                return false;
            }

            parsed.Add(final!);
        }

        worktrees = parsed;
        return true;
    }

    private async Task<string?> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision)
            || revision.StartsWith('-')
            || revision.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            return null;
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result.StandardOutput.Trim() : null;
    }

    private async Task<GitActionResult> ReadActualStatusAsync(GitRepositorySnapshot repository)
    {
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitActionResult.Success(status.IsSuccess ? status.Snapshot : null);
    }

    private async Task<GitActionResult> MutationFailureAsync(
        GitRepositorySnapshot repository,
        GitCommandResult result)
    {
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitActionResult.Failure(
            NormalizeFailure(result),
            result.ErrorMessage,
            status.IsSuccess ? status.Snapshot : null);
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(
            _runtime.ExecutablePath!,
            workingDirectory,
            arguments,
            mode,
            cancellationToken);
    }

    private static bool TryGetRepositoryRoot(
        GitRepositorySnapshot repository,
        out string? repositoryRoot,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(repository);
        repositoryRoot = repository.RepositoryRoot;
        error = null;
        if (repository.Kind != GitRepositoryKind.WorkingTree || string.IsNullOrWhiteSpace(repositoryRoot))
        {
            error = "Worktree 管理只适用于具有工作区的 Git 仓库。";
            return false;
        }

        return true;
    }

    private static bool TryResolveDestination(string path, out string? fullPath, out string? error)
    {
        fullPath = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Worktree 路径不能为空。";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = "Worktree 路径无效。";
            return false;
        }
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static GitWorktreeListResult WorktreeFailure(GitCommandResult result)
    {
        return GitWorktreeListResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }

    private sealed class WorktreeBuilder
    {
        internal string? Path { get; set; }

        internal string? CommitHash { get; set; }

        internal string? Branch { get; set; }

        internal bool IsBare { get; set; }

        internal bool IsDetached { get; set; }

        internal bool IsLocked { get; set; }

        internal string? LockReason { get; set; }

        internal bool IsPrunable { get; set; }

        internal string? PruneReason { get; set; }

        internal bool TryBuild(string currentRepositoryRoot, out GitWorktreeInfo? worktree)
        {
            worktree = null;
            if (string.IsNullOrWhiteSpace(Path))
            {
                return false;
            }

            worktree = new(
                Path,
                CommitHash,
                Branch,
                IsBare,
                IsDetached,
                IsLocked,
                LockReason,
                IsPrunable,
                PruneReason,
                PathEquals(Path, currentRepositoryRoot));
            return true;
        }
    }
}
