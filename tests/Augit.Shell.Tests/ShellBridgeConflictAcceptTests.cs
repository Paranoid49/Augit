using System.Diagnostics;
using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/conflict-accept` 的宿主契约（规格 §7.14 最后一条：二进制、非法 UTF-8 与超限文件
/// 只能整侧接受）。这里用**真实 Git 仓库**验证行为，而不是只验证页面发出去了请求：
/// 网页层依赖 `available`/`reason` 两个字段决定是否离开解决器，字段语义错了只会静默出错。
/// </summary>
[TestClass]
public sealed class ShellBridgeConflictAcceptTests
{
    [TestMethod]
    public async Task 整侧接受用所选一侧覆盖文件并标记已解决()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        await CreateMergeConflictAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement result = await InvokeAsync(
            bridge,
            """{"id":1,"method":"git/conflict-accept","params":{"path":"conflict.txt","side":"theirs"}}""");

        Assert.AreEqual("合入内容", await File.ReadAllTextAsync(temporary.GetPath("conflict.txt")), result.GetRawText());
        Assert.IsTrue(result.GetProperty("available").GetBoolean(), result.GetRawText());
        Assert.IsFalse(result.GetProperty("hasConflicts").GetBoolean(), result.GetRawText());
        // 已暂存：工作区里不再有未合并条目，冲突会话也随之结束。
        GitCall unmerged = await RunGitAsync(temporary.FullPath, "diff", "--name-only", "--diff-filter=U");
        Assert.AreEqual(string.Empty, unmerged.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task 整侧接受另一侧得到另一侧内容()
    {
        // 与上一条配对：内容确实跟着 side 走，而不是恰好两边相同时的假通过。
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        await CreateMergeConflictAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        JsonElement result = await InvokeAsync(
            bridge,
            """{"id":2,"method":"git/conflict-accept","params":{"path":"conflict.txt","side":"yours"}}""");

        Assert.IsTrue(result.GetProperty("available").GetBoolean(), result.GetRawText());
        Assert.AreEqual("当前内容", await File.ReadAllTextAsync(temporary.GetPath("conflict.txt")), result.GetRawText());
    }

    [TestMethod]
    public async Task 整侧接受的参数与侧别非法时回到错误通道()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        await CreateMergeConflictAsync(runtime, temporary.FullPath);

        using ShellBridge bridge = new(temporary.FullPath);
        // 网页层只认顶层 error（bridge.js），放进 result 里会被当成"成功但没有原因"。
        JsonElement missingSide = await InvokeRawAsync(
            bridge,
            """{"id":3,"method":"git/conflict-accept","params":{"path":"conflict.txt"}}""");
        Assert.IsTrue(missingSide.TryGetProperty("error", out JsonElement missingSideError), missingSide.GetRawText());
        StringAssert.Contains(missingSideError.GetString(), "side");

        JsonElement unknownSide = await InvokeRawAsync(
            bridge,
            """{"id":4,"method":"git/conflict-accept","params":{"path":"conflict.txt","side":"ours"}}""");
        Assert.IsTrue(unknownSide.TryGetProperty("error", out JsonElement unknownSideError), unknownSide.GetRawText());
        StringAssert.Contains(unknownSideError.GetString(), "ours");

        // 参数非法时不得改写工作区。
        string content = await File.ReadAllTextAsync(temporary.GetPath("conflict.txt"));
        StringAssert.Contains(content, "<<<<<<<");
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

    private static async Task<GitCall> RunGitAsync(string repositoryPath, params string[] arguments)
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        return await GitTestEnvironment.RunRawAsync(runtime, repositoryPath, arguments);
    }

    /// <summary>造出冲突：基线提交 → incoming 分支改文件 → 主分支改同一行 → 合并失败。</summary>
    private static async Task CreateMergeConflictAsync(GitRuntimeInfo runtime, string repositoryPath)
    {
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(repositoryPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "conflict.txt", "基线内容", "test: base");
        string main = (await RunGitAsync(repositoryPath, "rev-parse", "--abbrev-ref", "HEAD"))
            .StandardOutput.Trim();
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "checkout", "-b", "incoming");
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "conflict.txt", "合入内容", "test: incoming");
        await GitTestEnvironment.RunAsync(runtime, repositoryPath, "checkout", main);
        await GitTestEnvironment.CommitFileAsync(runtime, repositoryPath, "conflict.txt", "当前内容", "test: current");
        GitCall merge = await RunGitAsync(repositoryPath, "merge", "incoming");
        Assert.IsFalse(merge.Success, "测试仓库必须形成冲突：" + merge.ErrorMessage);
    }
}
