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
    int? TotalSteps = null,
    // 规格 §7.13：Continue 的**前置条件未满足**时保留按钮并禁用，而"当前操作根本不支持
    // Continue"时必须直接不显示。两者必须可区分，否则界面只能自己再判断一遍宿主规则
    // （迟早漂移），因此这里把"是否支持"单独暴露。
    bool SupportsContinue = false)
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
