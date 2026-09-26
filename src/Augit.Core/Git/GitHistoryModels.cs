namespace Augit.Core.Git;

public enum GitReferenceKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
    Other,
}

public sealed record GitReferenceInfo(GitReferenceKind Kind, string Name, bool IsHead = false);

/// <summary>
/// 提交历史的筛选条件（规格 §7.8、`product-spec.md:112`）。
///
/// 各字段与权威的日志筛选同义：<see cref="Message"/> 是提交信息子串（大小写不敏感）、
/// <see cref="Author"/> 是作者、<see cref="Since"/>/<see cref="Until"/> 是日期区间、
/// <see cref="Branch"/> 是一个引用名（含 `HEAD`）、<see cref="FilePath"/> 是仓库内相对路径。
///
/// <see cref="Hash"/> 照权威的哈希筛选（`VcsLogFilterObject.fromHash`）：逗号／分号／空白分隔的
/// **哈希前缀**（每个 `[0-9a-fA-F]{7,64}`，只要有一个不匹配就整体失效），命中的是"完整哈希以该前缀
/// 开头的所有提交"；且它一旦成立就**短路**其余筛选（权威 `VcsLogFiltererImpl.filter()` 的
/// "hashes should be shown, no matter if they match other filters or not"）。
///
/// <see cref="RangeExclusive"/>/<see cref="RangeInclusive"/> 照权威的范围筛选
/// （`VcsLogFilterObject.fromRange(exclusiveRef, inclusiveRef)`、`VcsLogRangeFilter`，
/// 文本表现是 `"$exclusive..$inclusive"`）：取从 `Inclusive` 可达但不从 `Exclusive` 可达的提交，
/// 即 `git log <Exclusive>..<Inclusive>`。两端必须**同时**给出。
///
/// <see cref="Branches"/> 是**多个**分支名（引用树多选后的「更新分支筛选」「与当前分支比较」等），
/// 照权威 `VcsLogFilterObject.fromBranches(branchNames)`：取从**任一**匹配分支可达的提交（并集，
/// 即 `git log b1 b2 …`）；它优先于单值 <see cref="Branch"/>。
///
/// <see cref="Paths"/> 是**多个**路径（权威 `VcsLogStructureFilter` 持有的就是一组 `FilePath`，
/// `VcsLogFilterObject.fromPaths(...)`，`FileFilterModel.createFilter`），
/// 对应 `git log … -- <path1> <path2> …`；它优先于单值 <see cref="FilePath"/>（历史面板的"按路径筛选"给多值，
/// 文件历史面板给单值）。
/// </summary>
public sealed record GitHistoryFilter(
    string? Message = null,
    string? Hash = null,
    string? Author = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    string? Branch = null,
    string? FilePath = null,
    string? RangeExclusive = null,
    string? RangeInclusive = null,
    IReadOnlyList<string>? Branches = null,
    IReadOnlyList<string>? Authors = null,
    IReadOnlyList<string>? Paths = null);

/// <summary>
/// 提交作者（权威 `VcsLogUserResolver`／`GitUserRegistry` 从日志里收集的用户集合；
/// `VcsLogFilterObject.fromUserNames` 按它做"按用户筛选"）。名字与邮箱分开带回，界面据此显示与提交。
/// </summary>
public sealed record GitAuthorInfo(string Name, string Email);

public sealed record GitAuthorsResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitAuthorInfo> Authors)
{
    public static GitAuthorsResult Success(IReadOnlyList<GitAuthorInfo> authors)
    {
        ArgumentNullException.ThrowIfNull(authors);
        return new(true, GitOperationFailureKind.None, null, authors);
    }

    public static GitAuthorsResult Failure(GitOperationFailureKind failureKind, string errorMessage)
    {
        if (failureKind == GitOperationFailureKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, failureKind, errorMessage, []);
    }
}

public sealed record GitHistoryRequest(
    int Page = 0,
    int PageSize = 100,
    GitHistoryFilter? Filter = null);

/// <summary>
/// 一条提交历史。作者与提交者分成两组（`%an`/`%ae` 与 `%cn`/`%ce`），
/// 因为文件历史的作者列要按权威 `FileHistoryPanelImpl.AuthorColumnInfo` 的规则呈现：
/// 作者与提交者不同名时值后加 `*`，单元格 tooltip 再追加 `, via {提交者}`（见 <see cref="CommitterName"/>）。
/// </summary>
public sealed record GitHistoryEntry(
    string Graph,
    string FullHash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    string AuthorEmail,
    string CommitterName,
    string CommitterEmail,
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

/// <summary>
/// 一行归属。
///
/// <see cref="PreviousRevision"/> 来自 `git blame --line-porcelain` 每条记录的
/// `previous &lt;sha&gt; &lt;file&gt;` 头（只有该行在更早的修订里已存在时才有）——
/// 权威 `GitFileAnnotation.LineInfo.getPreviousFileRevision()`（`GitPreviousFileRevisionProvider`
/// 的第一分支，`plugins/git4idea/backend/src/annotate/GitFileAnnotation.java:482-501`）正是读它，
/// 供「标注上一修订」动作使用（`AnnotatePreviousRevisionAction`）。根提交的行没有该头 ⇒ 空串。
/// </summary>
public sealed record GitBlameLine(
    int LineNumber,
    string CommitHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Summary,
    string RelativePath,
    string Content,
    string PreviousRevision = "");

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

/// <summary>
/// 待推送提交的读取结果。没有上游时 IsSuccess 为 false，Error 说明原因，
/// 界面据此保留「定义远端」入口而不是显示成失败。
/// </summary>
public sealed record GitUnpushedResult(
    bool IsSuccess,
    GitOperationFailureKind FailureKind,
    string? ErrorMessage,
    IReadOnlyList<GitHistoryEntry>? Commits,
    string? Upstream)
{
    public static GitUnpushedResult Success(IReadOnlyList<GitHistoryEntry> commits, string upstream)
    {
        ArgumentNullException.ThrowIfNull(commits);
        return new(true, GitOperationFailureKind.None, null, commits, upstream);
    }

    public static GitUnpushedResult Failure(GitOperationFailureKind kind, string message)
    {
        return new(false, kind, message, null, null);
    }
}
