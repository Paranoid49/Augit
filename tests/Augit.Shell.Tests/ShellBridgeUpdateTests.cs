using System.Text.Json;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellBridgeUpdateTests
{
    [TestMethod]
    public async Task 隔离实例的更新检查不发起网络请求且返回禁用状态()
    {
        using TemporaryDirectory temporary = new();
        using ShellBridge bridge = new(temporary.FullPath, updatesEnabled: false);

        string response = await bridge.HandleAsync(
            """{"id":1,"method":"update/check","params":{}}""",
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement result = document.RootElement.GetProperty("result");
        Assert.IsTrue(result.GetProperty("disabled").GetBoolean());
        Assert.IsFalse(result.GetProperty("available").GetBoolean());
        Assert.AreEqual(Program.CurrentVersion, result.GetProperty("currentVersion").GetString());
    }

    [TestMethod]
    public async Task 未下载更新时应用动作返回错误()
    {
        using TemporaryDirectory temporary = new();
        using ShellBridge bridge = new(temporary.FullPath, updatesEnabled: false);

        string response = await bridge.HandleAsync(
            """{"id":1,"method":"update/apply","params":{}}""",
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(response);
        Assert.IsTrue(document.RootElement.TryGetProperty("error", out JsonElement error));
        Assert.IsTrue(error.GetString()!.Contains("未启用", StringComparison.Ordinal));
    }
}
