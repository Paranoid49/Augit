using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Augit.App.Tests;

public sealed partial class NativeMarkdownInteractionTests
{
    private const string ImagePixels = "iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAAEklEQVR4nGP4z8DAAMQN/4EUABt0BH3r3fEnAAAAAElFTkSuQmCC";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 慢图片不阻塞正文且成功失败分别更新原位置(bool dark) => RunAsync(async (window, workspace) =>
    {
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "# 图片测试\n\n正文立即可读。\n\n![慢图片](https://markdown-test.invalid/slow.png)\n\n"
            + "![不存在](https://markdown-test.invalid/missing.png)\n\n![损坏](https://markdown-test.invalid/broken.png)\n\n后文继续显示。",
            workspace, Path.Combine(workspace, "sample.md"), dark);
        using MemoryStream pixels = new(Convert.FromBase64String(ImagePixels));
        using MemoryStream invalid = new("不是图片"u8.ToArray());
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreWebView2WebResourceRequestedEventArgs? pending = null;
        CoreWebView2Deferral? deferral = null;
        CoreWebView2Environment? environment = null;
        MarkdownWebViewHost? host = null;
        Task<MarkdownWebViewHost> create = MarkdownWebViewHost.CreateAsync(window.Handle, html, (_, _) => { }, _ => { },
            (web, env) =>
            {
                environment = env;
                // 使用公开接口截住全部图片请求，不启动服务器或访问外网。
                web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                web.WebResourceRequested += (_, args) =>
                {
                    if (args.Request.Uri.EndsWith("/slow.png", StringComparison.Ordinal))
                    {
                        pending = args;
                        deferral = args.GetDeferral();
                        entered.TrySetResult();
                    }
                    else
                    {
                        bool broken = args.Request.Uri.EndsWith("/broken.png", StringComparison.Ordinal);
                        args.Response = env.CreateWebResourceResponse(broken ? invalid : null,
                            broken ? 200 : 404, "Test", "Content-Type: image/png\r\nCache-Control: no-store");
                    }
                };
            });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host = await create.WaitAsync(TimeSpan.FromSeconds(3));
            host.SetBounds(0, 0, 640, 480);
            host.SetVisible(true);
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "后文继续显示");
            Assert.AreEqual("\"loading\"", await ImageStateAsync(host, "慢图片"));
            await WaitForImageStateAsync(host, "不存在", "failure");
            await WaitForImageStateAsync(host, "损坏", "failure");
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "图片加载失败：不存在");
            StringAssert.Contains(await host.ReadPageForTestAsync("document.body.textContent"), "图片加载失败：损坏");
            Assert.AreEqual("\"none\"", await host.ReadPageForTestAsync(
                "getComputedStyle(document.querySelector('img[alt=\"不存在\"]')).display"));

            pending!.Response = environment!.CreateWebResourceResponse(pixels, 200, "OK", "Content-Type: image/png");
            deferral!.Complete();
            deferral = null;
            await WaitForImageStateAsync(host, "慢图片", "ready");
            Assert.AreEqual("3", await host.ReadPageForTestAsync("document.querySelector('img[alt=\"慢图片\"]').naturalWidth"));
            Assert.AreEqual("\"\"", await host.ReadPageForTestAsync(
                "document.querySelector('img[alt=\"慢图片\"]').parentElement.querySelector('[role=status]').textContent"));
            Assert.AreEqual("\"inline\"", await host.ReadPageForTestAsync(
                "getComputedStyle(document.querySelector('img[alt=\"慢图片\"]')).display"));

            int pid = host.BrowserProcessId;
            string folder = host.UserDataFolderForTest!;
            host.Dispose();
            Assert.IsFalse(ProcessRunning(pid));
            Assert.IsFalse(Directory.Exists(folder));
        }
        finally
        {
            if (deferral is not null)
            {
                pending!.Response = environment!.CreateWebResourceResponse(null, 503, "Test ended", "");
                deferral.Complete();
            }
            host ??= await create;
            host.Dispose();
        }
    });

    [TestMethod]
    public Task 替换正文后旧图片晚到不改变当前内容且本地图片正常显示() => RunAsync(async (window, workspace) =>
    {
        string path = Path.Combine(workspace, "sample.md");
        string html = await MarkdownPreviewRenderer.RenderAsync(
            "旧正文\n\n![旧图片](https://markdown-test.invalid/old.png)", workspace, path, false);
        CoreWebView2WebResourceRequestedEventArgs? pending = null;
        CoreWebView2Deferral? deferral = null;
        CoreWebView2Environment? environment = null;
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MarkdownWebViewHost? host = null;
        Task<MarkdownWebViewHost> create = MarkdownWebViewHost.CreateAsync(window.Handle, html, (_, _) => { }, _ => { },
            (web, env) =>
            {
                environment = env;
                web.AddWebResourceRequestedFilter("https://markdown-test.invalid/*", CoreWebView2WebResourceContext.Image,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                web.WebResourceRequested += (_, args) =>
                {
                    if (args.Request.Uri.EndsWith("/old.png", StringComparison.Ordinal))
                    {
                        pending = args;
                        deferral = args.GetDeferral();
                        entered.TrySetResult();
                    }
                    else args.Response = env.CreateWebResourceResponse(null, 404, "Missing", "");
                };
            });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host = await create.WaitAsync(TimeSpan.FromSeconds(3));
            await host.ReadPageForTestAsync("window.oldImage = document.querySelector('img');");
            await File.WriteAllBytesAsync(Path.Combine(workspace, "local.png"), Convert.FromBase64String(ImagePixels));
            string replacement = await MarkdownPreviewRenderer.RenderAsync(
                "# 最新正文\n\n![新图片](https://markdown-test.invalid/new.png)\n\n![本地图片](local.png)\n\n尾段",
                workspace, path, true);
            await host.UpdateContentAsync(replacement, CancellationToken.None);
            pending!.Response = environment!.CreateWebResourceResponse(null, 404, "Missing", "");
            deferral!.Complete();
            deferral = null;
            await WaitForImageStateAsync(host, "新图片", "failure");
            await WaitForImageStateAsync(host, "本地图片", "ready");
            await host.ReadPageForTestAsync("window.oldImage.dispatchEvent(new Event('error')); window.augitRefreshImages();");
            string text = await host.ReadPageForTestAsync("document.body.textContent");
            StringAssert.Contains(text, "最新正文");
            StringAssert.Contains(text, "图片加载失败：新图片");
            Assert.DoesNotContain("旧图片", text, StringComparison.Ordinal);
            Assert.AreEqual("2", await host.ReadPageForTestAsync("document.querySelectorAll('.markdown-image-feedback').length"));
            Assert.AreEqual("\"rgb(30, 31, 34)\"", await host.ReadPageForTestAsync("getComputedStyle(document.body).backgroundColor"));
            Assert.AreEqual("0", await host.ReadPageForTestAsync("document.scripts.length"), "预览 HTML 不得增加脚本或放宽 CSP。");
        }
        finally
        {
            if (deferral is not null)
            {
                pending!.Response = environment!.CreateWebResourceResponse(null, 503, "Test ended", "");
                deferral.Complete();
            }
            host ??= await create;
            host.Dispose();
        }
    });

    private static Task<string> ImageStateAsync(MarkdownWebViewHost host, string label) => host.ReadPageForTestAsync(
        $"document.querySelector('img[alt=\"{label}\"]').parentElement.dataset.state");

    private static async Task WaitForImageStateAsync(MarkdownWebViewHost host, string label, string state)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        string expected = JsonSerializer.Serialize(state);
        string actual;
        do
        {
            actual = await ImageStateAsync(host, label);
            if (actual == expected) return;
            await Task.Delay(15);
        } while (DateTime.UtcNow < deadline);
        Assert.AreEqual(expected, actual, label);
    }
}
