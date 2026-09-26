using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/history` 的可选筛选参数（规格 §7.8、`product-spec.md:112`）。
///
/// 界面侧对应权威日志筛选栏：`message` ← `VcsLogTextFilter`、`hash` ← `VcsLogHashFilter`、
/// `author` ← `VcsLogUserFilter`、`since`/`until` ← `VcsLogDateFilter`、
/// `branch` ← `VcsLogBranchFilter`、`path` ← `VcsLogStructureFilter`。
/// 这里验证参数确实走到了宿主的过滤通道，以及「文本或哈希」同时送出两者时
/// 哈希筛选**短路**文本筛选（权威 `VcsLogFiltererImpl.filter()` 的
/// "hashes should be shown, no matter if they match other filters or not"）。
/// </summary>
[TestClass]
public sealed class ShellBridgeHistoryTests
{
    [TestMethod]
    public async Task 历史筛选按文本分支作者日期与路径过滤()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement all = await InvokeAsync(bridge, HistoryRequest(1));
        Assert.IsTrue(all.GetProperty("available").GetBoolean(), all.GetRawText());
        Assert.IsTrue(all.GetProperty("isRepository").GetBoolean(), all.GetRawText());
        Assert.HasCount(3, all.GetProperty("commits").EnumerateArray().ToArray());
        string oldestFull = all.GetProperty("commits")[2].GetProperty("fullHash").GetString()!;

        // 文本：提交信息子串（大小写不敏感由 Git 负责，这里只钉子串命中）。
        JsonElement byMessage = await InvokeAsync(bridge, HistoryRequest(2, message: "可检索"));
        Assert.HasCount(1, byMessage.GetProperty("commits").EnumerateArray().ToArray());
        Assert.Contains(
            "可检索",
            byMessage.GetProperty("commits")[0].GetProperty("subject").GetString()!,
            StringComparison.Ordinal);

        // 分支：只列该引用可达的提交（feature/ux 停在中间那条，因此比 HEAD 少一条）。
        JsonElement byBranch = await InvokeAsync(bridge, HistoryRequest(3, branch: "feature/ux"));
        Assert.HasCount(2, byBranch.GetProperty("commits").EnumerateArray().ToArray());
        Assert.IsFalse(
            byBranch.GetProperty("commits").EnumerateArray()
                .Any(commit => commit.GetProperty("fullHash").GetString() == oldFullOf(all, 0)),
            "HEAD 上独有的提交不得出现在 feature/ux 的筛选结果里。");

        // 作者与路径。
        JsonElement byAuthor = await InvokeAsync(bridge, HistoryRequest(4, author: "Augit Tests"));
        Assert.HasCount(3, byAuthor.GetProperty("commits").EnumerateArray().ToArray());
        JsonElement byPath = await InvokeAsync(bridge, HistoryRequest(5, path: "latest.txt"));
        Assert.HasCount(1, byPath.GetProperty("commits").EnumerateArray().ToArray());

        // 日期区间：未来起点 ⇒ 空；过去终点 ⇒ 全部。
        JsonElement future = await InvokeAsync(
            bridge,
            HistoryRequest(6, since: DateTimeOffset.UtcNow.AddDays(1).ToString("O")));
        Assert.IsEmpty(future.GetProperty("commits").EnumerateArray().ToArray());
        Assert.IsFalse(future.GetProperty("hasNextPage").GetBoolean());
        JsonElement past = await InvokeAsync(
            bridge,
            HistoryRequest(7, until: DateTimeOffset.UtcNow.AddDays(1).ToString("O")));
        Assert.HasCount(3, past.GetProperty("commits").EnumerateArray().ToArray());

        // 哈希前缀命中的就是那条提交本身（不是"从它往前看"）。
        JsonElement byHash = await InvokeAsync(bridge, HistoryRequest(8, hash: oldestFull[..7]));
        Assert.HasCount(1, byHash.GetProperty("commits").EnumerateArray().ToArray());
        Assert.AreEqual(
            oldestFull,
            byHash.GetProperty("commits")[0].GetProperty("fullHash").GetString());
    }

    [TestMethod]
    public async Task 文本或哈希同时送出时哈希命中即短路文本筛选()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement all = await InvokeAsync(bridge, HistoryRequest(1));
        string oldestFull = all.GetProperty("commits")[2].GetProperty("fullHash").GetString()!;

        // 界面的「文本或哈希」照权威同时送出文本筛选与哈希筛选
        // （`TextFilterModel.setFilterText`）；哈希成立时必须仍然显示命中的提交。
        JsonElement shortCircuited = await InvokeAsync(
            bridge,
            HistoryRequest(2, message: "不会有任何提交命中这段文字", hash: oldestFull[..7]));
        Assert.HasCount(1, shortCircuited.GetProperty("commits").EnumerateArray().ToArray());
        Assert.AreEqual(
            oldestFull,
            shortCircuited.GetProperty("commits")[0].GetProperty("fullHash").GetString());

        // 不像哈希的文本只走文本筛选：此时"不命中的文本"确实筛空（不是被当成 revision）。
        JsonElement textOnly = await InvokeAsync(
            bridge,
            HistoryRequest(3, message: "不会有任何提交命中这段文字", hash: "abc12"));
        Assert.IsEmpty(textOnly.GetProperty("commits").EnumerateArray().ToArray());
    }

    [TestMethod]
    public async Task 非法时间参数返回可读原因()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        string payload = await bridge.HandleAsync(
            HistoryRequest(1, since: "不是时间"),
            CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        Assert.IsTrue(
            document.RootElement.TryGetProperty("error", out JsonElement error),
            document.RootElement.GetRawText());
        StringAssert.Contains(error.GetString(), "since");
    }

    [TestMethod]
    public async Task 范围筛选取inclusive相对exclusive的独有提交()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = (await GitTestEnvironment.RunRawAsync(
            runtime, temporary.FullPath, "branch", "--show-current")).StandardOutput.Trim();
        // 从当前提交起一个新分支再加一条：于是 `base..compare/ahead` 恰好是这一条。
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "compare/ahead");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "ahead.txt", "ahead\n", "feat: 领先一条");
        using ShellBridge bridge = new(temporary.FullPath);

        // 权威 `VcsLogRangeFilter`（`fromRange(exclusiveRef, inclusiveRef)`）：「与当前分支比较」看的
        // 就是 `<当前分支>..<选中分支>` 这一批提交（`GitCompareBranchesUi` 的构造）。
        JsonElement ahead = await InvokeAsync(
            bridge,
            HistoryRequest(1, rangeExclusive: baseBranch, rangeInclusive: "compare/ahead"));
        Assert.HasCount(1, ahead.GetProperty("commits").EnumerateArray().ToArray());
        Assert.Contains(
            "领先一条",
            ahead.GetProperty("commits")[0].GetProperty("subject").GetString()!,
            StringComparison.Ordinal);

        // 只给一端 ⇒ 可读原因（`git/history` 沿用"失败走 result.reason"的既有契约，不静默退化成整仓历史）。
        JsonElement halfRange = await InvokeAsync(
            bridge,
            HistoryRequest(2, rangeInclusive: "compare/ahead"));
        StringAssert.Contains(halfRange.GetProperty("reason").GetString(), "两端");
    }

    [TestMethod]
    public async Task 多分支筛选取各分支可达提交的并集()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        // 夹具：base → search（`feature/ux` 停在这里）→ latest（当前分支）。
        // 权威 `VcsLogFilterObject.fromBranches(branchNames)` ⇒ `git log <b1> <b2> …`（并集）。
        JsonElement one = await InvokeAsync(bridge, HistoryRequest(1, branches: ["feature/ux"]));
        Assert.HasCount(2, one.GetProperty("commits").EnumerateArray().ToArray());

        JsonElement all = await InvokeAsync(bridge, HistoryRequest(2));
        Assert.HasCount(3, all.GetProperty("commits").EnumerateArray().ToArray());
        string head = (await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "branch", "--show-current"))
            .StandardOutput.Trim();
        JsonElement both = await InvokeAsync(bridge, HistoryRequest(3, branches: ["feature/ux", head]));
        Assert.HasCount(3, both.GetProperty("commits").EnumerateArray().ToArray());
    }

    [TestMethod]
    public async Task 多路径筛选按任一命中路径过滤()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        // 权威「按路径筛选」的筛选值是**一组**路径（`VcsLogStructureFilter`），
        // 桥接层收 `paths` 字符串数组；只给一个路径时与旧的单值 `path` 等价。
        JsonElement single = await InvokeAsync(bridge, HistoryRequest(1, paths: ["latest.txt"]));
        Assert.HasCount(1, single.GetProperty("commits").EnumerateArray().ToArray());

        JsonElement both = await InvokeAsync(
            bridge,
            HistoryRequest(2, paths: ["latest.txt", "search.txt"]));
        Assert.HasCount(2, both.GetProperty("commits").EnumerateArray().ToArray());

        // 空数组按"没有这个筛选"处理（整仓历史），不退化成"筛掉全部提交"。
        JsonElement none = await InvokeAsync(bridge, HistoryRequest(3, paths: []));
        Assert.HasCount(3, none.GetProperty("commits").EnumerateArray().ToArray());
    }

    [TestMethod]
    public async Task 历史分页把page交给宿主()
    {
        // 规格 §7.8「分页加载在列表底部触发」：界面滚动触底时把 page 送来。
        // 夹具只有 3 条，因此"第 1 页为空"就是 page 确实生效的证据（没生效会再次返回 3 条）。
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement first = await InvokeAsync(bridge, HistoryRequest(1, page: 0));
        Assert.HasCount(3, first.GetProperty("commits").EnumerateArray().ToArray());

        JsonElement second = await InvokeAsync(bridge, HistoryRequest(2, page: 1));
        Assert.IsTrue(second.GetProperty("available").GetBoolean(), second.GetRawText());
        Assert.HasCount(0, second.GetProperty("commits").EnumerateArray().ToArray());
        Assert.IsFalse(second.GetProperty("hasNextPage").GetBoolean(), second.GetRawText());

        // 负页码按第 0 页处理，不抛出。
        JsonElement negative = await InvokeAsync(bridge, HistoryRequest(3, page: -5));
        Assert.HasCount(3, negative.GetProperty("commits").EnumerateArray().ToArray());
    }

    private static string oldFullOf(JsonElement response, int index) =>
        response.GetProperty("commits")[index].GetProperty("fullHash").GetString()!;

    private static string HistoryRequest(
        long id,
        string? message = null,
        string? hash = null,
        string? author = null,
        string? since = null,
        string? until = null,
        string? branch = null,
        string? path = null,
        string? rangeExclusive = null,
        string? rangeInclusive = null,
        string[]? branches = null,
        string[]? paths = null,
        int? page = null)
    {
        List<string> fields = [];
        void Add(string name, string? value)
        {
            if (value is not null)
            {
                fields.Add("\"" + name + "\":" + JsonSerializer.Serialize(value));
            }
        }

        Add("message", message);
        Add("hash", hash);
        Add("author", author);
        Add("since", since);
        Add("until", until);
        Add("branch", branch);
        Add("path", path);
        Add("rangeExclusive", rangeExclusive);
        Add("rangeInclusive", rangeInclusive);
        if (page is not null)
        {
            fields.Add("\"page\":" + page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (branches is not null)
        {
            fields.Add("\"branches\":" + JsonSerializer.Serialize(branches));
        }
        if (paths is not null)
        {
            fields.Add("\"paths\":" + JsonSerializer.Serialize(paths));
        }
        return "{\"id\":" + id + ",\"method\":\"git/history\",\"params\":{" + string.Join(',', fields) + "}}";
    }

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string request)
    {
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }

    private static async Task CreateRepositoryAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "base.txt", "基线\n", "test: base");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "search.txt", "可检索\n", "feat: 可检索");
        // feature/ux 停在"可检索"这条：分支筛选与 HEAD 的结果因此可区分。
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "branch", "feature/ux");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "latest.txt", "最新\n", "fix: 最新");
    }
}
