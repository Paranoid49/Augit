using System.Text.Json;
using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellModalRequestTests
{
    [TestMethod]
    public void 捕获遮盖与释放进入窗口通道()
    {
        foreach (string method in new[] { "window/modal-cover", "window/modal-uncover" })
        {
            Assert.IsTrue(ShellModalRequest.TryParse(JsonSerializer.Serialize(new { id = 1, method }), out ShellModalRequest? request));
            Assert.AreEqual(method, request!.Method);
        }
    }
    [TestMethod]
    public void 快照刷新只接受非负整数层级()
    {
        foreach (string depth in new[] { "-1", "1.5", "null", "\"0\"" })
        {
            Assert.IsTrue(ShellModalRequest.TryParse("{\"id\":1,\"method\":\"window/modal-refresh\",\"params\":{\"depth\":" + depth + "}}", out ShellModalRequest? request));
            Assert.ThrowsExactly<ArgumentException>(() => request!.SnapshotDepth());
        }
        Assert.IsTrue(ShellModalRequest.TryParse("{\"id\":1,\"method\":\"window/modal-refresh\",\"params\":{\"depth\":0}}", out ShellModalRequest? valid));
        Assert.AreEqual(0, valid!.SnapshotDepth());
    }
    [TestMethod]
    public void 对象与字符串信封均可读取且参数在解析释放后仍有效()
    {
        const string json = """{"id":7,"method":"window/modal-present","params":{"key":"a","title":"Settings","ancestors":["parent"],"x":100,"y":50,"width":400,"height":200}}""";
        foreach (string envelope in new[] { json, JsonSerializer.Serialize(json) })
        {
            Assert.IsTrue(ShellModalRequest.TryParse(envelope, out ShellModalRequest? request));
            Assert.AreEqual(7L, request!.Id);
            ShellModalPresentation presentation = request.Presentation(2000, 1200, 1.75);
            Assert.AreEqual("a", presentation.Key);
            Assert.IsTrue(presentation.Ancestors.SetEquals(["parent"]));
            Assert.AreEqual(new WindowFrameRect(175, 88, 875, 438), presentation.Clip);
        }
    }

    [TestMethod]
    public void 畸形与非模态请求不进入窗口通道()
    {
        foreach (string json in new[] { "{", "null", "[]", "\"{\"", "{\"method\":3,\"id\":1}",
            "{\"method\":\"window/modal-close\"}", "{\"method\":\"window/modal-state\",\"id\":1.5}",
            "{\"method\":\"workspace/info\",\"id\":1}" })
        {
            Assert.IsFalse(ShellModalRequest.TryParse(json, out _), json);
        }
    }

    [TestMethod]
    public void 无效表单参数返回校验错误而不会泄漏Json访问异常()
    {
        foreach (string parameters in new[] { "null", "{}", "{\"ancestors\":[null]}",
            "{\"key\":\"a\",\"title\":\"Settings\",\"ancestors\":[],\"x\":0,\"y\":0,\"width\":\"400\",\"height\":200}",
            "{\"key\":\"a\",\"title\":\"Settings\",\"ancestors\":[],\"x\":0,\"y\":0,\"width\":400,\"height\":0}" })
        {
            Assert.IsTrue(ShellModalRequest.TryParse("{\"id\":1,\"method\":\"window/modal-present\",\"params\":" + parameters + "}", out ShellModalRequest? request));
            Assert.ThrowsExactly<ArgumentException>(() => request!.Presentation(800, 600, 1));
        }
    }
}
