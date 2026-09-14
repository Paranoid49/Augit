using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Core.Documents;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeImagePreviewTests
{
    public TestContext TestContext { get; set; } = null!;
    private const string TransparentPixels = "iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAAEklEQVR4nGP4z8DAAMQN/4EUABt0BH3r3fEnAAAAAElFTkSuQmCC";

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void 图片动作图标有独立的缩放和适应几何(bool fit, bool zoomIn)
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using Surface surface = new(32, 32);
        surface.DrawIcon(fit, zoomIn);
        int colored = 0;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            if (surface.Pixel(x, y) != 0x00FFFFFF) { colored++; Assert.IsTrue(x >= 9 && x <= 23 && y >= 9 && y <= 23); }
        Assert.IsGreaterThan(20, colored);
        if (fit) Assert.AreEqual(0x00FFFFFFu, surface.Pixel(15, 15));
        else Assert.AreNotEqual(0x00FFFFFFu, surface.Pixel(15, 15));
        if (!fit) Assert.AreEqual(!zoomIn, surface.Pixel(15, 13) == 0x00FFFFFF);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PNG透明半透明和不透明像素按主题正确合成(bool dark)
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("alpha.png");
        File.WriteAllBytes(path, Convert.FromBase64String(TransparentPixels));
        using ViewScope scope = new(path, 192, 120);
        scope.View.ApplyAppearance(dark);
        for (int i = 0; i < 12; i++) scope.View.ZoomIn();
        Assert.AreEqual(800, scope.View.ZoomPercentage);
        using Surface capture = new(192, 120);
        capture.Draw(scope.View);
        var bounds = scope.View.CurrentImageBounds;
        int y = bounds.Y + 3;
        uint Background(int x) => (((x - bounds.X) / 8 + (y - bounds.Y) / 8) % 2 == 0)
            ? NativeTheme.Palette(dark).Panel : NativeTheme.Palette(dark).PanelMuted;
        int transparentX = bounds.X + 3, halfX = bounds.X + 11, opaqueX = bounds.X + 19;
        Assert.AreEqual(Background(transparentX), capture.Pixel(transparentX, y), "透明像素不能覆盖棋盘格。");
        uint half = capture.Pixel(halfX, y), background = Background(halfX);
        Assert.IsLessThanOrEqualTo(2, Math.Abs((int)(half & 255) - (128 + (int)(background & 255) * 127 / 255)));
        Assert.IsLessThanOrEqualTo(2, Math.Abs((int)((half >> 8) & 255) - (int)((background >> 8) & 255) * 127 / 255));
        Assert.IsLessThanOrEqualTo(2, Math.Abs((int)((half >> 16) & 255) - (int)((background >> 16) & 255) * 127 / 255));
        Assert.AreEqual(0x000000FFu, capture.Pixel(opaqueX, y));
        foreach (int outsideX in new[] { 3, 11, 19, 187 })
            Assert.AreEqual(NativeTheme.Palette(dark).Panel, capture.Pixel(outsideX, 3),
                "图片矩形外始终为纯色，不能继续铺棋盘格。");
    }

    [TestMethod]
    [DataRow(false, 96)]
    [DataRow(true, 96)]
    [DataRow(false, 120)]
    [DataRow(true, 120)]
    [DataRow(false, 144)]
    [DataRow(true, 144)]
    public void 棋盘格随图片移动且边框始终位于图片内部(bool dark, int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        int width = NativeTheme.Scale(331), height = NativeTheme.Scale(271);
        using ViewScope scope = new(CreatePattern(200, 0), width, height);
        scope.View.ApplyAppearance(dark);
        VerifyPixels(width, height, checkBorder: true);
        width += NativeTheme.Scale(6);
        height += NativeTheme.Scale(10);
        scope.Resize(width, height);
        VerifyPixels(width, height, checkBorder: true);
        while (scope.View.ZoomPercentage < 400) scope.View.ZoomIn();
        var before = scope.View.CurrentImageBounds;
        scope.Message(0x0201, 0, Position(100, 100));
        scope.Message(0x0200, 1, Position(105, 107));
        scope.Message(0x0202);
        Assert.AreEqual(before.X + 5, scope.View.CurrentImageBounds.X);
        Assert.AreEqual(before.Y + 7, scope.View.CurrentImageBounds.Y);
        VerifyPixels(width, height, checkBorder: false);
        scope.View.FitToArea();
        VerifyPixels(width, height, checkBorder: true);

        void VerifyPixels(int viewWidth, int viewHeight, bool checkBorder)
        {
            using Surface capture = new(viewWidth, viewHeight);
            capture.Draw(scope.View);
            var bounds = scope.View.CurrentImageBounds;
            var palette = NativeTheme.Palette(dark);
            int border = NativeTheme.Scale(1), cell = NativeTheme.Scale(8);
            // 比较实际像素，包含不在格子边界上的位移、裁切后的负原点和透明图片区域。
            for (int y = 0; y < viewHeight; y += 7)
                for (int x = 0; x < viewWidth; x += 7) Verify(x, y);
            if (checkBorder)
            {
                for (int x = bounds.X - 1; x <= bounds.X + bounds.Width; x++)
                    for (int edge = -1; edge <= border; edge++)
                    {
                        Verify(x, bounds.Y + edge);
                        Verify(x, bounds.Y + bounds.Height - edge - 1);
                    }
                for (int y = bounds.Y - 1; y <= bounds.Y + bounds.Height; y++)
                    for (int edge = -1; edge <= border; edge++)
                    {
                        Verify(bounds.X + edge, y);
                        Verify(bounds.X + bounds.Width - edge - 1, y);
                    }
            }
            void Verify(int x, int y)
            {
                int relativeX = x - bounds.X, relativeY = y - bounds.Y;
                uint expected = palette.Panel;
                if (relativeX >= 0 && relativeX < bounds.Width && relativeY >= 0 && relativeY < bounds.Height)
                    expected = relativeX < border || relativeY < border || relativeX >= bounds.Width - border || relativeY >= bounds.Height - border
                        ? palette.BorderStrong : ((relativeX / cell + relativeY / cell) % 2 == 0 ? palette.Panel : palette.PanelMuted);
                Assert.AreEqual(expected, capture.Pixel(x, y), $"图片 ({bounds.X},{bounds.Y}) 的画布像素 ({x},{y}) 不符合图片局部棋盘或内部边框。");
            }
        }
    }

    [TestMethod]
    public void 适应区域可低于十百分比并保持大图完整可见()
    {
        double scale = NativeImageView.CalculateFitScaleForTest(320, 240, 5000, 5000);
        Assert.IsLessThan(.1, scale);
        var bounds = NativeImageView.CalculateImageBoundsForTest(320, 240, 5000, 5000, scale);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(32), bounds.X);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(32), bounds.Y);
        Assert.AreEqual(.1, NativeImageView.CalculateNextZoomForTest(scale, true));
        Assert.AreEqual(scale, NativeImageView.CalculateNextZoomForTest(scale, false));
    }

    [TestMethod]
    public void 放大后拖动滚轮和方向键可到达图片边缘且适应后归中()
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using ViewScope scope = new(SamplePath(), 320, 240);
        scope.View.ZoomIn(); scope.View.ZoomIn();
        Assert.AreEqual(50, scope.View.ZoomPercentage);
        scope.Message(0x0201, 0, Position(100, 100));
        scope.Message(0x0200, 1, Position(2000, 2000));
        Assert.AreEqual(0, scope.View.CurrentImageBounds.X);
        Assert.AreEqual(0, scope.View.CurrentImageBounds.Y);
        scope.Message(0x0200, 1, Position(-2000, -2000));
        var bounds = scope.View.CurrentImageBounds;
        Assert.AreEqual(320, bounds.X + bounds.Width);
        Assert.AreEqual(240, bounds.Y + bounds.Height);
        scope.Message(0x0202);
        Assert.AreNotEqual(scope.View.Handle, NativeMethods.GetCapture());
        scope.Message(NativeMethods.WindowMessageKeyDown, 37);
        Assert.AreEqual(bounds.X + 32, scope.View.CurrentImageBounds.X);
        scope.Message(NativeMethods.WindowMessageMouseWheel, (nuint)(120 << 16));
        Assert.AreEqual(bounds.Y + 48, scope.View.CurrentImageBounds.Y);
        scope.Message(NativeMethods.WindowMessageMouseWheel, (nuint)((120 << 16) | 8));
        Assert.AreEqual(75, scope.View.ZoomPercentage);
        scope.View.FitToArea();
        bounds = scope.View.CurrentImageBounds;
        Assert.AreEqual((320 - bounds.Width) / 2, bounds.X);
        Assert.AreEqual((240 - bounds.Height) / 2, bounds.Y);
        Assert.IsTrue(scope.View.IsFitToArea);
    }

    [TestMethod]
    public void 隐藏与提前销毁释放拖动捕获和位图()
    {
        using ViewScope scope = new(SamplePath(), 320, 240);
        scope.View.ZoomIn();
        scope.Message(0x0201, 0, Position(30, 30));
        Assert.AreEqual(scope.View.Handle, NativeMethods.GetCapture());
        scope.View.SetVisible(false);
        Assert.AreNotEqual(scope.View.Handle, NativeMethods.GetCapture());
        scope.View.SetVisible(true);
        scope.Message(0x0201, 0, Position(30, 30));
        nint previousHandle = scope.View.Handle;
        _ = NativeMethods.DestroyWindow(previousHandle);
        Assert.AreNotEqual(previousHandle, NativeMethods.GetCapture());
        Assert.AreEqual(0, scope.View.Handle);
        Assert.AreEqual(0, scope.Bitmap.Handle, "原生窗口提前销毁后不等待托管 Dispose 才释放图片。");
    }

    [TestMethod]
    public void 高精度缩放累计一档且快速多档输入只通知一次()
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using ViewScope scope = new(SamplePath(), 320, 240);
        scope.View.ZoomIn(); scope.View.ZoomIn();
        Assert.AreEqual(50, scope.View.ZoomPercentage);
        int changes = 0;
        scope.View.ViewChanged += () => changes++;
        nint focus = NativeMethods.GetFocus();
        for (int i = 0; i < 3; i++) scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(30, 8));
        Assert.AreEqual(50, scope.View.ZoomPercentage, "不到一档时不跳变比例。");
        Assert.AreEqual(0, changes);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(30, 8));
        Assert.AreEqual(75, scope.View.ZoomPercentage);
        Assert.AreEqual(1, changes);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(360, 8));
        Assert.AreEqual(150, scope.View.ZoomPercentage, "一次三档必须完整处理。");
        Assert.AreEqual(2, changes, "同一事件合并成一次视图刷新。");
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(0, 8));
        Assert.AreEqual(2, changes);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(-240, 8));
        Assert.AreEqual(100, scope.View.ZoomPercentage);
        Assert.AreEqual(focus, NativeMethods.GetFocus(), "滚轮不能移动输入焦点。");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 细小滚动保留余量且横向滚轮与Shift方向正确(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(SamplePath(), 320, 240);
        while (scope.View.ZoomPercentage < 100) scope.View.ZoomIn();
        var initial = scope.View.CurrentImageBounds;
        for (int i = 0; i < 120; i++) scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(1));
        Assert.AreEqual(initial.Y + NativeTheme.Scale(48), scope.View.CurrentImageBounds.Y, "小于一像素的输入不能丢失。");
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(-120));
        Assert.AreEqual(initial, scope.View.CurrentImageBounds);
        scope.Message(NativeMethods.WindowMessageMouseHorizontalWheel, Wheel(120));
        Assert.AreEqual(initial.X - NativeTheme.Scale(48), scope.View.CurrentImageBounds.X);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(120, 4));
        Assert.AreEqual(initial, scope.View.CurrentImageBounds);
    }

    [TestMethod]
    public void 缩放余量不穿过隐藏按钮或不同滚轮模式()
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using ViewScope scope = new(SamplePath(), 320, 240);
        scope.View.ZoomIn(); scope.View.ZoomIn();
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(90, 8));
        var before = scope.View.CurrentImageBounds;
        scope.View.SetVisible(false);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(120, 8));
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(120));
        Assert.AreEqual(before, scope.View.CurrentImageBounds);
        scope.View.SetVisible(true);
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(30, 8));
        Assert.AreEqual(50, scope.View.ZoomPercentage, "隐藏前余量不能让重新显示后的第一次输入跳档。");
        scope.View.ZoomIn();
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(90, 8));
        Assert.AreEqual(75, scope.View.ZoomPercentage, "按钮动作清除之前的滚轮余量。");
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(120));
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(30, 8));
        Assert.AreEqual(75, scope.View.ZoomPercentage, "切换到平移后不继承缩放余量。");
        scope.View.FitToArea();
        scope.Message(NativeMethods.WindowMessageMouseWheel, Wheel(90, 8));
        Assert.IsTrue(scope.View.IsFitToArea);
    }

    [TestMethod]
    public void 外部替换复用图片窗口并保持缩放焦点而失效内容进入信息页()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("sample.png");
        File.Copy(SamplePath(), path);
        DocumentReadResult result = new(DocumentReadStatus.ImageReady, path, path, new(DocumentKind.Png, "PNG 图片"),
            new FileInfo(path).Length, null, null, null, string.Empty);
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 600, 500, 0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using NativeDocumentView document = new(owner, temporary.FullPath, result, new(), _ => { }, (_, _, _) => { });
            document.SetBounds(0, 0, 550, 450);
            NativeImageTestPump.Wait(document);
            NativeImageView image = Image(document);
            image.ZoomIn(); image.ZoomIn();
            _ = NativeMethods.SetFocus(image.Handle);
            var before = image.CurrentImageBounds;
            int zoom = image.ZoomPercentage;
            NativeImageTestPump.Wait(document, document.ReloadAsync(result));
            Assert.AreSame(image, Image(document));
            Assert.AreEqual(zoom, image.ZoomPercentage);
            Assert.AreEqual(before, image.CurrentImageBounds);
            Assert.AreEqual(image.Handle, NativeMethods.GetFocus());
            File.WriteAllBytes(path, Convert.FromBase64String(TransparentPixels));
            NativeImageTestPump.Wait(document, document.ReloadAsync(result));
            Assert.AreSame(image, Image(document));
            Assert.AreEqual(3, image.BitmapWidth, "相同读取记录不能跳过实际图片更新。");
            Assert.AreEqual(zoom, image.ZoomPercentage);
            File.WriteAllText(path, "损坏的图片");
            NativeImageTestPump.Wait(document, document.ReloadAsync(result));
            Assert.IsFalse(document.IsImagePreviewReady);
            Assert.IsTrue(document.InfoPageHasSinglePrimaryActionForTest);
            File.Copy(SamplePath(), path, true);
            NativeImageTestPump.Wait(document, document.ReloadAsync(result));
            Assert.IsTrue(document.IsImagePreviewReady);
        }
        finally { _ = NativeMethods.DestroyWindow(owner); }
    }

    [TestMethod]
    public void 连续重绘复用棋盘与画布且关闭后回收Gdi资源()
    {
        using (ViewScope warm = new(SamplePath(), 320, 240)) { warm.Paint(); warm.Settle(); }
        using Process process = Process.GetCurrentProcess();
        int before = GetGuiResources(process.Handle, 0);
        using (ViewScope scope = new(SamplePath(), 320, 240))
        {
            scope.Paint();
            scope.Settle();
            int live = GetGuiResources(process.Handle, 0);
            for (int i = 0; i < 30; i++) { scope.View.ZoomIn(); scope.Paint(); scope.Settle(); scope.View.ZoomOut(); scope.Paint(); scope.Settle(); }
            Assert.AreEqual(live, GetGuiResources(process.Handle, 0));
        }
        Assert.IsLessThanOrEqualTo(before + 1, GetGuiResources(process.Handle, 0));
    }

    [TestMethod]
    [DataRow(255)]
    [DataRow(128)]
    [DataRow(0)]
    public void 高频图片缩小平滑采样并保留半透明及全透明像素(int alpha)
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using ViewScope scope = new(CreatePattern(100, (byte)alpha), 114, 114);
        using Surface capture = new(114, 114);
        Assert.AreEqual(50, scope.View.ZoomPercentage);
        capture.Draw(scope.View);
        scope.Settle();
        capture.Draw(scope.View);
        uint pixel = capture.Pixel(55, 55);
        for (int shift = 0; shift < 24; shift += 8)
        {
            int channel = (int)((pixel >> shift) & 255);
            int background = (int)((NativeTheme.Palette(false).Panel >> shift) & 255);
            int expected = alpha / 2 + background * (255 - alpha) / 255;
            Assert.IsLessThanOrEqualTo(3, Math.Abs(channel - expected), "缩小必须平滑相邻像素并保留正确透明度。");
        }
        scope.View.ZoomIn();
        scope.View.ZoomIn();
        Assert.AreEqual(100, scope.View.ZoomPercentage);
        capture.Draw(scope.View);
        pixel = capture.Pixel(55, 55);
        if (alpha == 255) Assert.IsTrue(pixel is 0 or 0x00FFFFFF, "100% 时应保留图片原始像素。");
    }

    [TestMethod]
    public void 最大像素样本的连续缩小绘制记录实际帧耗时()
    {
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride(96);
        using ViewScope scope = new(CreatePattern(5000), 720, 480);
        using Surface capture = new(720, 480);
        long initial = Stopwatch.GetTimestamp();
        capture.Draw(scope.View);
        double firstFrame = Stopwatch.GetElapsedTime(initial).TotalMilliseconds;
        long preparation = Stopwatch.GetTimestamp();
        scope.Settle();
        double backgroundWait = Stopwatch.GetElapsedTime(preparation).TotalMilliseconds;
        Assert.IsTrue(scope.View.HasSmoothSampleForTest);
        double[] milliseconds = new double[12];
        for (int i = 0; i < milliseconds.Length; i++)
        {
            long started = Stopwatch.GetTimestamp();
            capture.Draw(scope.View);
            milliseconds[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(milliseconds);
        TestContext.WriteLine($"图片绘制：2500 万像素，720×480，首帧={firstFrame:F2}ms，等待后台采样={backgroundWait:F2}ms，P50={milliseconds[6]:F2}ms，最大={milliseconds[^1]:F2}ms。");
        Assert.IsLessThan(150d, firstFrame, "平滑缓存未就绪时也必须及时显示图片。");
        Assert.IsLessThan(150d, milliseconds[^1], "最大允许图片的绘制不能长时间阻塞界面。");
    }

    [TestMethod]
    public void 图片采样期间替换或关闭不会读取已经释放的原图()
    {
        using ViewScope scope = new(CreatePattern(5000), 720, 480);
        using Surface capture = new(720, 480);
        capture.Draw(scope.View);
        WicBitmap replacement = CreatePattern(100);
        scope.View.SetBitmap(replacement);
        capture.Draw(scope.View);
        scope.Settle();
        Assert.AreEqual(0, scope.Bitmap.Handle, "最后一个后台采样结束后必须释放旧图。");
        Assert.AreEqual(100, scope.View.BitmapWidth);
        Assert.IsFalse(scope.View.HasSmoothSampleForTest, "原尺寸新图不能使用旧图的缩小缓存。");
        scope.View.Dispose();
        Assert.AreEqual(0, replacement.Handle);
    }

    private static WicBitmap CreatePattern(int size, byte alpha = 255)
    {
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint handle = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out nint bits, 0, 0);
        Assert.AreNotEqual(0, handle);
        byte[][] rows = [new byte[size * 4], new byte[size * 4]];
        for (int row = 0; row < 2; row++) for (int x = 0; x < size; x++)
        {
            byte value = (byte)((x + row) % 2 == 0 ? 0 : alpha);
            rows[row][x * 4] = rows[row][x * 4 + 1] = rows[row][x * 4 + 2] = value;
            rows[row][x * 4 + 3] = alpha;
        }
        for (int y = 0; y < size; y++) Marshal.Copy(rows[y % 2], 0, bits + y * size * 4, size * 4);
        return new(handle, size, size, bits);
    }

    private static NativeImageView Image(NativeDocumentView document) =>
        (NativeImageView)typeof(NativeDocumentView).GetField("_imageView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(document)!;

    internal static string SamplePath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Augit.slnx")))
                return Path.Combine(directory.FullName, "docs", "ux-mockups", "assets", "image-sample.png");
        throw new InvalidOperationException("找不到共享图片样本。");
    }

    private static nint Position(int x, int y) => (nint)(((y & 0xffff) << 16) | (x & 0xffff));

    private static nuint Wheel(int delta, int keys = 0) => unchecked((nuint)(((uint)(ushort)delta << 16) | (uint)keys));

    private sealed class ViewScope : IDisposable
    {
        private readonly nint _owner;
        internal ViewScope(string path, int width, int height) : this(WicBitmapLoader.Load(path), width, height) { }
        internal ViewScope(WicBitmap bitmap, int width, int height)
        {
            _owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, width, height,
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            View = new(_owner);
            View.SetBounds(0, 0, width, height);
            Bitmap = bitmap;
            View.SetBitmap(Bitmap);
        }
        internal NativeImageView View { get; }
        internal WicBitmap Bitmap { get; }
        internal void Resize(int width, int height)
        {
            _ = NativeMethods.MoveWindow(_owner, 0, 0, width, height, true);
            View.SetBounds(0, 0, width, height);
        }
        internal void Message(uint message, nuint word = 0, nint value = 0) => _ = NativeMethods.SendMessage(View.Handle, message, word, value);
        internal void Paint() { _ = NativeMethods.InvalidateRectangle(View.Handle, 0, false); _ = UpdateWindow(View.Handle); }
        internal void Settle()
        {
            long deadline = Environment.TickCount64 + 5_000;
            while (View.SamplingForTest || !View.SampleWorkersForTest.IsCompleted)
            {
                NativeFindTestPump.Dispatch(View.Handle);
                if (Environment.TickCount64 >= deadline) Assert.Fail("后台采样没有完成。");
                Thread.Sleep(1);
            }
            View.SampleWorkersForTest.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            View.Dispose();
            View.SampleWorkersForTest.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(_owner);
        }
    }

    private sealed class Surface : IDisposable
    {
        private readonly nint _dc, _bitmap, _previous;
        internal Surface(int width, int height)
        {
            _dc = NativeMethods.CreateCompatibleDeviceContext(0);
            NativeMethods.BitmapInfo info = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32
                }
            };
            _bitmap = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out _, 0, 0);
            _previous = NativeMethods.SelectObject(_dc, _bitmap);
        }
        internal void Draw(NativeImageView view) => _ = NativeMethods.SendMessage(view.Handle, 0x0318, (nuint)_dc, 0);
        internal uint Pixel(int x, int y) => NativeMethods.GetPixel(_dc, x, y);
        internal void DrawIcon(bool fit, bool zoomIn)
        {
            NativeMethods.Rectangle bounds = new() { Right = 32, Bottom = 32 };
            nint brush = NativeMethods.CreateSolidBrush(0x00FFFFFF);
            _ = NativeMethods.FillRectangle(_dc, ref bounds, brush);
            _ = NativeMethods.DeleteObject(brush);
            NativeTheme.DrawImageActionIcon(_dc, 16, 16, fit, zoomIn, 0);
        }
        public void Dispose() { _ = NativeMethods.SelectObject(_dc, _previous); _ = NativeMethods.DeleteObject(_bitmap); _ = NativeMethods.DeleteDeviceContext(_dc); }
    }

    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetGuiResources(nint process, int flags);
}
