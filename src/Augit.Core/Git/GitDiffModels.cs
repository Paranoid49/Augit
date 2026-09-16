namespace Augit.Core.Git;

/// <summary>
/// 差异生成选项。
/// <paramref name="BaseRevision"/> 是左侧比较基准：工作区 Diff 用 HEAD；
/// 引用比较（规格 §7.9）传入具体分支、标签或版本。
/// <paramref name="TargetRevision"/> 只在历史比较（规格 §7.8）里给出：
/// 它表示右侧也取自 Git 中的某个版本，而不是工作区。
/// 两者同时给出时执行「版本 ↔ 版本」比较，不读取工作区文件。
/// </summary>
public sealed record GitDiffOptions(
    bool IgnoreWhitespace = false,
    string BaseRevision = "HEAD",
    string? TargetRevision = null)
{
    /// <summary>是否比较两个 Git 版本（历史比较），而不是「版本 ↔ 工作区」。</summary>
    public bool IsRevisionComparison => !string.IsNullOrWhiteSpace(TargetRevision);
}

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
