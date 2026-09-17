using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/stash-write` / `git/stash-content` 的宿主契约（规格 §7.11 应用、弹出、删除；
/// 设计稿的 Stash 详情还有"包含 N 个文件"）。
///
/// 网页层只认 `ok`/`reason` 与 `stashes` 三个字段，字段语义错了界面会静默出错；
/// 引用守卫（只接受 `stash@{n}`）不测就等于没有。
/// </summary>
[TestClass]
public sealed class ShellBridgeStashWriteTests
{
    [TestMethod]
    public async Task 应用与弹出按真实状态更新Stash列表()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryWithStashAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement content = await InvokeAsync(
            bridge,
            """{"id":1,"method":"git/stash-content","params":{"reference":"stash@{0}"}}""");
        Assert.IsTrue(content.GetProperty("available").GetBoolean(), content.GetRawText());
        Assert.AreEqual(1, content.GetProperty("files").GetArrayLength(), content.GetRawText());
        Assert.AreEqual("stashed.txt", content.GetProperty("files")[0].GetProperty("path").GetString());

        JsonElement applied = await InvokeAsync(
            bridge,
            """{"id":2,"method":"git/stash-write","params":{"action":"apply","reference":"stash@{0}"}}""");
        Assert.IsTrue(applied.GetProperty("ok").GetBoolean(), applied.GetRawText());
        // 应用保留 Stash：列表条数不变。
        Assert.AreEqual(1, applied.GetProperty("stashes").GetProperty("stashes").GetArrayLength());
        // Windows 上 core.autocrlf 会把工作区文件换成 CRLF：只比较语义内容。
        string appliedText = (await File.ReadAllTextAsync(temporary.GetPath("stashed.txt")))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.AreEqual("已改动\n", appliedText);

        // 应用之后工作区已有改动，弹出会被 Git 拒绝（"local changes would be overwritten"）：
        // 先清干净再弹出，才是"弹出"这个动作本身的语义。
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "reset", "--hard", "HEAD");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "clean", "-fd");
        JsonElement popped = await InvokeAsync(
            bridge,
            """{"id":3,"method":"git/stash-write","params":{"action":"pop","reference":"stash@{0}"}}""");
        Assert.IsTrue(popped.GetProperty("ok").GetBoolean(), popped.GetRawText());
        // 弹出会删掉它：列表变空。
        Assert.AreEqual(0, popped.GetProperty("stashes").GetProperty("stashes").GetArrayLength());
    }

    [TestMethod]
    public async Task 删除只动Stash列表不改工作区()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryWithStashAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        GitCall before = await GitTestEnvironment.RunRawAsync(
            runtime, temporary.FullPath, "status", "--porcelain");
        JsonElement dropped = await InvokeAsync(
            bridge,
            """{"id":4,"method":"git/stash-write","params":{"action":"drop","reference":"stash@{0}"}}""");

        Assert.IsTrue(dropped.GetProperty("ok").GetBoolean(), dropped.GetRawText());
        Assert.AreEqual(0, dropped.GetProperty("stashes").GetProperty("stashes").GetArrayLength());
        GitCall after = await GitTestEnvironment.RunRawAsync(
            runtime, temporary.FullPath, "status", "--porcelain");
        Assert.AreEqual(before.StandardOutput, after.StandardOutput, "删除 Stash 不得改写工作区。");
    }

    [TestMethod]
    public async Task 引用与动作非法时被挡下且不改变列表()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryWithStashAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        // 任意 rev / 选项都不接受：网页层若能传 `--all` 就等于拿到任意 Git 参数。
        JsonElement injected = await InvokeAsync(
            bridge,
            """{"id":5,"method":"git/stash-write","params":{"action":"drop","reference":"--all"}}""");
        Assert.IsTrue(injected.GetProperty("available").GetBoolean(), injected.GetRawText());
        Assert.IsFalse(injected.GetProperty("ok").GetBoolean(), injected.GetRawText());
        Assert.IsFalse(string.IsNullOrWhiteSpace(injected.GetProperty("reason").GetString()));
        Assert.AreEqual(1, injected.GetProperty("stashes").GetProperty("stashes").GetArrayLength());

        JsonElement missing = await InvokeAsync(
            bridge,
            """{"id":6,"method":"git/stash-write","params":{"action":"apply","reference":"stash@{9}"}}""");
        Assert.IsFalse(missing.GetProperty("ok").GetBoolean(), missing.GetRawText());
        Assert.AreEqual(1, missing.GetProperty("stashes").GetProperty("stashes").GetArrayLength());

        // 参数与动作非法走顶层 error（网页层只认这个通道）。
        JsonElement noReference = await InvokeRawAsync(
            bridge,
            """{"id":7,"method":"git/stash-write","params":{"action":"apply"}}""");
        Assert.IsTrue(noReference.TryGetProperty("error", out JsonElement noReferenceError), noReference.GetRawText());
        StringAssert.Contains(noReferenceError.GetString(), "reference");

        JsonElement unknownAction = await InvokeRawAsync(
            bridge,
            """{"id":8,"method":"git/stash-write","params":{"action":"delete-all","reference":"stash@{0}"}}""");
        Assert.IsTrue(unknownAction.TryGetProperty("error", out JsonElement unknownActionError), unknownAction.GetRawText());
        StringAssert.Contains(unknownActionError.GetString(), "delete-all");
    }

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string request)
    {
        JsonElement response = await InvokeRawAsync(bridge, request);
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }

    private static async Task<JsonElement> InvokeRawAsync(ShellBridge bridge, string request)
    {
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    /// <summary>造一个带单个 Stash 的仓库（Stash 里有一个已跟踪文件与一个未跟踪文件）。</summary>
    private static async Task CreateRepositoryWithStashAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "stashed.txt", "基线\n", "test: base");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "stashed.txt"), "已改动\n");
        GitCall stash = await GitTestEnvironment.RunRawAsync(
            runtime, repositoryPath, "stash", "push", "--include-untracked", "-m", "测试 Stash");
        Assert.IsTrue(stash.Success, stash.ErrorMessage);
    }
}
