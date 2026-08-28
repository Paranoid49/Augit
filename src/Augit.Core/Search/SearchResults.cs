namespace Augit.Core.Search;

public sealed record FileSearchResult(string RelativePath, int Score);

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
