using System.Text.Json;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class RefreshTraceBridgeTests
{
    [TestMethod]
    public async Task 未启用刷新跟踪时网页回报不写入()
    {
        using TemporaryDirectory workspace = new();
        using ShellBridge bridge = new(workspace.FullPath);

        JsonElement info = await InvokeAsync(bridge, "{\"id\":1,\"method\":\"workspace/info\",\"params\":{}}");
        JsonElement applied = await InvokeAsync(
            bridge,
            "{\"id\":2,\"method\":\"audit/refresh-applied\",\"params\":{\"stage\":\"applied\"}}");

        Assert.IsFalse(info.GetProperty("refreshTrace").GetBoolean());
        Assert.IsFalse(applied.GetProperty("recorded").GetBoolean());
    }

    [TestMethod]
    public async Task 启用刷新跟踪时只接受应用与绘制阶段并在关闭时导出()
    {
        using TemporaryDirectory workspace = new();
        using TemporaryDirectory traceDirectory = new();
        RefreshStageTrace trace = new(traceDirectory.FullPath);
        ShellBridge bridge = new(workspace.FullPath, notify: null, settingsStore: null, refreshTrace: trace);
        try
        {
            JsonElement info = await InvokeAsync(bridge, "{\"id\":1,\"method\":\"workspace/info\",\"params\":{}}");
            Assert.IsTrue(info.GetProperty("refreshTrace").GetBoolean());

            JsonElement applied = await InvokeAsync(
                bridge,
                "{\"id\":2,\"method\":\"audit/refresh-applied\",\"params\":{\"stage\":\"applied\",\"path\":\"a.txt\"}}");
            JsonElement painted = await InvokeAsync(
                bridge,
                "{\"id\":3,\"method\":\"audit/refresh-applied\",\"params\":{\"stage\":\"painted\"}}");
            JsonElement rejected = await InvokeAsync(
                bridge,
                "{\"id\":4,\"method\":\"audit/refresh-applied\",\"params\":{\"stage\":\"other\"}}");

            Assert.IsTrue(applied.GetProperty("recorded").GetBoolean());
            Assert.IsTrue(painted.GetProperty("recorded").GetBoolean());
            Assert.IsFalse(rejected.GetProperty("recorded").GetBoolean());
        }
        finally
        {
            // Dispose 负责把阶段样本落盘；这里必须显式关闭而不是依赖进程退出。
            bridge.Dispose();
        }

        string file = Directory.GetFiles(traceDirectory.FullPath, "refresh-*.json").Single();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
        string[] stages = [.. document.RootElement.GetProperty("samples").EnumerateArray()
            .Select(sample => sample.GetProperty("stage").GetString()!)];
        CollectionAssert.Contains(stages, "page-applied");
        CollectionAssert.Contains(stages, "page-painted");
        CollectionAssert.DoesNotContain(stages, "page-other");
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
