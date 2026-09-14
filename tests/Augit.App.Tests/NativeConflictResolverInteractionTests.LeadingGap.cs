using System.Runtime.InteropServices;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    [DataRow("Light", 96, 0)]
    [DataRow("Dark", 96, 2)]
    [DataRow("Light", 120, 2)]
    [DataRow("Dark", 120, 0)]
    [DataRow("Light", 144, 0)]
    [DataRow("Dark", 144, 2)]
    public async Task 首行空侧留白随三栏滚动并在接受保存撤销后保持原文(string theme, int dpi, int emptySide)
    {
        string prefix = "新增一😀\n新增二\n";
        string common = string.Concat(Enumerable.Range(0, 60).Select(i => $"公共内容 {i}\n"));
        string tail = "尾行\n" + (dpi == 144 ? new string('x', 110_000) : string.Empty);
        string yours = (emptySide == 0 ? "" : prefix) + common + "左三\n" + tail;
        string theirs = (emptySide == 2 ? "" : prefix) + common + "右三\n右四\n" + tail;
        string first = "<<<<<<< HEAD\n" + (emptySide == 0 ? "" : prefix)
            + "=======\n" + (emptySide == 2 ? "" : prefix) + ">>>>>>> feature\n";
        string result = first + common + "<<<<<<< HEAD\n左三\n=======\n右三\n右四\n>>>>>>> feature\n" + tail;
        int registrations = NativeConflictTextViewport.RegistrationCountForTest;
        await RunAsync(async (dialog, service) =>
        {
            await WaitForConflictPresentationAsync(dialog);
            NativeConflictTextViewport[] views = dialog.ViewportsForTest;
            nint[] editors = views.Select(view => view.Editor.Handle).ToArray();
            string[] texts = [yours, result, theirs];
            NativeConflictTextViewport empty = views[emptySide];
            Assert.AreEqual(2, empty.LeadingLines);
            Assert.IsTrue(HasScrollBar(empty.Handle, NativeMethods.WindowStyleVerticalScroll));
            foreach (var view in views.Where(view => view != empty)) AssertNoContainerScrollBars(view);
            AssertSameScreenLine(editors, texts, "公共内容 0");
            int height = (int)NativeMethods.SendMessage(editors[emptySide], 2279, 0, 0);
            Assert.AreEqual(2 * height, ClientTop(empty.Editor.Handle) - ClientTop(empty.Handle), "首行留白必须真实占据两个显示行。");
            int parses = dialog.ResultParseCountForTest;
            int layouts = empty.LayoutCountForTest;
            await Task.Delay(200);
            Assert.AreEqual(parses, dialog.ResultParseCountForTest, "展示更新不得触发重复解析。");
            Assert.AreEqual(layouts, empty.LayoutCountForTest, "稳定画面不能继续布局或刷新正文。");

            empty.SetFirstVisibleLine(1);
            await Task.Delay(50);
            Assert.AreEqual((1, 1, 1), dialog.FirstVisibleLinesForTest);
            Assert.AreEqual(height, ClientTop(empty.Editor.Handle) - ClientTop(empty.Handle));
            AssertSameScreenLine(editors, texts, "公共内容 0");
            for (int side = 0; side < 3; side++)
            {
                views[side].SetFirstVisibleLine(0);
                await Task.Delay(50);
                nint wheelTarget = side == emptySide ? views[side].Handle : editors[side];
                _ = NativeMethods.SendMessage(wheelTarget, NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(-120 << 16)), 0);
                await Task.Delay(50);
                Assert.IsGreaterThan(0, views[side].FirstVisibleLine, "在正文和首行留白上滚轮都应滚动。");
                Assert.AreEqual(views[0].FirstVisibleLine, views[1].FirstVisibleLine);
                Assert.AreEqual(views[1].FirstVisibleLine, views[2].FirstVisibleLine);
                AssertSameScreenLine(editors, texts, "公共内容 3");
            }
            empty.SetFirstVisibleLine(40);
            await Task.Delay(50);
            AssertScreenTop(editors, texts, "公共内容 38");
            _ = NativeMethods.SendMessage(empty.Handle, NativeMethods.WindowMessageVerticalScroll, 6, 0); // SB_TOP。
            await Task.Delay(50);
            Assert.AreEqual((0, 0, 0), dialog.FirstVisibleLinesForTest);
            AssertSameScreenLine(editors, texts, "公共内容 0");

            // 使用 Scintilla 的真实选中文本接口验证复制范围，避免覆盖用户系统剪贴板。
            _ = NativeMethods.SendMessage(empty.Editor.Handle, 2013, 0, 0);
            int length = (int)NativeMethods.SendMessage(empty.Editor.Handle, 2161, 0, 0);
            nint buffer = Marshal.AllocCoTaskMem(length);
            try
            {
                _ = NativeMethods.SendMessage(empty.Editor.Handle, 2161, 0, buffer);
                Assert.AreEqual(texts[emptySide], Marshal.PtrToStringUTF8(buffer));
            }
            finally { Marshal.FreeCoTaskMem(buffer); }
            ClickConflictAction(dialog, 5);
            await Task.Delay(50);
            // 第二处紧邻文件尾，实际首行可能受视口高度限制；核对共同正文的屏幕位置。
            AssertSameScreenLine(editors, texts, "公共内容 59");
            AssertSameScreenLine(editors, texts, "尾行");
            ClickConflictAction(dialog, 3);
            await WaitForConflictPresentationAsync(dialog);
            ClickConflictAction(dialog, 3);
            await WaitForConflictPresentationAsync(dialog);
            string accepted = prefix + common + "左三\n右三\n右四\n" + tail;
            Assert.AreEqual(accepted, dialog.ResultTextForTest);
            Assert.AreEqual(0, empty.LeadingLines);
            foreach (var view in views) AssertNoContainerScrollBars(view);
            Task save = dialog.SaveForTestAsync();
            Assert.AreEqual(accepted, service.Saves.Single().ResultText);
            service.Pending.SetResult(Failure());
            await save;
            for (int undo = 0; undo < 2; undo++)
            {
                _ = NativeMethods.SendMessage(editors[1], 2176, 0, 0);
                await WaitForConflictPresentationAsync(dialog);
            }
            Assert.AreEqual(result, dialog.ResultTextForTest);
            Assert.IsFalse(dialog.ResultIsDirtyForTest);
            Assert.AreEqual(2, empty.LeadingLines);
            empty.SetFirstVisibleLine(0);
            await Task.Delay(50);
            AssertSameScreenLine(editors, texts, "公共内容 0");
            Assert.IsTrue(dialog.YoursIsReadOnlyForTest && dialog.TheirsIsReadOnlyForTest);
        }, theme: theme, dpi: dpi, resultText: result, yoursText: yours, theirsText: theirs, codeSize: 13);
        Assert.AreEqual(registrations, NativeConflictTextViewport.RegistrationCountForTest, "关闭解决器必须释放全部容器与正文回调。");
    }

    [TestMethod]
    [DataRow("Light", 96, 0)]
    [DataRow("Dark", 96, 2)]
    [DataRow("Light", 120, 2)]
    [DataRow("Dark", 120, 0)]
    [DataRow("Light", 144, 0)]
    [DataRow("Dark", 144, 2)]
    public Task 超过一屏的首行空侧可分页并回到完整留白(string theme, int dpi, int emptySide)
    {
        string inserted = string.Concat(Enumerable.Range(0, 80).Select(i => $"新增 {i}\n"));
        string tail = string.Concat(Enumerable.Range(0, 80).Select(i => $"后文 {i}\n"));
        string yours = emptySide == 0 ? tail : inserted + tail;
        string theirs = emptySide == 2 ? tail : inserted + tail;
        string result = "<<<<<<< HEAD\n" + (emptySide == 0 ? "" : inserted)
            + "=======\n" + (emptySide == 2 ? "" : inserted) + ">>>>>>> feature\n" + tail;
        return RunAsync(async (dialog, service) =>
        {
            await WaitForConflictPresentationAsync(dialog);
            var views = dialog.ViewportsForTest;
            var empty = views[emptySide];
            nint[] editors = views.Select(view => view.Editor.Handle).ToArray();
            string[] texts = [yours, result, theirs];
            Assert.AreEqual(80, empty.LeadingLines);
            Assert.IsTrue(HasScrollBar(empty.Handle, NativeMethods.WindowStyleVerticalScroll), "超过一屏的首行留白必须提供可见滚动条。");
            Assert.IsFalse(HasScrollBar(empty.Handle, NativeMethods.WindowStyleHorizontalScroll), "短行不应出现额外横向滚动条。");
            _ = NativeMethods.GetClientRectangle(empty.Handle, out var client);
            Assert.AreEqual(client.Bottom, ClientTop(empty.Editor.Handle) - ClientTop(empty.Handle));
            int thumbBefore = AssertLeadingScrollBarPixels(dialog, empty, theme, dpi, emptySide, "top");
            _ = NativeMethods.SendMessage(empty.Handle, NativeMethods.WindowMessageVerticalScroll, 3, 0); // SB_PAGEDOWN。
            Assert.IsGreaterThan(0, empty.FirstVisibleLine);
            empty.SetFirstVisibleLine(75);
            await Task.Delay(60);
            AssertSameScreenLine(editors, texts, "后文 0");
            int thumbAfter = AssertLeadingScrollBarPixels(dialog, empty, theme, dpi, emptySide, "scrolled");
            Assert.IsGreaterThan(thumbBefore, thumbAfter, "进入正文后，滚动条滑块必须同步移动。");
            _ = NativeMethods.SetFocus(empty.Editor.Handle);
            _ = NativeMethods.SendMessage(empty.Editor.Handle, NativeMethods.WindowMessageKeyDown, 0x22, 0);
            await Task.Delay(60);
            Assert.IsGreaterThan(75, empty.FirstVisibleLine, "PageDown 应跨过留白末尾进入真实正文。");
            Assert.AreEqual(views[1].FirstVisibleLine, empty.FirstVisibleLine);
            _ = NativeMethods.SendMessage(empty.Handle, NativeMethods.WindowMessageVerticalScroll, 6, 0);
            await Task.Delay(60);
            Assert.AreEqual((0, 0, 0), dialog.FirstVisibleLinesForTest);
            Assert.AreEqual(tail, empty.Editor.GetTextContent());
            Assert.AreEqual(result, dialog.ResultTextForTest);
        }, theme: theme, dpi: dpi, resultText: result, yoursText: yours, theirsText: theirs, physicalPixels: true);
    }

    private static int AssertLeadingScrollBarPixels(NativeConflictResolverDialog dialog,
        NativeConflictTextViewport empty, string theme, int dpi, int emptySide, string state)
    {
        LeadingScrollBarInfo info = new() { Size = (uint)Marshal.SizeOf<LeadingScrollBarInfo>() };
        Assert.IsTrue(GetLeadingScrollBarInfo(empty.Handle, -5, ref info)); // OBJID_VSCROLL。
        Assert.AreEqual(0U, info.State & 0x18001U, "原生滚动条不能不可见、在屏幕外或不可用。");
        Assert.IsGreaterThan(info.Bounds.Left, info.Bounds.Right);
        Assert.IsGreaterThan(info.ThumbTop, info.ThumbBottom);
        _ = NativeMethods.GetClientRectangle(empty.Handle, out var client);
        var viewport = Bounds(empty.Handle);
        Assert.AreEqual(viewport.Right - viewport.Left - client.Right, info.Bounds.Right - info.Bounds.Left,
            "滚动条必须占据非客户区，不能覆盖正文。");
        FramePixels pixels = FramePixels.Capture(dialog.HandleForTest);
        var window = Bounds(dialog.HandleForTest);
        int x = (info.Bounds.Left + info.Bounds.Right) / 2 - window.Left;
        HashSet<uint> colors = [];
        for (int y = info.Bounds.Top + info.ButtonSize; y < info.Bounds.Bottom - info.ButtonSize; y++)
            colors.Add(pixels.At(x, y - window.Top));
        Assert.IsGreaterThan(1, colors.Count, "实际截图中必须有轨道和滑块，不能只有空白或仅设置滚动范围。");
        if (state == "top")
            Assert.AreEqual(NativeTheme.Palette(theme == "Dark").Panel,
                pixels.At(viewport.Left - window.Left + client.Right / 2, viewport.Top - window.Top + client.Bottom / 2),
                "超过一屏的空侧留白必须保持纯色，不能泄露正文或行号。");
        if (Environment.GetEnvironmentVariable("AUGIT_VISUAL_EVIDENCE") is { Length: > 0 } directory)
            pixels.Save(Path.Combine(directory, $"conflict-long-gap-{theme.ToLowerInvariant()}-{dpi}-{emptySide}-{state}.bmp"));
        return info.ThumbTop;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LeadingScrollBarInfo
    {
        internal uint Size;
        internal NativeMethods.Rectangle Bounds;
        internal int ButtonSize, ThumbTop, ThumbBottom, Reserved;
        internal uint State, UpState, UpPageState, ThumbState, DownPageState, DownState;
    }

    [DllImport("user32.dll", EntryPoint = "GetScrollBarInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLeadingScrollBarInfo(nint window, int objectId, ref LeadingScrollBarInfo info);

    private static int ClientTop(nint window)
    {
        NativeMethods.Point point = default;
        Assert.IsTrue(NativeMethods.ClientToScreen(window, ref point));
        return point.Y;
    }

    private static bool HasScrollBar(nint window, uint flag) =>
        (unchecked((uint)NativeMethods.GetWindowLongPointer(window, NativeMethods.WindowLongStyle)) & flag) != 0;

    private static void AssertNoContainerScrollBars(NativeConflictTextViewport viewport) =>
        Assert.IsFalse(HasScrollBar(viewport.Handle, NativeMethods.WindowStyleVerticalScroll | NativeMethods.WindowStyleHorizontalScroll),
            "没有首行留白时，容器不能与正文叠加两组滚动条。");

    private static void AssertSameScreenLine(nint[] editors, string[] texts, string line)
    {
        int expected = ClientTop(editors[1]) + TextY(editors[1], texts[1], line);
        for (int side = 0; side < 3; side++)
            Assert.AreEqual(expected, ClientTop(editors[side]) + TextY(editors[side], texts[side], line), $"第 {side + 1} 栏的 {line} 错位。");
    }
}
