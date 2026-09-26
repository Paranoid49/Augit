using System.Globalization;
using System.Text.RegularExpressions;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitReferenceService : IGitReferenceService
{
    private const char FieldSeparator = '\x1f';
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitStatusService _statusService;

    public GitRuntimeInfo Runtime => _runtime;

    public GitReferenceService(GitRuntimeInfo runtime)
        : this(
            runtime,
            new GitCommandRunner(TimeSpan.FromSeconds(30), 8 * 1024 * 1024),
            new GitStatusService(runtime))
    {
    }

    internal GitReferenceService(
        GitRuntimeInfo runtime,
        GitCommandRunner runner,
        GitStatusService statusService)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            throw new ArgumentException("Git 运行环境不可用。", nameof(runtime));
        }

        _runtime = runtime;
        _runner = runner;
        _statusService = statusService;
    }

    public async Task<GitReferenceResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitReferenceResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult branchResult = await RunAsync(
            repositoryRoot!,
            [
                "for-each-ref",
                $"--format=%(refname){FieldSeparator}%(objectname){FieldSeparator}%(HEAD){FieldSeparator}%(upstream:short){FieldSeparator}%(subject){FieldSeparator}%(symref)",
                "refs/heads",
                "refs/remotes",
            ],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!branchResult.IsSuccess)
        {
            return ReferenceFailure(branchResult);
        }

        GitCommandResult tagResult = await RunAsync(
            repositoryRoot!,
            [
                "for-each-ref",
                $"--format=%(refname:short){FieldSeparator}%(objectname){FieldSeparator}%(*objectname){FieldSeparator}%(objecttype){FieldSeparator}%(contents:subject)",
                "refs/tags",
            ],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!tagResult.IsSuccess)
        {
            return ReferenceFailure(tagResult);
        }

        if (branchResult.IsOutputTruncated || tagResult.IsOutputTruncated)
        {
            return GitReferenceResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "分支或标签列表超过 8 MB，已停止读取。");
        }

        if (!TryParseBranches(branchResult.StandardOutput, out IReadOnlyList<GitBranchInfo>? branches)
            || !TryParseTags(tagResult.StandardOutput, out IReadOnlyList<GitTagInfo>? tags))
        {
            return GitReferenceResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析分支或标签列表。");
        }

        return GitReferenceResult.Success(new(branches!, tags!));
    }

    /// <summary>
    /// 读取"我的分支"（权威 `BranchesDashboardUtil.checkIsMyBranchesSynchronously`
    /// ＋ `isMyBranch`／`findExclusiveCommits`，`BranchesDashboardUtil.kt:85-160`）：
    /// 分支的**独占提交**非空、且**全部**由当前 Git 用户提交才算"我的"。
    ///
    /// 独占提交的语义照权威 `VcsLogGraphData.exclusiveCommits`
    /// （`platform/vcs-log/impl/src/com/intellij/vcs/log/util/DataPackUtil.kt:54-64` ＋
    /// `graph/utils/GraphUtil.kt:142-158` 的 `exclusiveNodes` 注释
    /// "nodes reachable only from the specified head node and not from others"）：
    /// **只从该分支头可达、不从其它分支头可达**的提交；标签不算分支头（`isBranchHead` 只认 `type.isBranch`）。
    /// 对应 `git rev-list --count <tip> --not <其它分支头的哈希>`。
    /// 权威对 headNode 自身永远算独占（`it == headNode || !isHead(it)`），因此这里把
    /// **与自身 tip 相同**的其它分支头从 `--not` 里排除，使"两个分支指向同一提交"时两者都算有独占提交。
    ///
    /// 成本：每个分支两次 `rev-list --count`（总数／我的数）。只在用户打开「我的分支」时调用一次。
    /// </summary>
    public async Task<GitMyBranchesResult> ReadMyBranchesAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitMyBranchesResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        // 当前用户：权威 `VcsLogFilterObject.ME` 由日志的用户解析器解析成当前 Git 用户
        // （`VcsLogFilterObject.fromUserNames(listOf(ME))`）。这里取仓库配置的 `user.email`，
        // 缺失时退回 `user.name`。
        string? email = await ReadConfigAsync(repositoryRoot!, "user.email", cancellationToken).ConfigureAwait(false);
        string? name = await ReadConfigAsync(repositoryRoot!, "user.name", cancellationToken).ConfigureAwait(false);
        string? me = string.IsNullOrWhiteSpace(email) ? NullIfEmpty(name ?? string.Empty) : email;
        if (string.IsNullOrWhiteSpace(me))
        {
            return GitMyBranchesResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "没有配置 Git 用户（user.email／user.name），无法判断哪些分支属于你。");
        }

        GitReferenceResult references = await ReadAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!references.IsSuccess || references.Snapshot is not { } snapshot)
        {
            return GitMyBranchesResult.Failure(
                references.FailureKind == GitOperationFailureKind.None
                    ? GitOperationFailureKind.CommandFailed
                    : references.FailureKind,
                references.ErrorMessage ?? "无法读取分支列表。");
        }

        string authorFilter = $"--author={Regex.Escape(me)}";
        List<string> mine = [];
        foreach (GitBranchInfo branch in snapshot.Branches)
        {
            List<string> arguments = ["rev-list", "--count", branch.CommitHash];
            List<string> others = snapshot.Branches
                .Where(other => !string.Equals(other.Name, branch.Name, StringComparison.Ordinal))
                .Where(other => !string.Equals(other.CommitHash, branch.CommitHash, StringComparison.Ordinal))
                .Select(other => other.CommitHash)
                .ToList();
            if (others.Count > 0)
            {
                arguments.Add("--not");
                arguments.AddRange(others);
            }

            long? total = await CountRevisionsAsync(repositoryRoot!, arguments, cancellationToken).ConfigureAwait(false);
            if (total is null || total <= 0)
            {
                // 没有独占提交 ⇒ 权威 `isMyBranch` 直接判否（`exclusiveCommits.isEmpty() -> false`）。
                continue;
            }

            List<string> mineArguments = [.. arguments, authorFilter];
            long? byMe = await CountRevisionsAsync(repositoryRoot!, mineArguments, cancellationToken).ConfigureAwait(false);
            if (byMe == total)
            {
                mine.Add(branch.Name);
            }
        }

        return GitMyBranchesResult.Success(me, mine);
    }

    private async Task<string?> ReadConfigAsync(
        string repositoryRoot,
        string key,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunAsync(
            repositoryRoot,
            ["config", "--get", key],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NullIfEmpty(result.StandardOutput.TrimEnd('\r', '\n')) : null;
    }

    private async Task<long?> CountRevisionsAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await RunAsync(
            repositoryRoot,
            arguments,
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            && long.TryParse(result.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count)
                ? count
                : null;
    }

    public async Task<GitActionResult> CreateBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        string? startPoint = null,
        CancellationToken cancellationToken = default)
    {
        string? error = await ValidateBranchRequestAsync(
            repository,
            branchName,
            cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        List<string> arguments = ["branch", branchName];
        if (!string.IsNullOrWhiteSpace(startPoint))
        {
            string? resolved = await ResolveRevisionAsync(
                repository.RepositoryRoot!,
                startPoint,
                cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return GitActionResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "创建分支的起点不存在或不是唯一提交。");
            }

            arguments.Add(resolved);
        }

        return await RunMutationAsync(repository, arguments, GitCommandMode.LocalWrite, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GitActionResult> CreateTrackingBranchAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string remoteBranch,
        CancellationToken cancellationToken = default)
    {
        string? error = await ValidateBranchRequestAsync(
            repository,
            localBranch,
            cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            error = await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                remoteBranch,
                cancellationToken).ConfigureAwait(false);
        }

        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["switch", "--track", "-c", localBranch, remoteBranch],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> SwitchBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        string? error = TryGetRepositoryRoot(repository, out _, out string? repositoryError)
            ? await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                branchName,
                cancellationToken).ConfigureAwait(false)
            : repositoryError;
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["switch", branchName],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> CheckoutReferenceAsync(
        GitRepositorySnapshot repository,
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        string? resolved = await ResolveRevisionAsync(
            repositoryRoot!,
            reference,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return GitActionResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "指定标签或版本不存在，或不是唯一提交。");
        }

        return await RunMutationAsync(
            repository,
            ["switch", "--detach", resolved],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> RenameBranchAsync(
        GitRepositorySnapshot repository,
        string currentName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        string? error = await ValidateBranchRequestAsync(
            repository,
            newName,
            cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            error = await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                currentName,
                cancellationToken).ConfigureAwait(false);
        }

        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["branch", "-m", currentName, newName],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> DeleteBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        bool force,
        CancellationToken cancellationToken = default)
    {
        string? error = TryGetRepositoryRoot(repository, out _, out string? repositoryError)
            ? await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                branchName,
                cancellationToken).ConfigureAwait(false)
            : repositoryError;
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["branch", force ? "-D" : "-d", branchName],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> SetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string upstream,
        CancellationToken cancellationToken = default)
    {
        string? error = TryGetRepositoryRoot(repository, out _, out string? repositoryError)
            ? await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                localBranch,
                cancellationToken).ConfigureAwait(false)
            : repositoryError;
        if (error is null)
        {
            error = await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                upstream,
                cancellationToken).ConfigureAwait(false);
        }

        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["branch", $"--set-upstream-to={upstream}", localBranch],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> UnsetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        CancellationToken cancellationToken = default)
    {
        string? error = TryGetRepositoryRoot(repository, out _, out string? repositoryError)
            ? await ValidateExistingReferenceAsync(
                repository.RepositoryRoot!,
                localBranch,
                cancellationToken).ConfigureAwait(false)
            : repositoryError;
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["branch", "--unset-upstream", localBranch],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitActionResult> CreateTagAsync(
        GitRepositorySnapshot repository,
        string tagName,
        string? targetRevision,
        string? message,
        CancellationToken cancellationToken = default)
    {
        string? error = await ValidateTagRequestAsync(repository, tagName, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        string? target = null;
        if (!string.IsNullOrWhiteSpace(targetRevision))
        {
            target = await ResolveRevisionAsync(
                repository.RepositoryRoot!,
                targetRevision,
                cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return GitActionResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "标签目标不存在或不是唯一提交。");
            }
        }

        List<string> arguments = ["tag"];
        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("-a");
            arguments.Add(tagName);
            if (target is not null)
            {
                arguments.Add(target);
            }
            arguments.Add("-m");
            arguments.Add(message);
        }
        else
        {
            arguments.Add(tagName);
            if (target is not null)
            {
                arguments.Add(target);
            }
        }

        return await RunMutationAsync(repository, arguments, GitCommandMode.LocalWrite, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GitActionResult> DeleteLocalTagAsync(
        GitRepositorySnapshot repository,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        string? error = await ValidateExistingTagAsync(repository, tagName, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        return await RunMutationAsync(
            repository,
            ["tag", "-d", tagName],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<GitActionResult> PushTagAsync(
        GitRepositorySnapshot repository,
        string remoteName,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        return RunTagNetworkOperationAsync(
            repository,
            remoteName,
            tagName,
            delete: false,
            cancellationToken);
    }

    public Task<GitActionResult> DeleteRemoteTagAsync(
        GitRepositorySnapshot repository,
        string remoteName,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        return RunTagNetworkOperationAsync(
            repository,
            remoteName,
            tagName,
            delete: true,
            cancellationToken);
    }

    internal static bool TryParseBranches(string output, out IReadOnlyList<GitBranchInfo>? branches)
    {
        branches = null;
        List<GitBranchInfo> parsed = [];
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length != 6)
            {
                return false;
            }

            if (fields[5].Length > 0)
            {
                continue;
            }

            bool isRemote = fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal);
            string name = isRemote ? fields[0][13..] : fields[0].StartsWith("refs/heads/", StringComparison.Ordinal)
                ? fields[0][11..]
                : fields[0];
            parsed.Add(new(
                name,
                fields[0],
                isRemote,
                fields[2] == "*",
                NullIfEmpty(fields[3]),
                fields[1],
                fields[4]));
        }

        branches = parsed
            .OrderBy(branch => branch.IsRemote)
            .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return true;
    }

    internal static bool TryParseTags(string output, out IReadOnlyList<GitTagInfo>? tags)
    {
        tags = null;
        List<GitTagInfo> parsed = [];
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length != 5)
            {
                return false;
            }

            bool annotated = fields[3].Equals("tag", StringComparison.Ordinal);
            parsed.Add(new(
                fields[0],
                annotated && fields[2].Length > 0 ? fields[2] : fields[1],
                annotated,
                NullIfEmpty(fields[4])));
        }

        tags = parsed.OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private async Task<GitActionResult> RunTagNetworkOperationAsync(
        GitRepositorySnapshot repository,
        string remoteName,
        string tagName,
        bool delete,
        CancellationToken cancellationToken)
    {
        string? error = await ValidateExistingTagAsync(repository, tagName, cancellationToken).ConfigureAwait(false);
        if (error is null && !IsSafeToken(remoteName))
        {
            error = "Remote 名称无效。";
        }

        if (error is not null)
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error);
        }

        string refspec = delete
            ? $":refs/tags/{tagName}"
            : $"refs/tags/{tagName}:refs/tags/{tagName}";
        return await RunMutationAsync(
            repository,
            ["push", remoteName, refspec],
            GitCommandMode.Network,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ValidateBranchRequestAsync(
        GitRepositorySnapshot repository,
        string branchName,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return error;
        }

        if (!IsSafeToken(branchName))
        {
            return "分支名称无效。";
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            ["check-ref-format", "--branch", branchName],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? null : "分支名称不符合 Git 规则。";
    }

    private async Task<string?> ValidateTagRequestAsync(
        GitRepositorySnapshot repository,
        string tagName,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return error;
        }

        if (!IsSafeToken(tagName))
        {
            return "标签名称无效。";
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            ["check-ref-format", $"refs/tags/{tagName}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? null : "标签名称不符合 Git 规则。";
    }

    private async Task<string?> ValidateExistingTagAsync(
        GitRepositorySnapshot repository,
        string tagName,
        CancellationToken cancellationToken)
    {
        string? error = await ValidateTagRequestAsync(repository, tagName, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        GitCommandResult result = await RunAsync(
            repository.RepositoryRoot!,
            ["show-ref", "--verify", "--quiet", $"refs/tags/{tagName}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? null : "指定本地标签不存在。";
    }

    private async Task<string?> ValidateExistingReferenceAsync(
        string repositoryRoot,
        string reference,
        CancellationToken cancellationToken)
    {
        return await ResolveRevisionAsync(repositoryRoot, reference, cancellationToken).ConfigureAwait(false) is null
            ? "指定分支或引用不存在。"
            : null;
    }

    private async Task<string?> ResolveRevisionAsync(
        string repositoryRoot,
        string revision,
        CancellationToken cancellationToken)
    {
        if (!IsSafeToken(revision))
        {
            return null;
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NullIfEmpty(result.StandardOutput) : null;
    }

    private async Task<GitActionResult> RunMutationAsync(
        GitRepositorySnapshot repository,
        IReadOnlyList<string> arguments,
        GitCommandMode mode,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepositoryRoot(repository, out string? repositoryRoot, out string? error))
        {
            return GitActionResult.Failure(GitOperationFailureKind.InvalidRequest, error!);
        }

        GitCommandResult result = await RunAsync(
            repositoryRoot!,
            arguments,
            mode,
            cancellationToken).ConfigureAwait(false);
        GitStatusResult actualStatus = await _statusService.ReadAsync(repository, CancellationToken.None)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? GitActionResult.Success(actualStatus.IsSuccess ? actualStatus.Snapshot : null)
            : GitActionResult.Failure(
                NormalizeFailure(result),
                result.ErrorMessage,
                actualStatus.IsSuccess ? actualStatus.Snapshot : null);
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
            error = "分支与标签管理只适用于具有工作区的 Git 仓库。";
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

    private static GitReferenceResult ReferenceFailure(GitCommandResult result)
    {
        return GitReferenceResult.Failure(NormalizeFailure(result), result.ErrorMessage);
    }

    private static GitOperationFailureKind NormalizeFailure(GitCommandResult result)
    {
        return result.FailureKind == GitOperationFailureKind.None
            ? GitOperationFailureKind.CommandFailed
            : result.FailureKind;
    }
}
