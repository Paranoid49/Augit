using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/branch` 的 `delete` 与 `git/tag` 的宿主契约（规格 §5.2 分支与标签）。
///
/// 删除分支按权威 `GitDeleteBranchOperation` 的口径分两步：先用 `force=false`
/// （`git branch -d` 会拒绝未完全合并的分支并给出原因，界面据此说明影响），
/// 用户确认丢弃后再 `force=true`；标签支持创建（指定或不指定目标版本、可选附注）与删除。
/// </summary>
[TestClass]
public sealed class ShellBridgeBranchTagTests
{
    [TestMethod]
    public async Task 未完全合并的分支需要强制才能删除()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        // feature 分支上多一个提交，且不合并回主分支 ⇒ 未完全合并。
        GitCall created = await GitTestEnvironment.RunRawAsync(
            runtime, temporary.FullPath, "switch", "-c", "feature/x");
        Assert.IsTrue(created.Success, created.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "extra.txt", "额外\n", "test: extra");
        GitCall back = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "switch", "-");
        Assert.IsTrue(back.Success, back.ErrorMessage);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement refused = await InvokeAsync(bridge, BranchRequest(1, "delete", "feature/x", force: false));
        Assert.IsFalse(refused.GetProperty("changed").GetBoolean(), refused.GetRawText());
        StringAssert.Contains(refused.GetProperty("reason").GetString(), "not fully merged");

        JsonElement forced = await InvokeAsync(bridge, BranchRequest(2, "delete", "feature/x", force: true));
        Assert.IsTrue(forced.GetProperty("changed").GetBoolean(), forced.GetRawText());
        GitCall branches = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "branch", "--list");
        Assert.DoesNotContain("feature/x", branches.StandardOutput);
    }

    [TestMethod]
    public async Task 已合并的分支直接删除且无需强制()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        GitCall created = await GitTestEnvironment.RunRawAsync(
            runtime, temporary.FullPath, "branch", "merged/y");
        Assert.IsTrue(created.Success, created.ErrorMessage);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement deleted = await InvokeAsync(bridge, BranchRequest(1, "delete", "merged/y", force: false));
        Assert.IsTrue(deleted.GetProperty("changed").GetBoolean(), deleted.GetRawText());
    }

    [TestMethod]
    public async Task 标签可创建可删除()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement lightweight = await InvokeAsync(bridge, TagRequest(1, "create", "v0.1"));
        Assert.IsTrue(lightweight.GetProperty("changed").GetBoolean(), lightweight.GetRawText());
        JsonElement annotated = await InvokeAsync(bridge, TagRequest(2, "create", "v0.2", message: "第二个版本"));
        Assert.IsTrue(annotated.GetProperty("changed").GetBoolean(), annotated.GetRawText());

        GitCall listed = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "tag", "--list");
        StringAssert.Contains(listed.StandardOutput, "v0.1");
        StringAssert.Contains(listed.StandardOutput, "v0.2");

        JsonElement removed = await InvokeAsync(bridge, TagRequest(3, "delete", "v0.1"));
        Assert.IsTrue(removed.GetProperty("changed").GetBoolean(), removed.GetRawText());
        GitCall after = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "tag", "--list");
        Assert.DoesNotContain("v0.1", after.StandardOutput);
        StringAssert.Contains(after.StandardOutput, "v0.2");
    }

    [TestMethod]
    public async Task 从指定提交新建分支()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        GitCall baseCommit = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "rev-parse", "HEAD");
        Assert.IsTrue(baseCommit.Success, baseCommit.ErrorMessage);
        string startPoint = baseCommit.StandardOutput.Trim();
        // 让 HEAD 前进，这样"从指定提交建分支"与"从 HEAD 建分支"结果不同、可区分。
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "second.txt", "第二个\n", "test: second");

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement created = await InvokeAsync(bridge, BranchRequest(1, "create", "from/base", force: false, startPoint));
        Assert.IsTrue(created.GetProperty("changed").GetBoolean(), created.GetRawText());
        GitCall branchCommit = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "rev-parse", "from/base");
        Assert.IsTrue(branchCommit.Success, branchCommit.ErrorMessage);
        Assert.AreEqual(startPoint, branchCommit.StandardOutput.Trim());

        // 起点不存在时必须失败并给出原因（界面据此提示，不静默从 HEAD 建）。
        JsonElement bogus = await InvokeAsync(bridge, BranchRequest(2, "create", "from/bogus", force: false, "deadbeefdeadbeef"));
        Assert.IsFalse(bogus.GetProperty("changed").GetBoolean(), bogus.GetRawText());
        StringAssert.Contains(bogus.GetProperty("reason").GetString(), "起点不存在");
    }

    [TestMethod]
    public async Task 标签可以打在指定提交上()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        GitCall baseCommit = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "rev-parse", "HEAD");
        Assert.IsTrue(baseCommit.Success, baseCommit.ErrorMessage);
        string startPoint = baseCommit.StandardOutput.Trim();
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "second.txt", "第二个\n", "test: second");

        using ShellBridge bridge = new(temporary.FullPath);
        // 日志右键菜单的「新建标签…」用的就是这条：`GitCreateTagAction` 把标签打在**选中的提交**上。
        JsonElement created = await InvokeAsync(bridge, TagRequest(1, "create", "v0.3", target: startPoint));
        Assert.IsTrue(created.GetProperty("changed").GetBoolean(), created.GetRawText());
        GitCall tagged = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "rev-parse", "v0.3^{commit}");
        Assert.IsTrue(tagged.Success, tagged.ErrorMessage);
        Assert.AreEqual(startPoint, tagged.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task 多选删除分支时逐个删并把被拒的分支带回界面()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        // merged/y 与基线同点（可安全删除）；feature/x 多一条未合并提交（-d 必须被拒）。
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "branch", "merged/y");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-c", "feature/x");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "extra.txt", "额外\n", "test: extra");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "switch", "-");

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement first = await InvokeAsync(bridge, NamesRequest(1, ["merged/y", "feature/x"], force: false));
        // 权威 `DeleteBranchAction.delete()` 对选中集逐个处理：能删的删掉，被拒的连同原因报回来。
        Assert.IsTrue(first.GetProperty("changed").GetBoolean(), first.GetRawText());
        Assert.HasCount(1, first.GetProperty("deleted").EnumerateArray().ToArray());
        Assert.AreEqual("merged/y", first.GetProperty("deleted")[0].GetString());
        JsonElement refused = first.GetProperty("refused").EnumerateArray().Single();
        Assert.AreEqual("feature/x", refused.GetProperty("name").GetString());
        StringAssert.Contains(refused.GetProperty("reason").GetString(), "not fully merged");
        GitCall remaining = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "branch", "--list");
        Assert.DoesNotContain("merged/y", remaining.StandardOutput);

        JsonElement forced = await InvokeAsync(bridge, NamesRequest(2, ["feature/x"], force: true));
        Assert.IsTrue(forced.GetProperty("changed").GetBoolean(), forced.GetRawText());
        GitCall afterForce = await GitTestEnvironment.RunRawAsync(runtime, temporary.FullPath, "branch", "--list");
        Assert.DoesNotContain("feature/x", afterForce.StandardOutput);
    }

    private static string NamesRequest(long id, string[] names, bool force)
    {
        return "{\"id\":" + id + ",\"method\":\"git/branch\",\"params\":{\"action\":\"delete\",\"names\":"
            + JsonSerializer.Serialize(names) + ",\"force\":" + (force ? "true" : "false") + "}}";
    }

    private static string BranchRequest(long id, string action, string name, bool force, string? startPoint = null)
    {
        string paramsJson = "{\"action\":\"" + action
            + "\",\"name\":" + JsonSerializer.Serialize(name) + ",\"force\":" + (force ? "true" : "false");
        if (startPoint is not null) paramsJson += ",\"startPoint\":" + JsonSerializer.Serialize(startPoint);
        return "{\"id\":" + id + ",\"method\":\"git/branch\",\"params\":" + paramsJson + "}}";
    }

    private static string TagRequest(long id, string action, string name, string? message = null, string? target = null)
    {
        string paramsJson = "{\"action\":\"" + action + "\",\"name\":" + JsonSerializer.Serialize(name);
        if (message is not null) paramsJson += ",\"message\":" + JsonSerializer.Serialize(message);
        if (target is not null) paramsJson += ",\"target\":" + JsonSerializer.Serialize(target);
        return "{\"id\":" + id + ",\"method\":\"git/tag\",\"params\":" + paramsJson + "}}";
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
    }
}
