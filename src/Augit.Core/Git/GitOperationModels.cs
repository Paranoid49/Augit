namespace Augit.Core.Git;

public enum GitAdvancedOperationKind
{
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

public enum GitOperationAction
{
    Continue,
    Skip,
    Abort,
}

public sealed record GitOperationRequest(
    GitAdvancedOperationKind Kind,
    string Revision);

public sealed record GitConflictFileInfo(
    string RelativePath,
    bool HasAncestor,
    bool HasYours,
    bool HasTheirs);

public sealed record GitOperationSession(
    GitOperationKind Kind,
    bool IsInProgress,
    bool HasConflicts,
    string? CurrentBranch,
    IReadOnlyList<GitConflictFileInfo> ConflictFiles,
    bool CanContinue,
    bool CanSkip,
    bool CanAbort,
    int? CurrentStep = null,
    int? TotalSteps = null)
{
    public bool Supports(GitOperationAction action)
    {
        return action switch
        {
            GitOperationAction.Continue => CanContinue,
            GitOperationAction.Skip => CanSkip,
            GitOperationAction.Abort => CanAbort,
            _ => false,
        };
    }
}

public sealed record GitAdvancedOperationResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitOperationSession? Session,
    GitStatusSnapshot? ActualStatus)
{
    public static GitAdvancedOperationResult Success(
        GitOperationSession session,
        GitStatusSnapshot? actualStatus = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new(true, GitOperationFailureKind.None, null, session, actualStatus);
    }

    public static GitAdvancedOperationResult Failure(
        GitOperationFailureKind failureKind,
        string errorMessage,
        GitOperationSession? session = null,
        GitStatusSnapshot? actualStatus = null)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, session, actualStatus);
    }
}
