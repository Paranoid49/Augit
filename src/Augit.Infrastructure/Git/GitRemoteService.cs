using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitRemoteService : IGitRemoteService
{
    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitStatusService _statusService;

    public GitRemoteService(GitRuntimeInfo runtime)
        : this(runtime, new GitCommandRunner(), new GitStatusService(runtime))
    {
    }

    internal GitRemoteService(
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

    public async Task<GitRemoteListResult> ReadRemotesAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!IsWorkingTree(repository))
        {
            return GitRemoteListResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "远端管理只适用于具有工作区的 Git 仓库。");
        }

        GitCommandResult result = await RunAsync(
            repository.RepositoryRoot!,
            ["remote", "-v"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return RemoteListFailure(result);
        }

        if (!TryParseRemotes(result.StandardOutput, out IReadOnlyList<GitRemoteInfo>? remotes))
        {
            return GitRemoteListResult.Failure(
                GitOperationFailureKind.CommandFailed,
                "无法解析 Git remote 列表。");
        }

        return GitRemoteListResult.Success(remotes!);
    }

    public async Task<GitRemoteOperationResult> AddRemoteAsync(
        GitRepositorySnapshot repository,
        string name,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, name, fetchUrl);
        if (validationError is not null)
        {
            return GitRemoteOperationResult.Failure(GitOperationFailureKind.InvalidRequest, validationError);
        }

        GitCommandResult addResult = await RunAsync(
            repository.RepositoryRoot!,
            ["remote", "add", name, fetchUrl],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        if (!addResult.IsSuccess)
        {
            return await MutationFailureAsync(repository, addResult).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(pushUrl) && !pushUrl.Equals(fetchUrl, StringComparison.Ordinal))
        {
            GitCommandResult pushUrlResult = await RunAsync(
                repository.RepositoryRoot!,
                ["remote", "set-url", "--push", name, pushUrl],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
            if (!pushUrlResult.IsSuccess)
            {
                return await MutationFailureAsync(repository, pushUrlResult).ConfigureAwait(false);
            }
        }

        return await ReadActualRemoteStateAsync(repository).ConfigureAwait(false);
    }

    public async Task<GitRemoteOperationResult> UpdateRemoteAsync(
        GitRepositorySnapshot repository,
        string currentName,
        string newName,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, currentName, fetchUrl)
            ?? ValidateName(newName, "新的 remote 名称");
        if (validationError is not null)
        {
            return GitRemoteOperationResult.Failure(GitOperationFailureKind.InvalidRequest, validationError);
        }

        string activeName = currentName;
        if (!currentName.Equals(newName, StringComparison.Ordinal))
        {
            GitCommandResult renameResult = await RunAsync(
                repository.RepositoryRoot!,
                ["remote", "rename", currentName, newName],
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
            if (!renameResult.IsSuccess)
            {
                return await MutationFailureAsync(repository, renameResult).ConfigureAwait(false);
            }

            activeName = newName;
        }

        GitCommandResult fetchUrlResult = await RunAsync(
            repository.RepositoryRoot!,
            ["remote", "set-url", activeName, fetchUrl],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        if (!fetchUrlResult.IsSuccess)
        {
            return await MutationFailureAsync(repository, fetchUrlResult).ConfigureAwait(false);
        }

        GitCommandResult pushUrlResult = await RunAsync(
            repository.RepositoryRoot!,
            ["remote", "set-url", "--push", activeName, pushUrl ?? fetchUrl],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        return pushUrlResult.IsSuccess
            ? await ReadActualRemoteStateAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, pushUrlResult).ConfigureAwait(false);
    }

    public async Task<GitRemoteOperationResult> DeleteRemoteAsync(
        GitRepositorySnapshot repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, name);
        if (validationError is not null)
        {
            return GitRemoteOperationResult.Failure(GitOperationFailureKind.InvalidRequest, validationError);
        }

        GitCommandResult result = await RunAsync(
            repository.RepositoryRoot!,
            ["remote", "remove", name],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualRemoteStateAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    public async Task<GitRemoteOperationResult> SetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string remoteName,
        string remoteBranch,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, remoteName)
            ?? ValidateName(localBranch, "本地分支名称")
            ?? ValidateName(remoteBranch, "远端分支名称");
        if (validationError is not null)
        {
            return GitRemoteOperationResult.Failure(GitOperationFailureKind.InvalidRequest, validationError);
        }

        GitCommandResult result = await RunAsync(
            repository.RepositoryRoot!,
            ["branch", $"--set-upstream-to={remoteName}/{remoteBranch}", localBranch],
            GitCommandMode.LocalWrite,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? await ReadActualRemoteStateAsync(repository).ConfigureAwait(false)
            : await MutationFailureAsync(repository, result).ConfigureAwait(false);
    }

    public async Task<GitRemoteOperationResult> FetchAsync(
        GitRepositorySnapshot repository,
        string? remoteName = null,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, remoteName, allowEmptyName: true);
        if (validationError is not null)
        {
            return GitRemoteOperationResult.Failure(GitOperationFailureKind.InvalidRequest, validationError);
        }

        List<string> arguments = ["fetch"];
        if (!string.IsNullOrWhiteSpace(remoteName))
        {
            arguments.Add(remoteName);
        }

        return await RunNetworkOperationAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task<GitRemoteOperationResult> PullAsync(
        GitRepositorySnapshot repository,
        GitPullMode mode = GitPullMode.RepositoryConfigured,
        CancellationToken cancellationToken = default)
    {
        if (!IsWorkingTree(repository))
        {
            return Task.FromResult(GitRemoteOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "pull 只适用于具有工作区的 Git 仓库。"));
        }

        List<string> arguments = ["pull"];
        switch (mode)
        {
            case GitPullMode.RepositoryConfigured:
                break;
            case GitPullMode.Merge:
                arguments.Add("--no-rebase");
                break;
            case GitPullMode.Rebase:
                arguments.Add("--rebase");
                break;
            case GitPullMode.FastForwardOnly:
                arguments.Add("--ff-only");
                break;
            default:
                return Task.FromResult(GitRemoteOperationResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    "pull 模式无效。"));
        }

        return RunNetworkOperationAsync(repository, arguments, cancellationToken);
    }

    public Task<GitRemoteOperationResult> PushAsync(
        GitRepositorySnapshot repository,
        string? remoteName = null,
        string? branchName = null,
        CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateRemoteRequest(repository, remoteName, allowEmptyName: true);
        if (validationError is null && !string.IsNullOrWhiteSpace(branchName))
        {
            validationError = ValidateName(branchName, "分支名称");
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                validationError ??= "指定推送分支时必须同时指定 remote。";
            }
        }

        if (validationError is not null)
        {
            return Task.FromResult(GitRemoteOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                validationError));
        }

        List<string> arguments = ["push"];
        if (!string.IsNullOrWhiteSpace(remoteName))
        {
            arguments.Add(remoteName);
        }

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            arguments.Add(branchName);
        }

        return RunNetworkOperationAsync(repository, arguments, cancellationToken);
    }

    internal static bool TryParseRemotes(string output, out IReadOnlyList<GitRemoteInfo>? remotes)
    {
        remotes = null;
        Dictionary<string, RemoteBuilder> builders = new(StringComparer.Ordinal);
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.TrimEnd('\r');
            int separator = line.IndexOf('\t');
            if (separator <= 0)
            {
                return false;
            }

            string name = line[..separator];
            string remainder = line[(separator + 1)..];
            const string FetchSuffix = " (fetch)";
            const string PushSuffix = " (push)";
            bool isFetch = remainder.EndsWith(FetchSuffix, StringComparison.Ordinal);
            bool isPush = remainder.EndsWith(PushSuffix, StringComparison.Ordinal);
            if (!isFetch && !isPush)
            {
                return false;
            }

            string url = remainder[..^(isFetch ? FetchSuffix.Length : PushSuffix.Length)];
            if (!builders.TryGetValue(name, out RemoteBuilder? builder))
            {
                builder = new();
                builders.Add(name, builder);
            }

            if (isFetch)
            {
                builder.FetchUrl = url;
            }
            else
            {
                builder.PushUrl = url;
            }
        }

        if (builders.Values.Any(builder => builder.FetchUrl is null || builder.PushUrl is null))
        {
            return false;
        }

        remotes = builders
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new GitRemoteInfo(pair.Key, pair.Value.FetchUrl!, pair.Value.PushUrl!))
            .ToArray();
        return true;
    }

    private async Task<GitRemoteOperationResult> RunNetworkOperationAsync(
        GitRepositorySnapshot repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!IsWorkingTree(repository))
        {
            return GitRemoteOperationResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "远程操作只适用于具有工作区的 Git 仓库。");
        }

        GitCommandResult result = await RunAsync(
            repository.RepositoryRoot!,
            arguments,
            GitCommandMode.Network,
            cancellationToken).ConfigureAwait(false);
        GitStatusResult actualStatus = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return result.IsSuccess
            ? GitRemoteOperationResult.Success(null, actualStatus.IsSuccess ? actualStatus.Snapshot : null)
            : GitRemoteOperationResult.Failure(
                result.FailureKind == GitOperationFailureKind.None
                    ? GitOperationFailureKind.CommandFailed
                    : result.FailureKind,
                result.ErrorMessage,
                actualStatus: actualStatus.IsSuccess ? actualStatus.Snapshot : null);
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

    private async Task<GitRemoteOperationResult> ReadActualRemoteStateAsync(GitRepositorySnapshot repository)
    {
        GitRemoteListResult remotes = await ReadRemotesAsync(repository, CancellationToken.None).ConfigureAwait(false);
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return remotes.IsSuccess
            ? GitRemoteOperationResult.Success(remotes.Remotes, status.IsSuccess ? status.Snapshot : null)
            : GitRemoteOperationResult.Failure(
                remotes.FailureKind,
                remotes.ErrorMessage!,
                actualStatus: status.IsSuccess ? status.Snapshot : null);
    }

    private async Task<GitRemoteOperationResult> MutationFailureAsync(
        GitRepositorySnapshot repository,
        GitCommandResult result)
    {
        GitRemoteListResult remotes = await ReadRemotesAsync(repository, CancellationToken.None).ConfigureAwait(false);
        GitStatusResult status = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitRemoteOperationResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage,
            remotes.IsSuccess ? remotes.Remotes : null,
            status.IsSuccess ? status.Snapshot : null);
    }

    private static string? ValidateRemoteRequest(
        GitRepositorySnapshot? repository,
        string? name,
        string? url = null,
        bool allowEmptyName = false)
    {
        if (!IsWorkingTree(repository))
        {
            return "远端操作只适用于具有工作区的 Git 仓库。";
        }

        if (!allowEmptyName || !string.IsNullOrWhiteSpace(name))
        {
            string? nameError = ValidateName(name, "remote 名称");
            if (nameError is not null)
            {
                return nameError;
            }
        }

        if (url is not null && (string.IsNullOrWhiteSpace(url) || url.StartsWith('-')))
        {
            return "remote 地址无效。";
        }

        return null;
    }

    private static string? ValidateName(string? value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith('-')
            || value.Any(char.IsControl)
            || value.Any(char.IsWhiteSpace))
        {
            return $"{displayName}无效。";
        }

        return null;
    }

    private static bool IsWorkingTree(GitRepositorySnapshot? repository)
    {
        return repository is { Kind: GitRepositoryKind.WorkingTree, RepositoryRoot: not null };
    }

    private static GitRemoteListResult RemoteListFailure(GitCommandResult result)
    {
        return GitRemoteListResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage);
    }

    private sealed class RemoteBuilder
    {
        public string? FetchUrl { get; set; }

        public string? PushUrl { get; set; }
    }
}
