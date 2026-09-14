using System.Globalization;
using System.Text.RegularExpressions;
using Augit.Core.Git;
using Augit.Infrastructure.Interop;

namespace Augit.Infrastructure.Git;

public sealed class GitWorkspaceStateService : IGitWorkspaceStateService
{
    private const char FieldSeparator = '\x1f';
    private const int MaximumStashOutputBytes = 20 * 1024 * 1024;
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitCommandRunner _contentRunner;
    private readonly GitStatusService _statusService;
    private readonly Func<string, RecycleBinResult> _moveToRecycleBin;

    public GitWorkspaceStateService(GitRuntimeInfo runtime)
        : this(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 8 * 1024 * 1024),
            new GitCommandRunner(TimeSpan.FromSeconds(30), MaximumStashOutputBytes),
            new GitStatusService(runtime),
            WindowsRecycleBin.Move)
    {
    }

    internal GitWorkspaceStateService(
        GitRuntimeInfo runtime,
        GitCommandRunner runner,
        GitCommandRunner contentRunner,
        GitStatusService statusService,
        Func<string, RecycleBinResult> moveToRecycleBin)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(moveToRecycleBin);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _runner = runner;
        _contentRunner = contentRunner;
        _statusService = statusService;
        _moveToRecycleBin = moveToRecycleBin;
    }

    public async Task<GitStashListResult> ReadStashesAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitStashListResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            [
                "stash",
                "list",
                $"--format=%gd{FieldSeparator}%H{FieldSeparator}%P{FieldSeparator}%aI{FieldSeparator}%s",
            ],
            GitCommandMode.LocalQuery,
            _runner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StashListFailure(result);
        }

        if (result.IsOutputTruncated || !TryParseStashes(result.StandardOutput, out IReadOnlyList<GitStashInfo>? stashes))
        {
            return GitStashListResult.Failure(
                GitOperationFailureKind.CommandFailed,
                result.IsOutputTruncated ? "Stash 列表超过 8 MB，已停止读取。" : "无法解析 Stash 列表。");
        }

        return GitStashListResult.Success(stashes!);
    }

    public async Task<GitStashContentResult> ReadStashContentAsync(
        GitRepositorySnapshot repository,
        string stashReference,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitStashContentResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        if (!IsStashReference(stashReference))
        {
            return GitStashContentResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Stash 引用无效。");
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            [
                "-c",
                "core.quotePath=false",
                "stash",
                "show",
                "--patch",
                "--include-untracked",
                "--no-ext-diff",
                "--no-textconv",
                stashReference,
            ],
            GitCommandMode.LocalQuery,
            _contentRunner,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StashContentFailure(result);
        }

        GitDiffContentStatus status = result.IsOutputTruncated
            ? GitDiffContentStatus.OutputTooLarge
            : IsBinaryPatch(result.StandardOutput)
                ? GitDiffContentStatus.Binary
                : GitDiffContentStatus.Ready;
        return GitStashContentResult.Success(new(
            status,
            $"{stashReference}^",
            stashReference,
            null,
            status == GitDiffContentStatus.Ready ? result.StandardOutput : null));
    }

    public Task<GitActionResult> StashAsync(
        GitRepositorySnapshot repository,
        string? message,
        bool includeUntracked,
        CancellationToken cancellationToken = default)
    {
        return StashWithOptionsAsync(repository, message, includeUntracked, keepIndex: false, cancellationToken);
    }

    public async Task<GitActionResult> StashWithOptionsAsync(
        GitRepositorySnapshot repository,
        string? message,
        bool includeUntracked,
        bool keepIndex = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out _, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        List<string> arguments = ["stash", "push"];
        if (includeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        if (keepIndex)
        {
            arguments.Add("--keep-index");
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("--message");
            arguments.Add(message);
        }

        return await RunMutationAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task<GitActionResult> UnstashAsync(
        GitRepositorySnapshot repository,
        string stashReference,
        bool keepStash,
        CancellationToken cancellationToken = default)
    {
        if (!IsStashReference(stashReference))
        {
            return Task.FromResult(GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Stash 引用无效。"));
        }

        return RunMutationAsync(
            repository,
            ["stash", keepStash ? "apply" : "pop", "--index", stashReference],
            cancellationToken);
    }

    public Task<GitActionResult> DeleteStashAsync(
        GitRepositorySnapshot repository,
        string stashReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsStashReference(stashReference))
        {
            return Task.FromResult(GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Stash 引用无效。"));
        }

        return RunMutationAsync(
            repository,
            ["stash", "drop", stashReference],
            cancellationToken);
    }

    public async Task<GitActionResult> ResetAsync(
        GitRepositorySnapshot repository,
        string targetRevision,
        GitResetMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult resolution = await ResolveRevisionAsync(
            repositoryRoot!,
            targetRevision,
            cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            // --verify --quiet 的退出码 1 表示目标不存在；取消、超时或运行环境错误不能伪装成输入错误。
            bool invalidTarget = resolution.FailureKind == GitOperationFailureKind.InvalidRequest
                || resolution is { ExitCode: 1, FailureKind: GitOperationFailureKind.CommandFailed };
            if (!invalidTarget)
            {
                return await MutationFailureAsync(repository, resolution).ConfigureAwait(false);
            }

            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Reset 目标不存在或不是唯一提交。");
        }
        string resolved = resolution.StandardOutput.Trim();

        string option = mode switch
        {
            GitResetMode.Soft => "--soft",
            GitResetMode.Mixed => "--mixed",
            GitResetMode.Hard => "--hard",
            _ => string.Empty,
        };
        if (option.Length == 0)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, "Reset 模式无效。");
        }

        return await RunMutationAsync(repository, ["reset", option, resolved], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GitActionResult> RollbackAsync(
        GitRepositorySnapshot repository,
        GitChangedFile changedFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changedFile);
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        if (changedFile.Kind == GitChangeKind.Unmerged)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "冲突文件必须在阶段四的冲突流程中处理，不能直接 Rollback。");
        }

        if (!GitPathValidator.TryNormalizeRelativePath(
            repositoryRoot!,
            changedFile.RelativePath,
            out string? relativePath,
            out string? fullPath))
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Rollback 文件路径越过了仓库边界。");
        }

        string? originalRelativePath = null;
        string? originalFullPath = null;
        if (!string.IsNullOrWhiteSpace(changedFile.OriginalRelativePath)
            && !GitPathValidator.TryNormalizeRelativePath(
                repositoryRoot!,
                changedFile.OriginalRelativePath,
                out originalRelativePath,
                out originalFullPath))
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Rollback 原文件路径越过了仓库边界。");
        }

        if (changedFile.Group == GitChangeGroup.UnversionedFiles)
        {
            RecycleBinResult recycle = _moveToRecycleBin(fullPath!);
            return recycle.IsSuccess
                ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
                : await FailureWithActualStatusAsync(repository, recycle.ErrorMessage!).ConfigureAwait(false);
        }

        if (changedFile.Kind is GitChangeKind.Added or GitChangeKind.Copied)
        {
            if (changedFile.HasStagedChanges)
            {
                GitCommandResult unstage = await RunAsync(
                    repositoryRoot!,
                    ["restore", "--staged", "--", relativePath!],
                    GitCommandMode.LocalWrite,
                    _runner,
                    cancellationToken).ConfigureAwait(false);
                if (!unstage.IsSuccess)
                {
                    return await MutationFailureAsync(repository, unstage).ConfigureAwait(false);
                }
            }

            RecycleBinResult recycle = _moveToRecycleBin(fullPath!);
            return recycle.IsSuccess
                ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
                : await FailureWithActualStatusAsync(repository, recycle.ErrorMessage!).ConfigureAwait(false);
        }

        if (changedFile.Kind == GitChangeKind.Renamed && originalRelativePath is not null)
        {
            GitCommandResult unstage = await RunAsync(
                repositoryRoot!,
                ["restore", "--staged", "--", originalRelativePath, relativePath!],
                GitCommandMode.LocalWrite,
                _runner,
                cancellationToken).ConfigureAwait(false);
            if (!unstage.IsSuccess)
            {
                return await MutationFailureAsync(repository, unstage).ConfigureAwait(false);
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                RecycleBinResult recycle = _moveToRecycleBin(fullPath!);
                if (!recycle.IsSuccess)
                {
                    return await FailureWithActualStatusAsync(repository, recycle.ErrorMessage!).ConfigureAwait(false);
                }
            }

            GitCommandResult restoreOriginal = await RunAsync(
                repositoryRoot!,
                ["restore", "--source=HEAD", "--worktree", "--", originalRelativePath],
                GitCommandMode.LocalWrite,
                _runner,
                cancellationToken).ConfigureAwait(false);
            return restoreOriginal.IsSuccess
                ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
                : await MutationFailureAsync(repository, restoreOriginal).ConfigureAwait(false);
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            ["restore", "--source=HEAD", "--staged", "--worktree", "--", relativePath!],
            GitCommandMode.LocalWrite,
            _runner,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    internal static bool TryParseStashes(string output, out IReadOnlyList<GitStashInfo>? stashes)
    {
        stashes = null;
        List<GitStashInfo> parsed = [];
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length != 5
                || !DateTimeOffset.TryParse(
                    fields[3],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset date))
            {
                return false;
            }

            string parent = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            (string branch, string message) = ParseStashSubject(fields[4]);
            parsed.Add(new(fields[0], fields[1], parent, date, fields[4], branch, message));
        }

        stashes = parsed;
        return true;
    }

    internal static (string Branch, string Message) ParseStashSubject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        int prefixLength = subject.StartsWith("On ", StringComparison.OrdinalIgnoreCase)
            ? 3
            : subject.StartsWith("WIP on ", StringComparison.OrdinalIgnoreCase)
                ? 7
                : 0;
        int separator = prefixLength == 0
            ? -1
            : subject.IndexOf(": ", prefixLength, StringComparison.Ordinal);
        if (separator <= prefixLength)
        {
            return (string.Empty, subject);
        }

        string branch = subject[prefixLength..separator].Trim();
        string message = subject[(separator + 2)..].Trim();
        return (branch, message.Length == 0 ? subject : message);
    }

    private async Task<GitActionResult> RunMutationAsync(
        GitRepositorySnapshot repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            arguments,
            GitCommandMode.LocalWrite,
            _runner,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualStatusAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    private async Task<GitActionResult> ReadActualStatusAsync(GitRepositorySnapshot repository)
    {
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitActionResult.Success(status.IsSuccess ? status.Snapshot : null);
    }

    private async Task<GitActionResult> FailureWithActualStatusAsync(
        GitRepositorySnapshot repository,
        string errorMessage)
    {
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitActionResult.Failure(
            GitOperationFailureKind.CommandFailed,
            errorMessage,
            status.IsSuccess ? status.Snapshot : null);
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

    private async Task<GitCommandResult> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision)
            || revision.StartsWith('-')
            || revision.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            return new(
                null,
                string.Empty,
                "Reset 目标无效。",
                GitOperationFailureKind.InvalidRequest);
        }

        return await RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            GitCommandMode.LocalQuery,
            _runner,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        GitCommandRunner runner,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(
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
            error = "本地状态管理只适用于具有工作区的 Git 仓库。";
            return false;
        }

        return true;
    }

    private static bool IsStashReference(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, "^stash@\\{[0-9]+\\}$", RegexOptions.CultureInvariant);
    }

    private static bool IsBinaryPatch(string patch)
    {
        return patch.Contains("Binary files ", StringComparison.Ordinal)
            || patch.Contains("GIT binary patch", StringComparison.Ordinal);
    }

    private static GitStashListResult StashListFailure(GitCommandResult result)
    {
        return GitStashListResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitStashContentResult StashContentFailure(GitCommandResult result)
    {
        return GitStashContentResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }
}
