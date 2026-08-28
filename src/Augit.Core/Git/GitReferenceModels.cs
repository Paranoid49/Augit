namespace Augit.Core.Git;

public sealed record GitBranchInfo(
    string Name,
    string FullName,
    bool IsRemote,
    bool IsCurrent,
    string? Upstream,
    string CommitHash,
    string Subject);

public sealed record GitTagInfo(
    string Name,
    string CommitHash,
    bool IsAnnotated,
    string? Message);

public sealed record GitReferenceSnapshot(
    IReadOnlyList<GitBranchInfo> Branches,
    IReadOnlyList<GitTagInfo> Tags);

public sealed record GitReferenceResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitReferenceSnapshot? Snapshot)
{
    public static GitReferenceResult Success(GitReferenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(true, GitOperationFailureKind.None, null, snapshot);
    }

    public static GitReferenceResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitActionResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitStatusSnapshot? ActualStatus)
{
    public static GitActionResult Success(GitStatusSnapshot? actualStatus = null)
    {
        return new(true, GitOperationFailureKind.None, null, actualStatus);
    }

    public static GitActionResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        GitStatusSnapshot? actualStatus = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, actualStatus);
    }
}
