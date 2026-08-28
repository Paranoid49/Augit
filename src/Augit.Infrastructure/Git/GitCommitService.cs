using System.Text.Json;
using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitCommitService : IGitCommitService
{
    private const long MaximumPackageJsonBytes = 1024 * 1024;
    private static readonly string[] CommitlintConfigurationNames =
    [
        "commitlint.config.js",
        "commitlint.config.cjs",
        "commitlint.config.mjs",
        "commitlint.config.ts",
        ".commitlintrc",
        ".commitlintrc.json",
        ".commitlintrc.yaml",
        ".commitlintrc.yml",
        ".commitlintrc.js",
        ".commitlintrc.cjs",
    ];

    private readonly GitRuntimeInfo _runtime;
    private readonly GitCommandRunner _runner;
    private readonly GitStatusService _statusService;

    public GitCommitService(GitRuntimeInfo runtime)
        : this(runtime, new GitCommandRunner(), new GitStatusService(runtime))
    {
    }

    internal GitCommitService(
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

    public async Task<GitCommitPolicyResult> ReadPolicyAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        if (!IsWorkingTree(repository))
        {
            return GitCommitPolicyResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "提交只适用于具有工作区的 Git 仓库。");
        }

        GitCommandResult hookPathResult = await _runner.RunAsync(
            _runtime.ExecutablePath!,
            repository.RepositoryRoot!,
            ["rev-parse", "--path-format=absolute", "--git-path", "hooks/commit-msg"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!hookPathResult.IsSuccess)
        {
            return PolicyFailure(hookPathResult);
        }

        string hookPath = hookPathResult.StandardOutput.Trim();
        bool hasHook = hookPath.Length > 0 && File.Exists(hookPath);
        bool hasCommitlint = HasCommitlintConfiguration(repository.RepositoryRoot!);
        return GitCommitPolicyResult.Success(new(hasHook, hasCommitlint));
    }

    public async Task<GitCommitResult> CommitAsync(
        GitRepositorySnapshot repository,
        GitCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsWorkingTree(repository))
        {
            return GitCommitResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "提交只适用于具有工作区的 Git 仓库。");
        }

        if (request.SelectedRelativePaths is null)
        {
            return GitCommitResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "提交文件选择无效。");
        }

        string[] selectedPaths = request.SelectedRelativePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedPaths.Length == 0 && !request.Amend)
        {
            return GitCommitResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "请至少选择一个要提交的文件。");
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return GitCommitResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "提交信息不能为空。");
        }

        GitStatusResult before = await _statusService.ReadAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!before.IsSuccess)
        {
            return StatusFailure(before);
        }

        Dictionary<string, GitChangedFile> currentFiles = before.Snapshot!.Files
            .ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        List<GitChangedFile> selectedFiles = [];
        foreach (string selectedPath in selectedPaths)
        {
            if (!currentFiles.TryGetValue(selectedPath.Replace('\\', '/'), out GitChangedFile? file))
            {
                return GitCommitResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    $"文件 {selectedPath} 已不在 Changes 中，请刷新后重新选择。",
                    before.Snapshot);
            }

            selectedFiles.Add(file);
        }

        if (request.Amend && !await HasHeadAsync(repository.RepositoryRoot!, cancellationToken).ConfigureAwait(false))
        {
            return GitCommitResult.Failure(
                GitOperationFailureKind.InvalidRequest,
                "当前仓库还没有可 Amend 的提交。",
                before.Snapshot);
        }

        GitCommitPolicyResult policy = await ReadPolicyAsync(repository, cancellationToken).ConfigureAwait(false);
        if (!policy.IsSuccess)
        {
            return GitCommitResult.Failure(policy.FailureKind, policy.ErrorMessage!, before.Snapshot);
        }

        if (!policy.Policy!.UsesRepositoryRules && request.MessageSource == GitCommitMessageSource.UserInput)
        {
            GitCommitValidationResult validation = ConventionalCommitValidator.Validate(request.Message);
            if (!validation.IsValid)
            {
                return GitCommitResult.Failure(
                    GitOperationFailureKind.InvalidRequest,
                    validation.ErrorMessage!,
                    before.Snapshot);
            }
        }

        string[] untrackedPaths = selectedFiles
            .Where(file => file.Group == GitChangeGroup.UnversionedFiles)
            .Select(file => file.RelativePath)
            .ToArray();
        if (untrackedPaths.Length > 0)
        {
            List<string> addArguments = ["add", "--"];
            addArguments.AddRange(untrackedPaths);
            GitCommandResult addResult = await _runner.RunAsync(
                _runtime.ExecutablePath!,
                repository.RepositoryRoot!,
                addArguments,
                GitCommandMode.LocalWrite,
                cancellationToken).ConfigureAwait(false);
            if (!addResult.IsSuccess)
            {
                return await CommandFailureWithActualStatusAsync(repository, addResult).ConfigureAwait(false);
            }
        }

        List<string> commitArguments = ["commit", "--only", "--cleanup=strip", "--file=-"];
        if (request.Amend)
        {
            commitArguments.Add("--amend");
        }

        if (selectedFiles.Count > 0)
        {
            commitArguments.Add("--");
            foreach (GitChangedFile file in selectedFiles)
            {
                if (!string.IsNullOrWhiteSpace(file.OriginalRelativePath))
                {
                    commitArguments.Add(file.OriginalRelativePath);
                }

                commitArguments.Add(file.RelativePath);
            }
        }

        GitCommandResult commitResult = await _runner.RunWithStandardInputAsync(
            _runtime.ExecutablePath!,
            repository.RepositoryRoot!,
            commitArguments,
            GitCommandMode.LocalWrite,
            request.Message,
            cancellationToken).ConfigureAwait(false);
        if (!commitResult.IsSuccess)
        {
            return await CommandFailureWithActualStatusAsync(repository, commitResult).ConfigureAwait(false);
        }

        GitCommandResult hashResult = await _runner.RunAsync(
            _runtime.ExecutablePath!,
            repository.RepositoryRoot!,
            ["rev-parse", "--verify", "HEAD"],
            GitCommandMode.LocalQuery,
            CancellationToken.None).ConfigureAwait(false);
        if (!hashResult.IsSuccess)
        {
            return await CommandFailureWithActualStatusAsync(repository, hashResult).ConfigureAwait(false);
        }

        GitStatusResult actual = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return actual.IsSuccess
            ? GitCommitResult.Success(hashResult.StandardOutput.Trim(), actual.Snapshot!)
            : GitCommitResult.Failure(
                actual.FailureKind,
                $"提交已完成，但无法刷新仓库状态：{actual.ErrorMessage}");
    }

    private async Task<bool> HasHeadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        GitCommandResult result = await _runner.RunAsync(
            _runtime.ExecutablePath!,
            repositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private async Task<GitCommitResult> CommandFailureWithActualStatusAsync(
        GitRepositorySnapshot repository,
        GitCommandResult commandResult)
    {
        GitStatusResult actual = await _statusService.ReadAsync(repository, CancellationToken.None).ConfigureAwait(false);
        return GitCommitResult.Failure(
            commandResult.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : commandResult.FailureKind,
            commandResult.ErrorMessage,
            actual.IsSuccess ? actual.Snapshot : null);
    }

    private static bool HasCommitlintConfiguration(string repositoryRoot)
    {
        if (CommitlintConfigurationNames.Any(name => File.Exists(Path.Combine(repositoryRoot, name))))
        {
            return true;
        }

        string packagePath = Path.Combine(repositoryRoot, "package.json");
        try
        {
            FileInfo packageFile = new(packagePath);
            if (!packageFile.Exists || packageFile.Length > MaximumPackageJsonBytes)
            {
                return false;
            }

            using FileStream stream = packageFile.OpenRead();
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("commitlint", out _);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool IsWorkingTree(GitRepositorySnapshot? repository)
    {
        return repository is { Kind: GitRepositoryKind.WorkingTree, RepositoryRoot: not null };
    }

    private static GitCommitPolicyResult PolicyFailure(GitCommandResult result)
    {
        return GitCommitPolicyResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage);
    }

    private static GitCommitResult StatusFailure(GitStatusResult result)
    {
        return GitCommitResult.Failure(
            result.FailureKind == GitOperationFailureKind.None
                ? GitOperationFailureKind.CommandFailed
                : result.FailureKind,
            result.ErrorMessage!);
    }
}
