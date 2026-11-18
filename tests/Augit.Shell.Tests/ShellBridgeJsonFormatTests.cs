using System.Text.Json;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellBridgeJsonFormatTests
{
    [TestMethod]
    public async Task 格式化有效Json并保留属性顺序()
    {
        using TemporaryDirectory temporary = new();
        using ShellBridge bridge = new(temporary.FullPath, updatesEnabled: false);

        string response = await bridge.HandleAsync(
            """{"id":1,"method":"json/format","params":{"text":"{\"b\":1,\"a\":[true,null]}"}}""",
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement result = document.RootElement.GetProperty("result");
        Assert.IsTrue(result.GetProperty("valid").GetBoolean());
        string formatted = result.GetProperty("formatted").GetString()!;
        Assert.Contains("\"b\": 1", formatted);
        Assert.Contains("\"a\": [", formatted);
        Assert.IsLessThan(formatted.IndexOf("\"a\"", StringComparison.Ordinal), formatted.IndexOf("\"b\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task 超过5Mb的输入在宿主侧也被拒绝()
    {
        using TemporaryDirectory temporary = new();
        using ShellBridge bridge = new(temporary.FullPath, updatesEnabled: false);
        string request = JsonSerializer.Serialize(new
        {
            id = 1,
            method = "json/format",
            @params = new { text = new string(' ', 5 * 1024 * 1024 + 1) },
        });

        string response = await bridge.HandleAsync(request, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(response);
        string error = document.RootElement.GetProperty("error").GetString()!;
        Assert.Contains("5 MB", error);
    }

    [TestMethod]
    public async Task 非法Json返回从1开始的行列()
    {
        using TemporaryDirectory temporary = new();
        using ShellBridge bridge = new(temporary.FullPath, updatesEnabled: false);

        string response = await bridge.HandleAsync(
            """{"id":1,"method":"json/format","params":{"text":"{\n  \"a\":\n}"}}""",
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement result = document.RootElement.GetProperty("result");
        Assert.IsFalse(result.GetProperty("valid").GetBoolean());
        Assert.IsGreaterThanOrEqualTo(1L, result.GetProperty("errorLine").GetInt64());
        Assert.IsGreaterThanOrEqualTo(1L, result.GetProperty("errorColumn").GetInt64());
    }
}
