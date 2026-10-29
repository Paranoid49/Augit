using System.Text.Json;
using Augit.Shell;

namespace Augit.Shell.Tests;

/// <summary>终端会话标识只接受有限字符，避免网页把任意字符串作为宿主字典键。</summary>
[TestClass]
public sealed class ShellBridgeTerminalSessionIdTests
{
    [TestMethod]
    public void 合法会话标识原样保留()
    {
        Assert.AreEqual("terminal-2", Read("terminal-2"));
        Assert.AreEqual("local_A1", Read("local_A1"));
    }

    [TestMethod]
    public void 缺失或非法标识回退到main()
    {
        Assert.AreEqual("main", Read(null));
        Assert.AreEqual("main", Read("has space"));
        Assert.AreEqual("main", Read("../escape"));
        Assert.AreEqual("main", Read(new string('a', 65)));
    }

    private static string Read(string? id)
    {
        string json = id is null ? "{}" : JsonSerializer.Serialize(new { sessionId = id });
        using JsonDocument document = JsonDocument.Parse(json);
        return ShellBridge.ReadTerminalSessionId(document.RootElement);
    }
}