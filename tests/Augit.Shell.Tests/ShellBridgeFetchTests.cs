using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/fetch` 的可选 `branch` 参数（分支面板的「更新选中分支」，规格 §5.2）。
///
/// 权威 `UpdateSelectedBranchAction` → `GitBranchActionsUtil.updateBranches()`
/// （`plugins/git4idea/backend/src/ui/branch/GitBranchActionsUtil.kt:62-101`）对**非当前**的
/// 受跟踪本地分支用 refspec `"$remoteBranchName:$localBranchName"` 直接快进该本地分支。
/// 这里验证参数确实走到这条路：带 `branch` 只动该分支、不带 `branch` 仍是整仓获取。
/// </summary>
[TestClass]
public sealed class ShellBridgeFetchTests
{
    [TestMethod]
    public async Task 带branch参数只快进该分支且不带branch仍是整仓获取()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string sourcePath = temporary.GetPath("source");
        string consumerPath = temporary.GetPath("consumer");
        Directory.CreateDirectory(sourcePath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);

        GitRepositoryService repositoryService = new(runtime);
        GitRepositoryOperationResult sourceInitialized = await repositoryService.InitializeAsync(sourcePath);
        Assert.IsTrue(sourceInitialized.IsSuccess, sourceInitialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "base.txt", "基线\n", "test: base");
        GitRemoteService sourceRemoteService = new(runtime);
        Assert.IsTrue(
            (await sourceRemoteService.AddRemoteAsync(sourceInitialized.Repository!, "origin", remotePath)).IsSuccess);
        GitCall defaultBranchCall = await GitTestEnvironment.RunRawAsync(
            runtime,
            sourcePath,
            "branch",
            "--show-current");
        Assert.IsTrue(defaultBranchCall.Success, defaultBranchCall.ErrorMessage);
        string defaultBranch = defaultBranchCall.StandardOutput.Trim();
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "branch", "feature/tracked");
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", defaultBranch)).IsSuccess);
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", "feature/tracked")).IsSuccess);

        GitRepositoryOperationResult cloned = await repositoryService.CloneAsync(remotePath, consumerPath);
        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        await GitTestEnvironment.RunAsync(
            runtime,
            consumerPath,
            "branch",
            "--track",
            "feature/tracked",
            "origin/feature/tracked");
        GitCall currentBeforeCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "HEAD");
        string currentBefore = currentBeforeCall.StandardOutput.Trim();

        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", "feature/tracked");
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "tracked.txt", "远端新增\n", "test: tracked");
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", "feature/tracked")).IsSuccess);
        GitCall sourceTrackedCall = await GitTestEnvironment.RunRawAsync(
            runtime,
            sourcePath,
            "rev-parse",
            "feature/tracked");
        string sourceTrackedHead = sourceTrackedCall.StandardOutput.Trim();

        // 远端还有默认分支上的新提交，用于验证"不带 branch 仍是整仓获取"。
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", defaultBranch);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "main.txt", "默认分支新增\n", "test: main");
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", defaultBranch)).IsSuccess);

        using ShellBridge bridge = new(consumerPath);
        JsonElement updated = await InvokeAsync(bridge, FetchRequest(1, "feature/tracked"));
        GitCall localTrackedCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "feature/tracked");
        GitCall currentAfterCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "HEAD");

        Assert.IsTrue(updated.GetProperty("fetched").GetBoolean(), updated.GetRawText());
        Assert.AreEqual(sourceTrackedHead, localTrackedCall.StandardOutput.Trim());
        Assert.AreEqual(currentBefore, currentAfterCall.StandardOutput.Trim());

        // 不带 branch ⇒ 整仓获取：远端跟踪引用前进，本地分支仍然不动。
        JsonElement whole = await InvokeAsync(bridge, FetchRequest(2, null));
        GitCall trackingCall = await GitTestEnvironment.RunRawAsync(
            runtime,
            consumerPath,
            "rev-parse",
            $"origin/{defaultBranch}");
        GitCall localMainCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "HEAD");

        Assert.IsTrue(whole.GetProperty("fetched").GetBoolean(), whole.GetRawText());
        Assert.AreNotEqual(currentBefore, trackingCall.StandardOutput.Trim());
        Assert.AreEqual(currentBefore, localMainCall.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task 按分支取已检出的当前分支时被Git拒绝且不移动它()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string sourcePath = temporary.GetPath("source");
        string consumerPath = temporary.GetPath("consumer");
        Directory.CreateDirectory(sourcePath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);

        GitRepositoryService repositoryService = new(runtime);
        GitRepositoryOperationResult sourceInitialized = await repositoryService.InitializeAsync(sourcePath);
        Assert.IsTrue(sourceInitialized.IsSuccess, sourceInitialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "base.txt", "基线\n", "test: base");
        GitRemoteService sourceRemoteService = new(runtime);
        Assert.IsTrue(
            (await sourceRemoteService.AddRemoteAsync(sourceInitialized.Repository!, "origin", remotePath)).IsSuccess);
        GitCall defaultBranchCall = await GitTestEnvironment.RunRawAsync(
            runtime,
            sourcePath,
            "branch",
            "--show-current");
        string defaultBranch = defaultBranchCall.StandardOutput.Trim();
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", defaultBranch)).IsSuccess);

        GitRepositoryOperationResult cloned = await repositoryService.CloneAsync(remotePath, consumerPath);
        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        GitCall headCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "HEAD");
        string headBefore = headCall.StandardOutput.Trim();

        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "main.txt", "默认分支新增\n", "test: main");
        Assert.IsTrue(
            (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", defaultBranch)).IsSuccess);

        using ShellBridge bridge = new(consumerPath);
        JsonElement refused = await InvokeAsync(bridge, FetchRequest(1, defaultBranch));
        GitCall headAfterCall = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", "HEAD");

        // 当前分支的更新在权威里走"更新方式"（合并/rebase），Augit 尚未提供该通道；
        // 直接按 refspec 写回已检出分支会被 Git 拒绝 —— 必须失败并把原因交给界面，不能静默改动。
        Assert.IsFalse(refused.GetProperty("fetched").GetBoolean(), refused.GetRawText());
        StringAssert.Contains(refused.GetProperty("reason").GetString(), "refusing to fetch into branch");
        Assert.AreEqual(headBefore, headAfterCall.StandardOutput.Trim());
    }

    [TestMethod]
    public async Task 带branches数组时逐个快进选中的每个分支()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        string remotePath = temporary.GetPath("remote.git");
        string sourcePath = temporary.GetPath("source");
        string consumerPath = temporary.GetPath("consumer");
        Directory.CreateDirectory(sourcePath);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "init", "--bare", remotePath);
        GitRepositoryService repositoryService = new(runtime);
        GitRepositoryOperationResult sourceInitialized = await repositoryService.InitializeAsync(sourcePath);
        Assert.IsTrue(sourceInitialized.IsSuccess, sourceInitialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "base.txt", "基线\n", "test: base");
        GitRemoteService sourceRemoteService = new(runtime);
        Assert.IsTrue(
            (await sourceRemoteService.AddRemoteAsync(sourceInitialized.Repository!, "origin", remotePath)).IsSuccess);
        string defaultBranch = (await GitTestEnvironment.RunRawAsync(
            runtime, sourcePath, "branch", "--show-current")).StandardOutput.Trim();
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", "-c", "feature/one");
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "one.txt", "one\n", "test: one");
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", defaultBranch);
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", "-c", "feature/two");
        await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, "two.txt", "two\n", "test: two");
        await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", defaultBranch);
        foreach (string branch in new[] { defaultBranch, "feature/one", "feature/two" })
        {
            Assert.IsTrue(
                (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", branch)).IsSuccess);
        }

        GitRepositoryOperationResult cloned = await repositoryService.CloneAsync(remotePath, consumerPath);
        Assert.IsTrue(cloned.IsSuccess, cloned.ErrorMessage);
        foreach (string branch in new[] { "feature/one", "feature/two" })
        {
            await GitTestEnvironment.RunAsync(runtime, consumerPath, "branch", "--track", branch, $"origin/{branch}");
        }

        // 远端两个分支各自前进一条，再一次性"更新选中分支"（引用树多选）。
        foreach (string branch in new[] { "feature/one", "feature/two" })
        {
            await GitTestEnvironment.RunAsync(runtime, sourcePath, "switch", branch);
            await GitTestEnvironment.CommitFileAsync(runtime, sourcePath, $"{branch.Replace('/', '-')}.txt", "远端\n", $"test: {branch}");
            Assert.IsTrue(
                (await sourceRemoteService.PushAsync(sourceInitialized.Repository!, "origin", branch)).IsSuccess);
        }

        using ShellBridge bridge = new(consumerPath);
        JsonElement updated = await InvokeAsync(bridge, FetchRequest(1, null, ["feature/one", "feature/two"]));

        Assert.IsTrue(updated.GetProperty("fetched").GetBoolean(), updated.GetRawText());
        Assert.HasCount(2, updated.GetProperty("branches").EnumerateArray().ToArray());
        foreach (string branch in new[] { "feature/one", "feature/two" })
        {
            GitCall remote = await GitTestEnvironment.RunRawAsync(runtime, sourcePath, "rev-parse", branch);
            GitCall local = await GitTestEnvironment.RunRawAsync(runtime, consumerPath, "rev-parse", branch);
            Assert.AreEqual(remote.StandardOutput.Trim(), local.StandardOutput.Trim(), $"{branch} 应被快进到远端头");
        }
    }

    private static string FetchRequest(long id, string? branch, string[]? branches = null)
    {
        string paramsJson = branches is not null
            ? "{\"branches\":" + JsonSerializer.Serialize(branches) + "}"
            : branch is null
                ? "{}"
                : "{\"branch\":" + JsonSerializer.Serialize(branch) + "}";
        return "{\"id\":" + id + ",\"method\":\"git/fetch\",\"params\":" + paramsJson + "}";
    }

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string request)
    {
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }
}
