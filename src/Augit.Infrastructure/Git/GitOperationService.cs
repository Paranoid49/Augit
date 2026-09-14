using System.Globalization;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitOperationService : IGitOperationService
{
    private const string SmartCheckoutMarker = "Augit Smart Checkout ";
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitStatusService _statusService;
    private SmartCheckoutContext? _smartCheckout;

    public GitOperationService(GitRuntimeInfo runtime)
        : this(runtime, new GitCommandRunner(TimeSpan.FromSeconds(30), 16 * 1024 * 1024))
    {
    }

    internal GitOperationService(GitRuntimeInfo runtime, GitCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _runner = runner;
        _statusService = new(runtime, runner);
    }

    public async Task<GitAdvancedOperationResult> InspectAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out _, out string? validationError))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        return await ReadActualStateAsync(repository, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitAdvancedOperationResult> StartAsync(
        GitRepositorySnapshot repository,
        GitOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetRepositoryRoot(repository, out string? root, out string? validationError))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitAdvancedOperationResult before = await ReadActualStateAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || before.Session is null)
        {
            return before;
        }

        if (before.Session.IsInProgress || before.Session.HasConflicts)
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前仓库已有进行中的 Git 操作或未解决冲突。",
                before.Session,
                before.ActualStatus);
        }

        string revision = request.Revision.Trim();
        if (!IsSafeToken(revision))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "目标提交、分支或标签无效。",
                before.Session,
                before.ActualStatus);
        }

        GitCommandResult revisionResult = await RunAsync(
            root!,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!revisionResult.IsSuccess)
        {
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(revisionResult),
                "指定提交、分支或标签不存在。",
                before.Session,
                before.ActualStatus);
        }

        IReadOnlyList<string> arguments = request.Kind switch
        {
            GitAdvancedOperationKind.Merge => ["merge", "--no-edit", "--", revision],
            GitAdvancedOperationKind.Rebase => ["rebase", "--", revision],
            GitAdvancedOperationKind.CherryPick => ["cherry-pick", "--", revision],
            GitAdvancedOperationKind.Revert => ["revert", "--no-edit", "--", revision],
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        GitCommandResult command = await RunAsync(
            root!,
            arguments,
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        GitAdvancedOperationResult actual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
        if (!command.IsSuccess)
        {
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(command),
                command.ErrorMessage,
                actual.Session,
                actual.ActualStatus);
        }

        return actual.IsSuccess
            ? actual
            : GitAdvancedOperationResult.Failure(
                actual.FailureKind,
                actual.ErrorMessage ?? "Git 操作完成后无法读取仓库实际状态。",
                actual.Session,
                actual.ActualStatus);
    }

    public async Task<GitAdvancedOperationResult> ExecuteActionAsync(
        GitRepositorySnapshot repository,
        GitOperationAction action,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? root, out string? validationError))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        GitAdvancedOperationResult before = await ReadActualStateAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || before.Session is null)
        {
            return before;
        }

        if (!before.Session.Supports(action))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前 Git 操作不支持所选动作，仓库未被修改。",
                before.Session,
                before.ActualStatus);
        }

        if (before.Session.Kind == GitOperationKind.SmartCheckout)
        {
            return await CompleteSmartCheckoutAsync(repository, before, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<string> arguments = CreateActionArguments(before.Session.Kind, action);
        GitCommandResult command = await RunAsync(
            root!,
            arguments,
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        GitAdvancedOperationResult actual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
        if (!command.IsSuccess)
        {
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(command),
                command.ErrorMessage,
                actual.Session,
                actual.ActualStatus);
        }

        return actual.IsSuccess
            ? actual
            : GitAdvancedOperationResult.Failure(
                actual.FailureKind,
                actual.ErrorMessage ?? "Git 操作完成后无法读取仓库实际状态。",
                actual.Session,
                actual.ActualStatus);
    }

    public async Task<GitAdvancedOperationResult> SmartCheckoutAsync(
        GitRepositorySnapshot repository,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? root, out string? validationError))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError!);
        }

        string targetBranch = branchName.Trim();
        if (!IsSafeToken(targetBranch))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "目标本地分支名称无效。");
        }

        GitAdvancedOperationResult before = await ReadActualStateAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess || before.Session is null)
        {
            return before;
        }

        if (before.Session.IsInProgress || before.Session.HasConflicts)
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前仓库已有进行中的 Git 操作或未解决冲突。",
                before.Session,
                before.ActualStatus);
        }

        if (string.IsNullOrWhiteSpace(before.ActualStatus?.CurrentBranch))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Smart Checkout 需要从本地分支开始，当前处于分离 HEAD 或无提交状态。",
                before.Session,
                before.ActualStatus);
        }

        GitCommandResult branch = await RunAsync(
            root!,
            ["show-ref", "--verify", "--quiet", $"refs/heads/{targetBranch}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!branch.IsSuccess)
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "目标本地分支不存在。",
                before.Session,
                before.ActualStatus);
        }

        if (before.ActualStatus.Files.Count == 0)
        {
            GitCommandResult directSwitch = await RunAsync(
                root!,
                ["switch", targetBranch],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
            GitAdvancedOperationResult directActual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
            return directSwitch.IsSuccess
                ? directActual
                : GitAdvancedOperationResult.Failure(
                    NormalizeFailure(directSwitch),
                    directSwitch.ErrorMessage,
                    directActual.Session,
                    directActual.ActualStatus);
        }

        string message = $"{SmartCheckoutMarker}{Guid.NewGuid():N}";
        GitCommandResult stash = await RunAsync(
            root!,
            ["stash", "push", "--include-untracked", "--message", message],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        if (!stash.IsSuccess)
        {
            GitAdvancedOperationResult actual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(stash),
                stash.ErrorMessage,
                actual.Session,
                actual.ActualStatus);
        }

        string? stashHash = await ResolveRevisionAsync(root!, "refs/stash", CancellationToken.None).ConfigureAwait(false);
        if (stashHash is null)
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "Git 已执行临时 stash，但无法确认对应提交，Smart Checkout 已停止。",
                before.Session,
                before.ActualStatus);
        }

        GitCommandResult switched = await RunAsync(
            root!,
            ["switch", targetBranch],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        if (!switched.IsSuccess)
        {
            GitCommandResult restored = await RunAsync(
                root!,
                ["stash", "apply", "--index", stashHash],
                GitCommandMode.LocalWrite,
                CancellationToken.None).ConfigureAwait(false);
            if (restored.IsSuccess)
            {
                _ = await DropStashAsync(root!, stashHash, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _smartCheckout = new(stashHash);
            }

            GitAdvancedOperationResult actual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(switched),
                switched.ErrorMessage,
                actual.Session,
                actual.ActualStatus);
        }

        GitCommandResult applied = await RunAsync(
            root!,
            ["stash", "apply", "--index", stashHash],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess)
        {
            _smartCheckout = new(stashHash);
            GitAdvancedOperationResult conflicted = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(applied),
                applied.ErrorMessage,
                conflicted.Session,
                conflicted.ActualStatus);
        }

        GitCommandResult dropped = await DropStashAsync(root!, stashHash, CancellationToken.None).ConfigureAwait(false);
        GitAdvancedOperationResult completed = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
        return dropped.IsSuccess
            ? completed
            : GitAdvancedOperationResult.Failure(
                NormalizeFailure(dropped),
                "分支切换和改动恢复已经完成，但临时 stash 未能删除，请在 Stash 管理中确认后手工处理。",
                completed.Session,
                completed.ActualStatus);
    }

    private async Task<GitAdvancedOperationResult> CompleteSmartCheckoutAsync(
        GitRepositorySnapshot repository,
        GitAdvancedOperationResult before,
        CancellationToken cancellationToken)
    {
        if (_smartCheckout is null || repository.RepositoryRoot is null)
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "Smart Checkout 会话已经失效，临时 stash 已保留以避免数据丢失。",
                before.Session,
                before.ActualStatus);
        }

        GitCommandResult dropped = await DropStashAsync(
            repository.RepositoryRoot,
            _smartCheckout.StashHash,
            cancellationToken).ConfigureAwait(false);
        if (dropped.IsSuccess)
        {
            _smartCheckout = null;
        }

        GitAdvancedOperationResult actual = await ReadActualStateWithoutCancellationAsync(repository).ConfigureAwait(false);
        return dropped.IsSuccess
            ? actual
            : GitAdvancedOperationResult.Failure(
                NormalizeFailure(dropped),
                "冲突已解决，但临时 stash 未能删除，请在 Stash 管理中确认后手工处理。",
                actual.Session,
                actual.ActualStatus);
    }

    private async Task<GitAdvancedOperationResult> ReadActualStateAsync(
        GitRepositorySnapshot original,
        CancellationToken cancellationToken)
    {
        if (original.RepositoryRoot is null
            || original.GitDirectory is null
            || !Directory.Exists(original.RepositoryRoot)
            || !Directory.Exists(original.GitDirectory))
        {
            return GitAdvancedOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "仓库工作区或 Git 元数据目录已经不存在。");
        }

        // 冲突索引与工作区状态互不依赖，并行读取可缩短外部 Git 变化的同步延迟。
        Task<GitCommandResult> conflictTask = RunAsync(
            original.RepositoryRoot,
            ["ls-files", "--unmerged", "-z"],
            GitCommandMode.LocalQuery,
            cancellationToken);
        Task<GitStatusResult> statusTask = _statusService.ReadAsync(original, cancellationToken);
        await Task.WhenAll(conflictTask, statusTask).ConfigureAwait(false);
        GitCommandResult conflictResult = await conflictTask.ConfigureAwait(false);
        if (!conflictResult.IsSuccess
            || conflictResult.IsOutputTruncated
            || !GitConflictIndex.TryParse(conflictResult.StandardOutput, out IReadOnlyList<GitConflictIndexEntry>? conflicts))
        {
            return GitAdvancedOperationResult.Failure(
                NormalizeFailure(conflictResult),
                conflictResult.IsOutputTruncated
                    ? "冲突文件列表超过 16 MB，已停止读取。"
                    : conflictResult.ErrorMessage.Length == 0
                        ? "无法解析冲突文件列表。"
                        : conflictResult.ErrorMessage);
        }

        GitStatusResult status = await statusTask.ConfigureAwait(false);
        if (!status.IsSuccess || status.Snapshot is null)
        {
            return GitAdvancedOperationResult.Failure(status.FailureKind, status.ErrorMessage!);
        }

        GitOperationKind kind = GitRepositoryService.DetectOperation(original.GitDirectory);
        if (_smartCheckout is null && kind == GitOperationKind.None)
        {
            _smartCheckout = await FindSmartCheckoutContextAsync(
                original.RepositoryRoot,
                cancellationToken).ConfigureAwait(false);
        }

        if (_smartCheckout is not null && kind == GitOperationKind.None)
        {
            kind = GitOperationKind.SmartCheckout;
        }

        bool hasConflicts = conflicts!.Count > 0;
        bool inProgress = kind != GitOperationKind.None;
        bool hasRequiredStagedChanges = true;
        if (!hasConflicts && kind is GitOperationKind.CherryPick or GitOperationKind.Revert)
        {
            GitCommandResult staged = await _runner.RunWithAdditionalSuccessExitCodesAsync(
                _runtime.ExecutablePath!,
                original.RepositoryRoot,
                ["diff", "--cached", "--quiet", "--ignore-submodules", "--"],
                GitCommandMode.LocalQuery,
                new HashSet<int> { 1 },
                cancellationToken).ConfigureAwait(false);
            if (!staged.IsSuccess)
            {
                return GitAdvancedOperationResult.Failure(
                    NormalizeFailure(staged),
                    staged.ErrorMessage.Length == 0
                        ? "无法确认当前 Git 操作是否仍有可提交内容。"
                        : staged.ErrorMessage);
            }

            hasRequiredStagedChanges = staged.ExitCode == 1;
        }

        bool operationSupportsContinue = kind is
            GitOperationKind.Merge
            or GitOperationKind.Rebase
            or GitOperationKind.CherryPick
            or GitOperationKind.Revert
            or GitOperationKind.SmartCheckout;
        bool canContinue = inProgress
            && !hasConflicts
            && operationSupportsContinue
            && hasRequiredStagedChanges;
        bool canSkip = kind is GitOperationKind.Rebase or GitOperationKind.CherryPick or GitOperationKind.Revert;
        bool canAbort = kind is GitOperationKind.Merge or GitOperationKind.Rebase or GitOperationKind.CherryPick or GitOperationKind.Revert;
        (int? currentStep, int? totalSteps) = await ReadOperationProgressAsync(
            original.GitDirectory,
            kind,
            cancellationToken).ConfigureAwait(false);
        GitOperationSession session = new(
            kind,
            inProgress,
            hasConflicts,
            status.Snapshot.CurrentBranch,
            conflicts.Select(conflict => conflict.ToInfo()).ToArray(),
            canContinue,
            canSkip,
            canAbort,
            currentStep,
            totalSteps);
        return GitAdvancedOperationResult.Success(session, status.Snapshot);
    }

    private static async Task<(int? CurrentStep, int? TotalSteps)> ReadOperationProgressAsync(
        string gitDirectory,
        GitOperationKind kind,
        CancellationToken cancellationToken)
    {
        if (kind != GitOperationKind.Rebase)
        {
            return default;
        }

        string mergeDirectory = Path.Combine(gitDirectory, "rebase-merge");
        if (Directory.Exists(mergeDirectory))
        {
            return await ReadProgressPairAsync(
                Path.Combine(mergeDirectory, "msgnum"),
                Path.Combine(mergeDirectory, "end"),
                cancellationToken).ConfigureAwait(false);
        }

        string applyDirectory = Path.Combine(gitDirectory, "rebase-apply");
        if (Directory.Exists(applyDirectory))
        {
            return await ReadProgressPairAsync(
                Path.Combine(applyDirectory, "next"),
                Path.Combine(applyDirectory, "last"),
                cancellationToken).ConfigureAwait(false);
        }

        return default;
    }

    private static async Task<(int? CurrentStep, int? TotalSteps)> ReadProgressPairAsync(
        string currentPath,
        string totalPath,
        CancellationToken cancellationToken)
    {
        int? current = await ReadPositiveNumberAsync(currentPath, cancellationToken).ConfigureAwait(false);
        int? total = await ReadPositiveNumberAsync(totalPath, cancellationToken).ConfigureAwait(false);
        return current is > 0 && total is > 0 && current <= total
            ? (current, total)
            : default;
    }

    private static async Task<int?> ReadPositiveNumberAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists || file.Length is <= 0 or > 32)
            {
                return null;
            }

            string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                && value > 0
                    ? value
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private Task<GitAdvancedOperationResult> ReadActualStateWithoutCancellationAsync(GitRepositorySnapshot repository)
    {
        return ReadActualStateAsync(repository, CancellationToken.None);
    }

    private async Task<GitCommandResult> DropStashAsync(
        string repositoryRoot,
        string stashHash,
        CancellationToken cancellationToken)
    {
        GitCommandResult list = await RunAsync(
            repositoryRoot,
            ["stash", "list", "--format=%gd%x00%H", "-z"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return list;
        }

        string[] fields = list.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < fields.Length; index += 2)
        {
            if (fields[index + 1].Equals(stashHash, StringComparison.OrdinalIgnoreCase))
            {
                return await RunAsync(
                    repositoryRoot,
                    ["stash", "drop", fields[index]],
                    GitCommandMode.LocalWrite,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new(
            1,
            string.Empty,
            "找不到 Smart Checkout 创建的临时 stash。",
            GitOperationFailureKind.CommandFailed);
    }

    private async Task<SmartCheckoutContext?> FindSmartCheckoutContextAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        GitCommandResult list = await RunAsync(
            repositoryRoot,
            ["stash", "list", "--format=%H%x00%gs", "-z"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return null;
        }

        string[] fields = list.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < fields.Length; index += 2)
        {
            if (fields[index + 1].Contains(SmartCheckoutMarker, StringComparison.Ordinal))
            {
                return new(fields[index]);
            }
        }

        return null;
    }

    private async Task<string?> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", revision],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NullIfEmpty(result.StandardOutput) : null;
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

    private static IReadOnlyList<string> CreateActionArguments(
        GitOperationKind kind,
        GitOperationAction action)
    {
        string operation = kind switch
        {
            GitOperationKind.Merge => "merge",
            GitOperationKind.Rebase => "rebase",
            GitOperationKind.CherryPick => "cherry-pick",
            GitOperationKind.Revert => "revert",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return action switch
        {
            GitOperationAction.Continue => kind == GitOperationKind.Rebase
                ? ["-c", "core.editor=true", "-c", "sequence.editor=true", operation, "--continue"]
                : ["-c", "core.editor=true", operation, "--continue"],
            GitOperationAction.Skip => [operation, "--skip"],
            GitOperationAction.Abort => [operation, "--abort"],
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
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
            error = "高级 Git 操作只适用于具有工作区的 Git 仓库。";
            return false;
        }

        return true;
    }

    private static bool IsSafeToken(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith('-')
            && value.IndexOfAny(['\0', '\r', '\n']) < 0;
    }

    private static string? NullIfEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }

    private sealed record SmartCheckoutContext(string StashHash);
}
