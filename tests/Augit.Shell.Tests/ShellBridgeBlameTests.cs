using System.Text.Json;
using System.Text.RegularExpressions;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/blame` 载荷：归属行既要给**短日期**（归属槽位显示的就是它），也要给**日期时间**。
///
/// 权威 `GitFileAnnotation.getToolTip()`（`plugins/git4idea/backend/src/annotate/GitFileAnnotation.java:193`）
/// 的 `Date:` 行用 `DateFormatUtil.formatDateTime`（日期 ＋ 时间，
/// `platform/platform-api/src/com/intellij/util/text/DateFormatUtil.java:120-124`），
/// 而归属列旁显示的是短日期 ⇒ 两者必须是两个字段，界面才不会为了提示去猜时间。
/// </summary>
[TestClass]
public sealed class ShellBridgeBlameTests
{
    [TestMethod]
    public async Task 归属行同时回传短日期与日期时间()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "lines.txt", "第一行\n第二行\n", "test: 归属");
        using ShellBridge bridge = new(temporary.FullPath);

        string payload = await bridge.HandleAsync(
            "{\"id\":1,\"method\":\"git/blame\",\"params\":{\"path\":\"lines.txt\"}}",
            CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        JsonElement line = result.GetProperty("lines")[0];

        string? date = line.GetProperty("date").GetString();
        string? dateTime = line.GetProperty("dateTime").GetString();
        Assert.IsTrue(Regex.IsMatch(date!, @"^\d{4}/\d{1,2}/\d{1,2}$"), date);
        Assert.IsTrue(Regex.IsMatch(dateTime!, @"^\d{4}/\d{1,2}/\d{1,2} \d{1,2}:\d{2}$"), dateTime);
        // 日期时间必须以短日期开头（同一次本地时区换算，不是两次独立取时间）。
        Assert.StartsWith(date!, dateTime!, StringComparison.Ordinal);
        Assert.AreNotEqual(date, dateTime);
    }

    /// <summary>
    /// 「标注上一修订」的两半：载荷要带 `previousRevision`（`git blame --line-porcelain` 的
    /// `previous &lt;sha&gt; &lt;file&gt;` 头），桥接要能把 `revision` 透传给服务。
    ///
    /// 权威：`AnnotatePreviousRevisionAction.getFileRevision()` 用
    /// `PreviousFileRevisionProvider.getPreviousRevision(lineNumber)`
    /// （`GitFileAnnotation.java:482-501` 的第一分支就是 `lineInfo.getPreviousFileRevision()`），
    /// 拿到后在新标签里按该修订重新标注（`AnnotateRevisionAction`）。
    /// </summary>
    [TestMethod]
    public async Task 归属行带上一修订且可按该修订重新标注()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "lines.txt", "第一行\n第二行\n", "test: 初版");
        string first = (await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "rev-parse", "HEAD"))
            .StandardOutput.Trim();
        await File.WriteAllTextAsync(temporary.GetPath("lines.txt"), "第一行\n第二行改了\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "lines.txt");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "commit", "-m", "test: 改第二行");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement current = await BlameAsync(bridge);
        JsonElement currentLines = current.GetProperty("lines");
        // 第 2 行是**本次提交改的** ⇒ 它的上一修订就是初版；
        // 第 1 行本来就在初版里（它是初版引入的）⇒ 没有上一修订（权威此时把该动作隐藏）。
        string? previousOfChangedLine = currentLines[1].GetProperty("previousRevision").GetString();
        Assert.AreEqual(first, previousOfChangedLine, current.GetRawText());
        Assert.AreEqual(string.Empty, currentLines[0].GetProperty("previousRevision").GetString(), current.GetRawText());

        // 按该修订重新标注 ⇒ 整份文件都归到初版（说明 `revision` 真的透传到了 git blame）。
        JsonElement older = await BlameAsync(bridge, previousOfChangedLine);
        Assert.AreEqual(previousOfChangedLine, older.GetProperty("revision").GetString(), older.GetRawText());
        JsonElement olderLines = older.GetProperty("lines");
        Assert.IsTrue(
            olderLines.EnumerateArray().All(line => line.GetProperty("fullHash").GetString() == first),
            older.GetRawText());
        Assert.AreEqual("第二行", olderLines[1].GetProperty("content").GetString());
    }

    private static async Task<JsonElement> BlameAsync(ShellBridge bridge, string? revision = null)
    {
        string request = revision is null
            ? "{\"id\":9,\"method\":\"git/blame\",\"params\":{\"path\":\"lines.txt\"}}"
            : "{\"id\":9,\"method\":\"git/blame\",\"params\":{\"path\":\"lines.txt\",\"revision\":"
                + JsonSerializer.Serialize(revision) + "}}";
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }
}
