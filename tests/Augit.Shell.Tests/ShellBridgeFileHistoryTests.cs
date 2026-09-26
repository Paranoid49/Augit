using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/file-history` 载荷的作者列数据：作者与提交者两组身份都要回传。
///
/// 权威 `FileHistoryPanelImpl.AuthorColumnInfo`（`platform/vcs-impl/src/com/intellij/openapi/vcs/history/
/// FileHistoryPanelImpl.java:751-799`）用 `author != committerName` 决定值后加 `*`，
/// 单元格 tooltip 为 `{作者} <{邮箱}>`，提交者不同名时再追加 `, via {提交者} <{邮箱}>`
/// （`file.history.details.committer.tooltip.info` = "via {0}"，`VcsBundle.properties:935`）。
/// 因此宿主少发邮箱或提交者，界面就无法按权威呈现。
/// </summary>
[TestClass]
public sealed class ShellBridgeFileHistoryTests
{
    [TestMethod]
    public async Task 文件历史回传作者邮箱与提交者两组身份()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Committer Person");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "committer@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "notes.txt", "一\n", "test: 基线");
        // 第二版由**别人**代提交：作者 ≠ 提交者，正是 `*` 与 `, via …` 的条件。
        await File.WriteAllTextAsync(Path.Combine(temporary.FullPath, "notes.txt"), "一\n二\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "notes.txt");
        await GitTestEnvironment.RunAsync(
            runtime, temporary.FullPath,
            "-c", "user.name=Committer Person", "-c", "user.email=committer@example.invalid",
            "commit", "--author=Author Person <author@example.invalid>", "-m", "feat: 代提交");
        using ShellBridge bridge = new(temporary.FullPath);

        string payload = await bridge.HandleAsync(
            "{\"id\":1,\"method\":\"git/file-history\",\"params\":{\"path\":\"notes.txt\"}}",
            CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement result = document.RootElement.GetProperty("result");
        Assert.IsTrue(result.GetProperty("available").GetBoolean(), result.GetRawText());
        JsonElement newest = result.GetProperty("commits")[0];
        Assert.AreEqual("Author Person", newest.GetProperty("author").GetString());
        Assert.AreEqual("author@example.invalid", newest.GetProperty("authorEmail").GetString());
        Assert.AreEqual("Committer Person", newest.GetProperty("committerName").GetString());
        Assert.AreEqual("committer@example.invalid", newest.GetProperty("committerEmail").GetString());
        // 基线那条的作者与提交者同名：界面据此**不**加 `*`。
        JsonElement oldest = result.GetProperty("commits")[1];
        Assert.AreEqual(oldest.GetProperty("author").GetString(), oldest.GetProperty("committerName").GetString());
    }
}
