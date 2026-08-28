namespace Augit.Core.Git;

public enum GitRepositoryKind
{
    PlainDirectory,
    WorkingTree,
    BareRepository,
}

public enum GitOperationKind
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    SmartCheckout,
    Bisect,
}

public sealed record GitRepositorySnapshot(
    GitRepositoryKind Kind,
    string WorkspacePath,
    string? RepositoryRoot,
    string? GitDirectory,
    string? GitCommonDirectory,
    GitOperationKind Operation,
    bool HasConflicts)
{
    public bool IsRepository => Kind != GitRepositoryKind.PlainDirectory;

    public static GitRepositorySnapshot PlainDirectory(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        return new(GitRepositoryKind.PlainDirectory, workspacePath, null, null, null, GitOperationKind.None, false);
    }
}

public enum GitOperationFailureKind
{
    None,
    Cancelled,
    TimedOut,
    InteractiveInputRequired,
    CommandFailed,
    InvalidRequest,
}

public sealed record GitRepositoryOperationResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitRepositorySnapshot? Repository)
{
    public bool IsCancelled => FailureKind == GitOperationFailureKind.Cancelled;

    public static GitRepositoryOperationResult Success(GitRepositorySnapshot repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return new(true, GitOperationFailureKind.None, null, repository);
    }

    public static GitRepositoryOperationResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        GitRepositorySnapshot? repository = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, repository);
    }
}
