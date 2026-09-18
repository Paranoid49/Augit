using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/worktree-removal` / `git/worktree-remove` 的宿主契约（规格 §5.3/§10.4）：
/// 只有"干净且没有运行中的内置终端会话"的 Worktree 才能安全移除，
/// 界面据此禁用按钮并给出原因，因此三个字段的语义必须准确。
/// </summary>
[TestClass]
public sealed class ShellBridgeWorktreeTests
{
    [TestMethod]
    public async Task 干净Worktree可移除脏Worktree给出原因()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);
        string worktreePath = temporary.GetPath("linked");
        await CreateWorktreeAsync(runtime, temporary.FullPath, worktreePath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement clean = await InvokeAsync(bridge, PathRequest(1, "git/worktree-removal", worktreePath));
        Assert.IsTrue(clean.GetProperty("available").GetBoolean(), clean.GetRawText());
        Assert.IsTrue(clean.GetProperty("canRemove").GetBoolean(), clean.GetRawText());
        Assert.IsTrue(clean.GetProperty("isClean").GetBoolean(), clean.GetRawText());
        Assert.IsFalse(clean.GetProperty("hasActiveTerminal").GetBoolean(), clean.GetRawText());

        // 脏 Worktree：不可移除，并且原因要能直接给用户看。
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "dirty.txt"), "未跟踪\n");
        JsonElement dirty = await InvokeAsync(bridge, PathRequest(2, "git/worktree-removal", worktreePath));
        Assert.IsFalse(dirty.GetProperty("canRemove").GetBoolean(), dirty.GetRawText());
        Assert.IsFalse(dirty.GetProperty("isClean").GetBoolean(), dirty.GetRawText());
        StringAssert.Contains(dirty.GetProperty("reason").GetString(), "本地改动");

        // 移除尝试也要被守卫挡住，并给出原因（不是静默失败）。
        JsonElement refused = await InvokeAsync(bridge, PathRequest(3, "git/worktree-remove", worktreePath));
        Assert.IsFalse(refused.GetProperty("ok").GetBoolean(), refused.GetRawText());
        StringAssert.Contains(refused.GetProperty("reason").GetString(), "不能安全移除");

        // 清理后可以移除：列表随之少一条。
        File.Delete(Path.Combine(worktreePath, "dirty.txt"));
        GitCall status = await GitTestEnvironment.RunRawAsync(runtime, worktreePath, "status", "--porcelain");
        Assert.AreEqual(string.Empty, status.StandardOutput.Trim());
        JsonElement removed = await InvokeAsync(bridge, PathRequest(4, "git/worktree-remove", worktreePath));
        Assert.IsTrue(removed.GetProperty("ok").GetBoolean(), removed.GetRawText());
        Assert.AreEqual(1, removed.GetProperty("worktrees").GetProperty("worktrees").GetArrayLength());
    }

    [TestMethod]
    public async Task 当前窗口使用的Worktree不可移除()
    {
        using TemporaryDirectory temporary = new();
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        await CreateRepositoryAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement readiness = await InvokeAsync(bridge, PathRequest(5, "git/worktree-removal", temporary.FullPath));
        Assert.IsFalse(readiness.GetProperty("canRemove").GetBoolean(), readiness.GetRawText());
        StringAssert.Contains(readiness.GetProperty("reason").GetString(), "当前窗口");

        JsonElement refused = await InvokeAsync(bridge, PathRequest(6, "git/worktree-remove", temporary.FullPath));
        Assert.IsFalse(refused.GetProperty("ok").GetBoolean(), refused.GetRawText());

        JsonElement missing = await InvokeRawAsync(
            bridge,
            """{"id":7,"method":"git/worktree-removal","params":{}}""");
        Assert.IsTrue(missing.TryGetProperty("error", out JsonElement error), missing.GetRawText());
        StringAssert.Contains(error.GetString(), "path");
    }

    /// <summary>带路径参数请求体；拼接构造避免内插原始字符串里 JSON 大括号的歧义。</summary>
    private static string PathRequest(long id, string method, string path)
    {
        return "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":{\"path\":"
            + JsonSerializer.Serialize(path) + "}}";
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

    private static async Task CreateRepositoryAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "base.txt", "基线\n", "test: base");
    }

    private static async Task CreateWorktreeAsync(GitRuntimeInfo runtime, string repositoryPath, string worktreePath)
    {
        GitCall created = await GitTestEnvironment.RunRawAsync(
            runtime, repositoryPath, "worktree", "add", "-b", "feature/wt", worktreePath);
        Assert.IsTrue(created.Success, created.ErrorMessage);
        // worktree add 会检出分支：干净状态是"可移除"的前置条件。
        GitCall status = await GitTestEnvironment.RunRawAsync(runtime, worktreePath, "status", "--porcelain");
        Assert.AreEqual(string.Empty, status.StandardOutput.Trim(), status.StandardOutput);
    }
}
