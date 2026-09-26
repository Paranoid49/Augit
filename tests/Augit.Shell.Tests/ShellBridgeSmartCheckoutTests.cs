using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/checkout-smart`（Smart Checkout）。权威 `GitBrancher.checkout` 的 smart 分支：
/// 写入被本地改动挡住时先**临时 stash**、检出目标分支、再**恢复**改动
/// （`plugins/git4idea/backend/src/branch/GitCheckoutOperation.java:367-395,505-524`
/// 的 `smartCheckoutOrNotify` → `smartCheckout()` → `GitPreservingProcess`）；
/// 恢复失败即冲突会话，界面据此说明"改动仍在、可继续"（权威对话框的三个选择见
/// `GitSmartOperationDialog.java:36-125`，Augit 只实现 Smart 与取消，Force Checkout 登记为差异）。
/// </summary>
[TestClass]
public sealed class ShellBridgeSmartCheckoutTests
{
    [TestMethod]
    public async Task 干净工作区直接切换且不留下临时stash()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
        await WriteAsync(temporary.FullPath, "feature.txt", "feature\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "feature.txt");
        await CommitAsync(runtime, temporary.FullPath, "feat: 目标分支新增文件");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", baseBranch);
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement result = await InvokeAsync(bridge, SmartRequest(1, "feature"));

        Assert.IsTrue(result.GetProperty("available").GetBoolean(), result.GetRawText());
        Assert.IsTrue(result.GetProperty("switched").GetBoolean(), result.GetRawText());
        Assert.IsTrue(result.GetProperty("ok").GetBoolean(), result.GetRawText());
        Assert.IsFalse(result.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String,
            "成功时不应有失败原因：" + result.GetRawText());
        Assert.AreEqual("feature", await CurrentBranchAsync(runtime, temporary.FullPath));
        Assert.AreEqual(0, await StashCountAsync(bridge));
    }

    [TestMethod]
    public async Task 本地改动先暂存再恢复且临时stash被删除()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
        await WriteAsync(temporary.FullPath, "feature-only.txt", "feature\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "feature-only.txt");
        await CommitAsync(runtime, temporary.FullPath, "feat: 目标分支只改自己的文件");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", baseBranch);
        // 本地未提交改动不在目标分支的差异里 ⇒ 恢复不会冲突（"stash → 检出 → 恢复"的顺利路径）。
        await WriteAsync(temporary.FullPath, "base.txt", "本地改动\n");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement result = await InvokeAsync(bridge, SmartRequest(1, "feature"));

        Assert.IsTrue(result.GetProperty("switched").GetBoolean(), result.GetRawText());
        Assert.AreEqual("feature", await CurrentBranchAsync(runtime, temporary.FullPath));
        // 改动被恢复回来，而不是留在 stash 里。
        // Git 在 Windows 上可能按 autocrlf 写回 CRLF：只比较内容，不比较行尾。
        string restored = (await File.ReadAllTextAsync(Path.Combine(temporary.FullPath, "base.txt")))
            .Replace("\r\n", "\n");
        Assert.AreEqual("本地改动\n", restored);
        Assert.AreEqual(0, await StashCountAsync(bridge), "顺利完成后临时 stash 必须被删除。");
    }

    [TestMethod]
    public async Task 恢复冲突时保留临时stash并可从会话继续()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
        await WriteAsync(temporary.FullPath, "base.txt", "目标分支\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "base.txt");
        await CommitAsync(runtime, temporary.FullPath, "feat: 目标分支改同一处");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", baseBranch);
        // 同一个文件的同一处也有未提交改动 ⇒ 恢复必然冲突。
        await WriteAsync(temporary.FullPath, "base.txt", "本地改动\n");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement result = await InvokeAsync(bridge, SmartRequest(1, "feature"));

        Assert.IsFalse(result.GetProperty("switched").GetBoolean(), result.GetRawText());
        JsonElement session = result.GetProperty("session");
        Assert.AreEqual("SmartCheckout", session.GetProperty("kind").GetString());
        Assert.IsTrue(session.GetProperty("hasConflicts").GetBoolean(), result.GetRawText());
        Assert.IsTrue(session.GetProperty("conflicts").EnumerateArray()
            .Any(file => file.GetProperty("path").GetString() == "base.txt"), result.GetRawText());
        // 临时 stash 保留下来，用户不会因为这次切换丢改动。
        JsonElement stashes = await InvokeAsync(bridge, "{\"id\":2,\"method\":\"git/stashes\",\"params\":{}}");
        JsonElement[] entries = stashes.GetProperty("stashes").EnumerateArray().ToArray();
        Assert.HasCount(1, entries);
        StringAssert.Contains(entries[0].GetProperty("message").GetString(), "Augit Smart Checkout");

        // 冲突未解决时"继续"必须被拒（`CanContinue` 要求无冲突），会话仍在。
        JsonElement blocked = await InvokeAsync(
            bridge,
            "{\"id\":3,\"method\":\"git/operation-action\",\"params\":{\"action\":\"continue\"}}");
        Assert.IsFalse(blocked.GetProperty("ok").GetBoolean(), blocked.GetRawText());
        Assert.IsTrue(blocked.GetProperty("session").GetProperty("supportsContinue").GetBoolean());

        // 解决冲突后"继续"（权威 `GitPreservingProcess` 的收尾）删除临时 stash，会话结束。
        await WriteAsync(temporary.FullPath, "base.txt", "解决后的内容\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "base.txt");
        JsonElement ready = await InvokeAsync(bridge, "{\"id\":4,\"method\":\"git/operation\",\"params\":{}}");
        Assert.IsTrue(ready.GetProperty("session").GetProperty("canContinue").GetBoolean(), ready.GetRawText());
        JsonElement continued = await InvokeAsync(
            bridge,
            "{\"id\":5,\"method\":\"git/operation-action\",\"params\":{\"action\":\"continue\"}}");
        Assert.IsTrue(continued.GetProperty("ok").GetBoolean(), continued.GetRawText());
        Assert.AreEqual(0, await StashCountAsync(bridge), "续做完成后临时 stash 必须被删除。");
    }

    [TestMethod]
    public async Task 已跟踪改动挡住检出时回传受影响文件()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
        await WriteAsync(temporary.FullPath, "base.txt", "目标分支\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "base.txt");
        await CommitAsync(runtime, temporary.FullPath, "feat: 目标分支改同一处");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", baseBranch);
        await WriteAsync(temporary.FullPath, "base.txt", "本地改动\n");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement result = await InvokeAsync(bridge, CheckoutRequest(1, "feature"));

        Assert.IsFalse(result.GetProperty("switched").GetBoolean(), result.GetRawText());
        // 权威 `GitCheckoutOperation` 的 Smart Checkout 前提就是这条错误，界面据此决定是否给入口。
        Assert.IsTrue(result.GetProperty("overwriteRisk").GetBoolean(), result.GetRawText());
        Assert.AreEqual(
            "base.txt",
            string.Join(",", result.GetProperty("overwritePaths").EnumerateArray().Select(item => item.GetString())));
    }

    [TestMethod]
    public async Task 未跟踪文件挡住检出时也回传受影响文件()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string baseBranch = await CurrentBranchAsync(runtime, temporary.FullPath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature");
        await WriteAsync(temporary.FullPath, "u.txt", "目标分支的已跟踪内容\n");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "add", "--", "u.txt");
        await CommitAsync(runtime, temporary.FullPath, "feat: 目标分支新增 u.txt");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", baseBranch);
        // 同名但**未跟踪**的本地文件：git 的提示换了一种句式（"The following untracked working tree files…"）。
        await WriteAsync(temporary.FullPath, "u.txt", "本地未跟踪\n");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement result = await InvokeAsync(bridge, CheckoutRequest(1, "feature"));

        Assert.IsTrue(result.GetProperty("overwriteRisk").GetBoolean(), result.GetRawText());
        Assert.AreEqual(
            "u.txt",
            string.Join(",", result.GetProperty("overwritePaths").EnumerateArray().Select(item => item.GetString())));
    }

    private static string CheckoutRequest(long id, string name)
        => "{\"id\":" + id + ",\"method\":\"git/checkout\",\"params\":{\"name\":"
            + JsonSerializer.Serialize(name) + ",\"kind\":\"branch\"}}";

    private static string SmartRequest(long id, string name)
        => "{\"id\":" + id + ",\"method\":\"git/checkout-smart\",\"params\":{\"name\":"
            + JsonSerializer.Serialize(name) + "}}";

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string request)
    {
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }

    private static async Task<int> StashCountAsync(ShellBridge bridge)
    {
        JsonElement stashes = await InvokeAsync(bridge, "{\"id\":99,\"method\":\"git/stashes\",\"params\":{}}");
        return stashes.GetProperty("stashes").GetArrayLength();
    }

    private static async Task<string> CurrentBranchAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitCall call = await GitTestEnvironment.RunRawAsync(runtime, repositoryPath, "branch", "--show-current");
        Assert.IsTrue(call.Success, call.ErrorMessage);
        return call.StandardOutput.Trim();
    }

    private static Task WriteAsync(string repositoryPath, string relativePath, string content)
        => File.WriteAllTextAsync(Path.Combine(repositoryPath, relativePath), content);

    private static Task CommitAsync(GitRuntimeInfo runtime, string repositoryPath, string message)
        => GitTestEnvironment.RunAsync(
            runtime,
            repositoryPath,
            "-c", "user.name=Augit Tests", "-c", "user.email=augit-tests@example.invalid",
            "commit", "-m", message);

    private static async Task CreateRepositoryAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "base.txt", "基线\n", "test: base");
    }
}
