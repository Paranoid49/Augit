using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellBridgeGitStatusTests
{
    [TestMethod]
    public void Rollback复合状态标记工作区副本进入回收站()
    {
        GitChangedFile recreated = new(
            "recreated.txt",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Deleted,
            true,
            true,
            'D',
            '?');
        GitChangedFile stagedDeleted = recreated with { WorkTreeStatus = ' ' };

        Assert.IsTrue(ShellBridge.WasRollbackFileRecycled(recreated));
        Assert.IsFalse(ShellBridge.WasRollbackFileRecycled(stagedDeleted));
    }

    [TestMethod]
    public async Task Git状态桥接为同路径保留原始XY两侧状态()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "recreated.txt", "base\n", "test: base");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "rm", "--cached", "--", "recreated.txt");
        await File.WriteAllTextAsync(temporary.GetPath("recreated.txt"), "recreated\n");
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement status = await InvokeAsync(bridge, "{\"id\":1,\"method\":\"git/status\",\"params\":{}}");

        Assert.IsTrue(status.GetProperty("available").GetBoolean(), status.GetRawText());
        JsonElement file = status.GetProperty("files").EnumerateArray().Single();
        Assert.AreEqual("recreated.txt", file.GetProperty("path").GetString());
        Assert.AreEqual("Changes", file.GetProperty("group").GetString());
        Assert.AreEqual("Deleted", file.GetProperty("kind").GetString());
        Assert.IsTrue(file.GetProperty("staged").GetBoolean());
        Assert.IsTrue(file.GetProperty("workingTree").GetBoolean());
        Assert.AreEqual("D", file.GetProperty("indexStatus").GetString());
        Assert.AreEqual("?", file.GetProperty("workTreeStatus").GetString());
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
