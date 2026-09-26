namespace Augit.Core.Search;

public sealed record SearchOptions(
    string Query,
    bool MatchCase = false,
    bool MatchWholeWord = false,
    bool UseRegularExpression = false,
    bool IncludeIgnoredFiles = false)
{
    /// <summary>
    /// 快速打开（Go to File）返回的最大条目数。
    ///
    /// 权威 `SearchEverywhereUI.SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT = 30`
    /// （`platform/lang-impl/src/com/intellij/ide/actions/searcheverywhere/SearchEverywhereUI.java:217-218`）：
    /// 每个贡献者一次要多少条由 `contributors.size() > 1 ? MULTIPLE_CONTRIBUTORS_ELEMENTS_LIMIT : SINGLE_CONTRIBUTOR_ELEMENTS_LIMIT`
    /// 决定（同文件 `:951-956`，另外 Files 页里的 Files 贡献者固定按单贡献者的 30 要）。
    /// Augit 的快速打开就是 `GotoFileAction` 那条**单贡献者**（Files）路径 ⇒ 30；
    /// 多贡献者的 15 与"全仓搜索"的 1000 条阈值是另外两件事（见 `SearchOptions.MaximumTextResults`）。
    /// </summary>
    public const int MaximumFileResults = 30;

    public const int MaximumTextResults = 1000;

    /// <summary>
    /// 一次请求最多返回的文本结果数（默认 <see cref="MaximumTextResults"/>）。
    ///
    /// 权威 `UsageLimitUtil.USAGES_LIMIT`（`ide.find.result.count.warning.limit`，默认 1000）只是**提示阈值**：
    /// 用户选 `Continue` 之后搜索会继续跑完（`UsageViewManagerImpl.showTooManyUsagesWarningLater`，`:334-357`）。
    /// Augit 用**分页**表达"继续"：每次仍是有界的一页，界面按 <see cref="SkipResults"/> 取下页并追加，
    /// 既等价于"继续跑完"，又不会让单次桥接响应超过 WebView2 可靠传输的规模。
    /// </summary>
    public int MaximumResults { get; init; } = MaximumTextResults;

    /// <summary>跳过前面的多少条结果（"继续搜索"取下一页时用）。</summary>
    public int SkipResults { get; init; }

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 结果到限时的回退说明。权威在这里不是一句"已停止"，而是弹 "Too Many Results" 让用户选
    /// Continue／Abort（`UsageLimitUtil.showTooManyUsagesWarning`，`:26-34`）⇒ 文案不再断言"已停止搜索"，
    /// 界面会先给选择；用户中止后才由界面写停止说明。
    /// </summary>
    public const string ResultsTruncatedMessage = "结果超过 1000 条，可以继续搜索，或缩小范围与关键词。";

    public bool IsEmpty => string.IsNullOrEmpty(Query);
}
