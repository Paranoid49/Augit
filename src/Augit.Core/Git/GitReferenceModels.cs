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

/// <summary>
/// 「我的分支」的查询结果（权威 `BranchesDashboardUtil.checkIsMyBranchesSynchronously`，
/// `plugins/git4idea/backend/src/ui/branch/dashboard/BranchesDashboardUtil.kt:85-132`）：
/// 分支的**独占提交**非空、且**全部**由当前 Git 用户提交，才算"我的分支"。
/// <see cref="Author"/> 是判定用的当前用户（`user.email`，缺失时 `user.name`），
/// <see cref="Mine"/> 是命中的分支名（与 `git/references` 同名）。
/// </summary>
public sealed record GitMyBranchesResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    string? Author,
    IReadOnlyList<string> Mine)
{
    public static GitMyBranchesResult Success(string? author, IReadOnlyList<string> mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        return new(true, GitOperationFailureKind.None, null, author, mine);
    }

    public static GitMyBranchesResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null, []);
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
