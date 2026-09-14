namespace Augit.Core.Git;

public enum GitReferenceKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
    Other,
}

public sealed record GitReferenceInfo(GitReferenceKind Kind, string Name, bool IsHead = false);

public sealed record GitHistoryFilter(
    string? Message = null,
    string? Hash = null,
    string? Author = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    string? Branch = null,
    string? FilePath = null);

public sealed record GitHistoryRequest(
    int Page = 0,
    int PageSize = 100,
    GitHistoryFilter? Filter = null);

public sealed record GitHistoryEntry(
    string Graph,
    string FullHash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject,
    IReadOnlyList<GitReferenceInfo> References);

public sealed record GitHistoryPage(
    int Page,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<GitHistoryEntry> Entries);

public sealed record GitHistoryResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitHistoryPage? Page)
{
    public static GitHistoryResult Success(GitHistoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new(true, GitOperationFailureKind.None, null, page);
    }

    public static GitHistoryResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitCommitChangedFile(
    GitChangeKind Kind,
    string RelativePath,
    string? OriginalRelativePath);

public sealed record GitCommitDetails(
    GitHistoryEntry Commit,
    string Body,
    IReadOnlyList<GitCommitChangedFile> Files);

public sealed record GitCommitDetailsResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitCommitDetails? Details)
{
    public static GitCommitDetailsResult Success(GitCommitDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new(true, GitOperationFailureKind.None, null, details);
    }

    public static GitCommitDetailsResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitBlameLine(
    int LineNumber,
    string CommitHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Summary,
    string RelativePath,
    string Content);

public sealed record GitBlameResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitBlameLine>? Lines)
{
    public static GitBlameResult Success(IReadOnlyList<GitBlameLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new(true, GitOperationFailureKind.None, null, lines);
    }

    public static GitBlameResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public sealed record GitComparisonRequest(
    string BaseRevision,
    string? TargetRevision = null,
    string? RelativePath = null,
    bool IgnoreWhitespace = false);

public sealed record GitComparisonDocument(
    GitDiffContentStatus Status,
    string BaseRevision,
    string? TargetRevision,
    string? RelativePath,
    string? UnifiedPatch);

public sealed record GitComparisonResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitComparisonDocument? Document)
{
    public static GitComparisonResult Success(GitComparisonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(true, GitOperationFailureKind.None, null, document);
    }

    public static GitComparisonResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}
