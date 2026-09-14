using System.Diagnostics;
using System.Reflection;
using Augit.Core.Documents;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeImageLoadingTests
{
    [TestMethod]
    public void 解码期间可处理输入且超过阈值只在图片区显示进度()
    {
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        int callerThread = Environment.CurrentManagedThreadId, decoderThread = 0;
        using Scope scope = new(new(path =>
        {
            decoderThread = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("解码等待超时。");
            return WicBitmapLoader.Load(path);
        }));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.AreNotEqual(callerThread, decoderThread);
            Assert.IsFalse(scope.View.IsImagePreviewReady);
            Assert.IsFalse(scope.Image.CanZoomIn);
            Assert.IsFalse(scope.Image.CanZoomOut);
            _ = NativeMethods.SetFocus(scope.Owner);
            nint focus = NativeMethods.GetFocus();
            Stopwatch timer = Stopwatch.StartNew();
            scope.View.SetBounds(0, 0, 450, 350);
            _ = NativeMethods.SendMessage(scope.Image.Handle, NativeMethods.WindowMessageMouseWheel, (nuint)(120 << 16), 0);
            scope.View.SetVisible(false);
            scope.View.SetVisible(true);
            Assert.IsLessThan(150, timer.ElapsedMilliseconds, "图片解码不能阻塞布局、输入或隐藏。");
            WaitUntil(() => scope.Image.LoadingForTest, scope.View);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
            release.Set();
            NativeImageTestPump.Wait(scope.View);
            Assert.IsTrue(scope.View.IsImagePreviewReady);
            Assert.IsFalse(scope.Image.LoadingForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus());
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void 连续替换取消旧结果和队列中的旧解码且最终只显示最新图片()
    {
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        WicBitmap? stale = null;
        int calls = 0;
        using Scope scope = new(new(path =>
        {
            WicBitmap bitmap = WicBitmapLoader.Load(path);
            if (Interlocked.Increment(ref calls) == 1)
            {
                stale = bitmap;
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) { bitmap.Dispose(); throw new InvalidOperationException("解码等待超时。"); }
            }
            return bitmap;
        }));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            NativeImageView image = scope.Image;
            Task obsolete = scope.View.ReloadAsync(scope.Result);
            File.WriteAllBytes(scope.Path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAAEklEQVR4nGP4z8DAAMQN/4EUABt0BH3r3fEnAAAAAElFTkSuQmCC"));
            Task latest = scope.View.ReloadAsync(scope.Result);
            WaitUntil(() => obsolete.IsCompleted, scope.View);
            release.Set();
            NativeImageTestPump.Wait(scope.View, latest);
            Assert.AreSame(image, scope.Image);
            Assert.AreEqual(3, image.BitmapWidth);
            Assert.AreEqual(2, calls, "队列中已过期的请求不应进入 WIC。");
            Assert.AreEqual(0, stale!.Handle, "已解码但过期的完整位图必须释放。");
        }
        finally { release.Set(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 关闭及提前销毁窗口立即取消等待并释放晚到位图(bool nativeDestroy)
    {
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        WicBitmap? decoded = null;
        using Scope scope = new(new(path =>
        {
            decoded = WicBitmapLoader.Load(path);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) { decoded.Dispose(); throw new InvalidOperationException("解码等待超时。"); }
            return decoded;
        }));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            Task completion = scope.View.ImageLoadCompletion;
            Stopwatch timer = Stopwatch.StartNew();
            if (nativeDestroy) _ = NativeMethods.DestroyWindow(scope.View.Handle); else scope.View.Dispose();
            Assert.IsLessThan(150, timer.ElapsedMilliseconds);
            Assert.IsTrue(completion.IsCompleted);
            release.Set();
            scope.View.ImageWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.AreEqual(0, decoded!.Handle);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void 未分发到界面的位图在窗口销毁时同样释放()
    {
        WicBitmap? decoded = null;
        using Scope scope = new(new(path => decoded = WicBitmapLoader.Load(path)));
        scope.View.ImageWorkersForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        Assert.IsFalse(scope.View.IsImagePreviewReady);
        Assert.AreNotEqual(0, decoded!.Handle);
        _ = NativeMethods.DestroyWindow(scope.View.Handle);
        Assert.AreEqual(0, decoded.Handle);
    }

    [TestMethod]
    public void 后台解码失败显示信息页且恢复后可以重新打开()
    {
        bool fail = true;
        using Scope scope = new(new(path => fail ? throw new InvalidOperationException("损坏的图片") : WicBitmapLoader.Load(path)));
        NativeImageTestPump.Wait(scope.View);
        Assert.IsFalse(scope.View.IsImagePreviewReady);
        Assert.IsTrue(scope.View.InfoPageHasSinglePrimaryActionForTest);
        Assert.AreEqual(UiText.ImageDecodeFailed, scope.View.ReadStatusText);
        fail = false;
        NativeImageTestPump.Wait(scope.View, scope.View.ReloadAsync(scope.Result));
        Assert.IsTrue(scope.View.IsImagePreviewReady);
        Assert.AreEqual("PNG 图片", scope.View.ReadStatusText);
    }

    private static void WaitUntil(Func<bool> condition, NativeDocumentView view)
    {
        long deadline = Environment.TickCount64 + 3_000;
        while (!condition())
        {
            NativeFindTestPump.Dispatch(view.Handle);
            if (Environment.TickCount64 >= deadline) Assert.Fail("图片进度没有出现。");
            Thread.Sleep(1);
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        internal Scope(NativeImageDecoder decoder)
        {
            Path = _temporary.GetPath("sample.png");
            File.Copy(NativeImagePreviewTests.SamplePath(), Path);
            Result = new(DocumentReadStatus.ImageReady, Path, Path, new(DocumentKind.Png, "PNG 图片"),
                new FileInfo(Path).Length, null, null, null, string.Empty);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 600, 500, 0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(Owner, _temporary.FullPath, Result, new(), _ => { }, (_, _, _) => Assert.Fail("不得打开其他文档。"), decoder);
            View.SetBounds(0, 0, 550, 450);
        }
        internal string Path { get; }
        internal nint Owner { get; }
        internal NativeDocumentView View { get; }
        internal DocumentReadResult Result { get; }
        internal NativeImageView Image => (NativeImageView)typeof(NativeDocumentView)
            .GetField("_imageView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(View)!;
        public void Dispose()
        {
            View.Dispose();
            View.ImageWorkersForTest.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(Owner);
            _temporary.Dispose();
        }
    }
}
