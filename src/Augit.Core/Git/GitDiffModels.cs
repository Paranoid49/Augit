namespace Augit.Core.Git;

public sealed record GitDiffOptions(bool IgnoreWhitespace = false);

public enum GitDiffContentStatus
{
    Ready,
    Binary,
    SideTooLarge,
    OutputTooLarge,
}

public sealed record GitDiffDocument(
    GitDiffContentStatus Status,
    string RelativePath,
    string? OriginalRelativePath,
    long OldSize,
    long NewSize,
    string? UnifiedPatch)
{
    public bool HasTextDiff => Status == GitDiffContentStatus.Ready && UnifiedPatch is not null;
}

public sealed record GitDiffResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    GitDiffDocument? Document)
{
    public static GitDiffResult Success(GitDiffDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(true, GitOperationFailureKind.None, null, document);
    }

    public static GitDiffResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, null);
    }
}

public enum GitDiffLineKind
{
    Metadata,
    HunkHeader,
    Context,
    Removed,
    Added,
    Modified,
    NoNewlineMarker,
}

public sealed record GitDiffLine(
    GitDiffLineKind Kind,
    int? OldLineNumber,
    int? NewLineNumber,
    string Text);

public readonly record struct GitTextSpan(int Start, int Length);

public sealed record GitSideBySideRow(
    int? OldLineNumber,
    string? OldText,
    IReadOnlyList<GitTextSpan> OldChanges,
    int? NewLineNumber,
    string? NewText,
    IReadOnlyList<GitTextSpan> NewChanges,
    GitDiffLineKind Kind);
