using Augit.Core.Search;

namespace Augit.Core.Tests;

/// <summary>
/// 搜索状态文案的测试（规格 §7.15 第八条：超时和取消后结果区域**保留已完成结果**并标记状态）。
///
/// 视觉基线把非正常结束时的产品文案写死在 `ux-mockups/repository-search.html` 的场景参数里
/// （`?search-state=timeout`）：宿主文案必须与它逐字一致，否则"标记状态"在真实界面与视觉稿里
/// 说的不是同一句话（`ResultsTruncatedMessage` 就是按这条口径对齐的）。
/// 结果是否保留由**结果集本身**承载（超时/取消只是标记，不裁剪已收集的匹配）：
/// 服务在 `OperationCanceledException` 分支里返回的仍是取消前已收集的列表。
/// </summary>
[TestClass]
public sealed class SearchStatusTextTests
{
    [TestMethod]
    public void 超时文案说明已保留完成的结果且与视觉稿逐字一致()
    {
        // 宿主文案必须与视觉基线里的产品文案逐字一致（共享文件与运行时同源的那一份）。
        string mockup = File.ReadAllText(FindRepositoryFile("web/src/mockup.js"));
        StringAssert.Contains(mockup, SearchOptions.SearchTimedOutMessage);
        // 结果集在超时标记下给出的说明就是这句话（经辅助方法构造，避免分析器把断言折叠成常量）。
        Assert.AreEqual(SearchOptions.SearchTimedOutMessage, NoticeOf(timedOut: true, cancelled: false));
    }

    private static string? NoticeOf(bool timedOut, bool cancelled)
        => new FileSearchResultSet([new FileSearchResult("a.cs", 0)], timedOut, cancelled, ErrorMessage: null).Notice;

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件：{relativePath}");
    }

    [TestMethod]
    public void 取消文案与超时文案可以区分()
    {
        FileSearchResultSet cancelled = new([], IsTimedOut: false, IsCancelled: true, ErrorMessage: null);
        FileSearchResultSet timedOut = new([], IsTimedOut: true, IsCancelled: true, ErrorMessage: null);

        Assert.AreEqual("搜索已取消。", cancelled.Notice);
        // 同时命中两个标记时以超时为准（`Notice` 的判定顺序），两者不能给出同一句话。
        Assert.AreEqual(SearchOptions.SearchTimedOutMessage, timedOut.Notice);
        Assert.AreNotEqual(cancelled.Notice, timedOut.Notice);
    }

    [TestMethod]
    public void 文本搜索的超时取消与截断各有自己的文案()
    {
        TextSearchResult timedOut = new([], IsTruncated: false, IsTimedOut: true, IsCancelled: false, ErrorMessage: SearchOptions.SearchTimedOutMessage);
        TextSearchResult cancelled = new([], IsTruncated: false, IsTimedOut: false, IsCancelled: true, ErrorMessage: "搜索已取消。");
        TextSearchResult truncated = new([], IsTruncated: true, IsTimedOut: false, IsCancelled: false, ErrorMessage: null);

        Assert.AreEqual(SearchOptions.SearchTimedOutMessage, timedOut.Notice);
        Assert.AreEqual("搜索已取消。", cancelled.Notice);
        Assert.AreEqual(SearchOptions.ResultsTruncatedMessage, truncated.Notice);
    }
}
