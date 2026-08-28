namespace Augit.Core.Git;

public interface IGitRuntimeLocator
{
    Task<GitRuntimeInfo> ResolveAsync(
        string? configuredExecutablePath,
        CancellationToken cancellationToken = default);
}

public interface IGitRepositoryService
{
    GitRuntimeInfo Runtime { get; }

    Task<GitRepositoryOperationResult> InspectAsync(
        string workspacePath,
        CancellationToken cancellationToken = default);

    Task<GitRepositoryOperationResult> InitializeAsync(
        string workspacePath,
        CancellationToken cancellationToken = default);

    Task<GitRepositoryOperationResult> CloneAsync(
        string source,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public interface IGitStatusService
{
    Task<GitStatusResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);
}

public interface IGitDiffService
{
    Task<GitDiffResult> CreateAsync(
        GitRepositorySnapshot repository,
        GitChangedFile changedFile,
        GitDiffOptions options,
        CancellationToken cancellationToken = default);
}

public interface IGitCommitService
{
    Task<GitCommitPolicyResult> ReadPolicyAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitCommitResult> CommitAsync(
        GitRepositorySnapshot repository,
        GitCommitRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGitRemoteService
{
    Task<GitRemoteListResult> ReadRemotesAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> AddRemoteAsync(
        GitRepositorySnapshot repository,
        string name,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> UpdateRemoteAsync(
        GitRepositorySnapshot repository,
        string currentName,
        string newName,
        string fetchUrl,
        string? pushUrl = null,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> DeleteRemoteAsync(
        GitRepositorySnapshot repository,
        string name,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> SetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string remoteName,
        string remoteBranch,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> FetchAsync(
        GitRepositorySnapshot repository,
        string? remoteName = null,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PullAsync(
        GitRepositorySnapshot repository,
        GitPullMode mode = GitPullMode.RepositoryConfigured,
        CancellationToken cancellationToken = default);

    Task<GitRemoteOperationResult> PushAsync(
        GitRepositorySnapshot repository,
        string? remoteName = null,
        string? branchName = null,
        CancellationToken cancellationToken = default);
}

public interface IGitHistoryService
{
    Task<GitHistoryResult> ReadPageAsync(
        GitRepositorySnapshot repository,
        GitHistoryRequest request,
        CancellationToken cancellationToken = default);

    Task<GitCommitDetailsResult> ReadCommitAsync(
        GitRepositorySnapshot repository,
        string revision,
        CancellationToken cancellationToken = default);

    Task<GitComparisonResult> ReadCommitFileDiffAsync(
        GitRepositorySnapshot repository,
        string revision,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<GitBlameResult> ReadBlameAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        string? revision = null,
        CancellationToken cancellationToken = default);

    Task<GitComparisonResult> CompareAsync(
        GitRepositorySnapshot repository,
        GitComparisonRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGitReferenceService
{
    Task<GitReferenceResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> CreateBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        string? startPoint = null,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> CreateTrackingBranchAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string remoteBranch,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> SwitchBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> RenameBranchAsync(
        GitRepositorySnapshot repository,
        string currentName,
        string newName,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> DeleteBranchAsync(
        GitRepositorySnapshot repository,
        string branchName,
        bool force,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> SetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        string upstream,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> UnsetTrackingAsync(
        GitRepositorySnapshot repository,
        string localBranch,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> CreateTagAsync(
        GitRepositorySnapshot repository,
        string tagName,
        string? targetRevision,
        string? message,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> DeleteLocalTagAsync(
        GitRepositorySnapshot repository,
        string tagName,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> PushTagAsync(
        GitRepositorySnapshot repository,
        string remoteName,
        string tagName,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> DeleteRemoteTagAsync(
        GitRepositorySnapshot repository,
        string remoteName,
        string tagName,
        CancellationToken cancellationToken = default);
}

public interface IGitWorkspaceStateService
{
    Task<GitStashListResult> ReadStashesAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitStashContentResult> ReadStashContentAsync(
        GitRepositorySnapshot repository,
        string stashReference,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> StashAsync(
        GitRepositorySnapshot repository,
        string? message,
        bool includeUntracked,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> UnstashAsync(
        GitRepositorySnapshot repository,
        string stashReference,
        bool keepStash,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> ResetAsync(
        GitRepositorySnapshot repository,
        string targetRevision,
        GitResetMode mode,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> RollbackAsync(
        GitRepositorySnapshot repository,
        GitChangedFile changedFile,
        CancellationToken cancellationToken = default);
}

public interface IGitWorktreeService
{
    Task<GitWorktreeListResult> ReadAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> CreateAsync(
        GitRepositorySnapshot repository,
        string destinationPath,
        string sourceBranch,
        string? newBranch = null,
        CancellationToken cancellationToken = default);

    Task<GitActionResult> RemoveAsync(
        GitRepositorySnapshot repository,
        string worktreePath,
        CancellationToken cancellationToken = default);
}

public interface IGitOperationService
{
    Task<GitAdvancedOperationResult> InspectAsync(
        GitRepositorySnapshot repository,
        CancellationToken cancellationToken = default);

    Task<GitAdvancedOperationResult> StartAsync(
        GitRepositorySnapshot repository,
        GitOperationRequest request,
        CancellationToken cancellationToken = default);

    Task<GitAdvancedOperationResult> ExecuteActionAsync(
        GitRepositorySnapshot repository,
        GitOperationAction action,
        CancellationToken cancellationToken = default);

    Task<GitAdvancedOperationResult> SmartCheckoutAsync(
        GitRepositorySnapshot repository,
        string branchName,
        CancellationToken cancellationToken = default);
}

public interface IGitConflictService
{
    Task<GitConflictLoadResult> LoadAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<GitConflictMutationResult> SaveResolvedAsync(
        GitRepositorySnapshot repository,
        GitConflictSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<GitConflictMutationResult> AcceptSideAsync(
        GitRepositorySnapshot repository,
        string relativePath,
        GitConflictSide side,
        CancellationToken cancellationToken = default);
}
