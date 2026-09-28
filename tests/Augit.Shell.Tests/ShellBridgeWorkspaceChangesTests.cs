using System.Text.Json;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `workspace/changes` 的事件合并（规格 §9.2「多个文件系统与 `.git` 事件必须合并」）。
///
/// 两个观察器各有 50 毫秒的合并窗口（`WorkspaceFileWatcher.MergeDelay`／`GitMetadataWatcher.MergeDelay`），
/// 宿主把它们的批次分别累积在 `_pendingWorkspaceChanges` 与 `_pendingGitMetadataChange` 里，
/// **一次** `workspace/changes` 读取同时取走两侧 —— 因此多次事件（哪怕跨越多个合并窗口）
/// 只会变成一次查询；读取即消费，紧接着再读是空的。
/// </summary>
[TestClass]
public sealed class ShellBridgeWorkspaceChangesTests
{
    [TestMethod]
    public async Task 文件系统与git元数据事件合并成一次查询且只消费一次()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime)
            .InitializeAsync(temporary.FullPath);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.name", "Augit Tests");
        await GitTestEnvironment.RunAsync(runtime, temporary.FullPath, "config", "user.email", "augit-tests@example.invalid");
        await GitTestEnvironment.CommitFileAsync(runtime, temporary.FullPath, "base.txt", "基线\n", "test: base");
        using ShellBridge bridge = new(temporary.FullPath);

        // ① 先读一次状态：仓库确认可用后宿主才建立 `.git` 元数据监视（`EnsureGitWatcher`）。
        JsonElement status = await InvokeAsync(bridge, "{\"id\":1,\"method\":\"git/status\",\"params\":{}}");
        Assert.IsTrue(status.GetProperty("available").GetBoolean(), status.GetRawText());
        // ② 再读一次变化：文件系统观察器是**惰性建立**的（首次读取时创建），必须早于写入。
        JsonElement primed = await InvokeAsync(bridge, "{\"id\":2,\"method\":\"workspace/changes\",\"params\":{}}");
        Assert.AreEqual(0, primed.GetProperty("files").GetArrayLength(), "初始批次应为空。");
        await Task.Delay(200);

        // ③ 文件系统事件（三个文件同时写）与 `.git` 元数据事件（改写 HEAD）紧挨着发生。
        string[] changed = ["a.txt", "b.txt", "c.txt"];
        Task[] writes = [.. changed.Select(name => File.WriteAllTextAsync(temporary.GetPath(name), "变化"))];
        await Task.WhenAll(writes);
        string headPath = Path.Combine(initialized.Repository!.GitDirectory!, "HEAD");
        await File.AppendAllTextAsync(headPath, Environment.NewLine);
        // 等两个观察器各自越过合并窗口：即使它们分多次 flush，下面**一次**读取也要全部取走。
        await Task.Delay(600);

        JsonElement merged = await InvokeAsync(bridge, "{\"id\":3,\"method\":\"workspace/changes\",\"params\":{}}");
        Assert.IsTrue(merged.GetProperty("gitMetadata").GetBoolean(), "一次读取应同时带上 `.git` 元数据变化。");
        List<string> files = [.. merged.GetProperty("files").EnumerateArray().Select(item => item.GetString()!)];
        Assert.HasCount(changed.Length, files);
        foreach (string name in changed)
        {
            Assert.IsTrue(
                files.Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)),
                $"合并批次里缺少 {name}：{string.Join(',', files)}");
        }

        // ④ 读取即消费：紧接着的第二次读取必须是空的（同一批不会变成两次查询）。
        JsonElement consumed = await InvokeAsync(bridge, "{\"id\":4,\"method\":\"workspace/changes\",\"params\":{}}");
        Assert.AreEqual(0, consumed.GetProperty("files").GetArrayLength());
        Assert.IsFalse(consumed.GetProperty("gitMetadata").GetBoolean());
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
