namespace Augit.Core.Search;

public sealed record FileSearchResult(string RelativePath, int Score);

public sealed record FileSearchResultSet(
    IReadOnlyList<FileSearchResult> Matches,
    bool IsTimedOut,
    bool IsCancelled,
    string? ErrorMessage)
{
    public string? Notice => IsTimedOut ? SearchOptions.SearchTimedOutMessage
        : IsCancelled ? "搜索已取消。" : ErrorMessage;
}

public sealed record TextSearchMatch(string RelativePath, int LineNumber, int ColumnNumber, string LineText);

public sealed record TextSearchResult(
    IReadOnlyList<TextSearchMatch> Matches,
    bool IsTruncated,
    bool IsTimedOut,
    bool IsCancelled,
    string? ErrorMessage)
{
    public string? Notice => IsTruncated ? SearchOptions.ResultsTruncatedMessage : ErrorMessage;
}
