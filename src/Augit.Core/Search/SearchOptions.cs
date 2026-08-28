namespace Augit.Core.Search;

public sealed record SearchOptions(
    string Query,
    bool MatchCase = false,
    bool MatchWholeWord = false,
    bool UseRegularExpression = false,
    bool IncludeIgnoredFiles = false)
{
    public const int MaximumFileResults = 100;

    public const int MaximumTextResults = 1000;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public const string ResultsTruncatedMessage = "结果超过 1000 条，已停止搜索，请缩小范围或使用更准确的关键词。";

    public bool IsEmpty => string.IsNullOrEmpty(Query);
}
