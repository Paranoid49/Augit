namespace Augit.Core.Git;

public enum GitResetMode
{
    Soft,
    Mixed,
    Hard,
}

public sealed record GitStashInfo(
    string Reference,
    string CommitHash,
    string ParentHash,
    DateTimeOffset Date,
    string Subject,
    string Branch,
    string Message);

public sealed record GitStashListResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitStashInfo>? Stashes)
{
    public static GitStashListResult Success(IReadOnlyList<GitStashInfo> stashes)
    {
        ArgumentNullException.ThrowIfNull(stashes);
        return new(true, GitOperationFailureKind.None, null, stashes);
    }

    public static GitStashListResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitStashContentResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitComparisonDocument? Document)
{
    public static GitStashContentResult Success(GitComparisonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(true, GitOperationFailureKind.None, null, document);
    }

    public static GitStashContentResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitWorktreeInfo(
    string Path,
    string? CommitHash,
    string? Branch,
    bool IsBare,
    bool IsDetached,
    bool IsLocked,
    string? LockReason,
    bool IsPrunable,
    string? PruneReason,
    bool IsCurrent);

public sealed record GitWorktreeListResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitWorktreeInfo>? Worktrees)
{
    public static GitWorktreeListResult Success(IReadOnlyList<GitWorktreeInfo> worktrees)
    {
        ArgumentNullException.ThrowIfNull(worktrees);
        return new(true, GitOperationFailureKind.None, null, worktrees);
    }

    public static GitWorktreeListResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitWorktreeRemovalReadiness(
    bool CanRemove,
    bool IsClean,
    bool HasActiveTerminal,
    string? Reason);

public sealed record GitWorktreeRemovalReadinessResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitWorktreeRemovalReadiness? Readiness)
{
    public static GitWorktreeRemovalReadinessResult Success(GitWorktreeRemovalReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        return new(true, GitOperationFailureKind.None, null, readiness);
    }

    public static GitWorktreeRemovalReadinessResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}
