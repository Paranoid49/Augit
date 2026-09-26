using System.Text.Json;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `git/init`（创建 Git 仓库）。权威 `GitInit`
/// （`plugins/git4idea/backend/src/actions/GitInit.java`）：
/// **只有目标已在 Git 下**才用 Yes/No ＋ 警告图标问一次（`:66-74`，文案 `init.warning.already.under.git` 带目标目录），
/// 不是仓库时**没有任何确认**、直接初始化（`:76-78` 还把它放进后台任务，故这里并入写操作通道）。
/// </summary>
[TestClass]
public sealed class ShellBridgeInitTests
{
    [TestMethod]
    public async Task 非仓库目录直接初始化并让缓存失效()
    {
        using TemporaryDirectory temporary = new();
        await GitTestEnvironment.GetRuntimeAsync();
        using ShellBridge bridge = new(temporary.FullPath);

        JsonElement first = await InvokeAsync(bridge, InitRequest(1, temporary.FullPath));

        Assert.IsTrue(first.GetProperty("available").GetBoolean(), first.GetRawText());
        Assert.IsTrue(first.GetProperty("initialized").GetBoolean(), first.GetRawText());
        Assert.IsFalse(first.GetProperty("alreadyUnderGit").GetBoolean(), first.GetRawText());
        Assert.IsFalse(first.TryGetProperty("requiresConfirmation", out _), "非仓库目录不得要求确认。");
        Assert.IsTrue(Directory.Exists(Path.Combine(temporary.FullPath, ".git")), "应生成 .git 元数据。");

        // 宿主此前缓存了"这个目录不是仓库"的解析结果；初始化后必须作废，否则状态接口继续报非仓库。
        JsonElement status = await InvokeAsync(bridge, "{\"id\":2,\"method\":\"git/status\",\"params\":{}}");
        Assert.IsTrue(status.GetProperty("available").GetBoolean(), status.GetRawText());
        Assert.IsTrue(status.GetProperty("isRepository").GetBoolean(), status.GetRawText());
    }

    [TestMethod]
    public async Task 目标已在Git下先要求确认再按确认结果回话()
    {
        using TemporaryDirectory temporary = new();
        await GitTestEnvironment.GetRuntimeAsync();
        using ShellBridge bridge = new(temporary.FullPath);
        await InvokeAsync(bridge, InitRequest(1, temporary.FullPath));

        JsonElement ask = await InvokeAsync(bridge, InitRequest(2, temporary.FullPath));
        Assert.IsTrue(ask.GetProperty("requiresConfirmation").GetBoolean(), ask.GetRawText());
        Assert.IsFalse(ask.GetProperty("initialized").GetBoolean(), ask.GetRawText());

        JsonElement confirmed = await InvokeAsync(bridge, InitRequest(3, temporary.FullPath, confirm: true));
        Assert.IsTrue(confirmed.GetProperty("initialized").GetBoolean(), confirmed.GetRawText());
        Assert.IsTrue(confirmed.GetProperty("alreadyUnderGit").GetBoolean(), confirmed.GetRawText());
        Assert.IsFalse(confirmed.TryGetProperty("requiresConfirmation", out _));
    }

    [TestMethod]
    public async Task 目标目录不存在时返回可读原因()
    {
        using TemporaryDirectory temporary = new();
        await GitTestEnvironment.GetRuntimeAsync();
        using ShellBridge bridge = new(temporary.FullPath);
        string missing = Path.Combine(temporary.FullPath, "does-not-exist");

        JsonElement result = await InvokeAsync(bridge, InitRequest(1, missing));

        Assert.IsFalse(result.GetProperty("available").GetBoolean(), result.GetRawText());
        StringAssert.Contains(result.GetProperty("reason").GetString(), "不存在");
    }

    private static string InitRequest(long id, string path, bool confirm = false)
        => "{\"id\":" + id + ",\"method\":\"git/init\",\"params\":{\"path\":"
            + JsonSerializer.Serialize(path)
            + (confirm ? ",\"confirm\":true" : string.Empty)
            + "}}";

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string request)
    {
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }
}
