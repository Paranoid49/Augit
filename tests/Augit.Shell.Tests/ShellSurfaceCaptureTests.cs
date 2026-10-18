using System.Text.Json;
using Augit.Shell;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSurfaceCaptureTests
{
    // 单个像素PNG，用于验证协议返回字节未经重采样或重新编码。
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=";

    [TestMethod]
    public async Task 使用当前表面快速无损捕获且原样返回PNG()
    {
        int calls = 0;
        byte[] actual = await ShellSurfaceCapture.CaptureAsync((method, parameters) =>
        {
            calls++;
            Assert.AreEqual("Page.captureScreenshot", method);
            using JsonDocument options = JsonDocument.Parse(parameters);
            Assert.AreEqual("png", options.RootElement.GetProperty("format").GetString());
            Assert.IsTrue(options.RootElement.GetProperty("fromSurface").GetBoolean());
            Assert.IsFalse(options.RootElement.GetProperty("captureBeyondViewport").GetBoolean());
            Assert.IsTrue(options.RootElement.GetProperty("optimizeForSpeed").GetBoolean());
            return Task.FromResult(JsonSerializer.Serialize(new { data = Pixel }));
        });
        Assert.AreEqual(1, calls);
        CollectionAssert.AreEqual(Convert.FromBase64String(Pixel), actual);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("""{"data":null}""")]
    [DataRow("""{"data":17}""")]
    [DataRow("""{"data":""}""")]
    public async Task 缺失图片不返回空背景(string result)
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ShellSurfaceCapture.CaptureAsync((_, _) => Task.FromResult(result)));
    }

    [TestMethod]
    public async Task 复用捕获每次等待新响应且失败后不复用旧图片()
    {
        TaskCompletionSource<string> response = new();
        Task<byte[]> first = ShellSurfaceCapture.CaptureAsync((_, _) => response.Task);
        Assert.IsFalse(first.IsCompleted);
        response.SetResult(JsonSerializer.Serialize(new { data = Pixel }));
        CollectionAssert.AreEqual(Convert.FromBase64String(Pixel), await first);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ShellSurfaceCapture.CaptureAsync((_, _) => Task.FromResult("{}")));
        byte[] next = await ShellSurfaceCapture.CaptureAsync((_, _) =>
            Task.FromResult(JsonSerializer.Serialize(new { data = Pixel })));
        CollectionAssert.AreEqual(Convert.FromBase64String(Pixel), next);
        Assert.AreNotSame(await first, next);
    }

    [TestMethod]
    public async Task 非PNG与协议失败向上返回而不静默改用旧图()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ShellSurfaceCapture.CaptureAsync((_, _) =>
            Task.FromResult(JsonSerializer.Serialize(new { data = Convert.ToBase64String(new byte[] { 1, 2, 3 }) }))));
        InvalidOperationException failure = new("捕获失败");
        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ShellSurfaceCapture.CaptureAsync((_, _) => Task.FromException<string>(failure)));
        Assert.AreSame(failure, actual);
    }
}
