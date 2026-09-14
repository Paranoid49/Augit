using System.Runtime.InteropServices;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    [DataRow(96, "Light", 13)]
    [DataRow(120, "Light", 13)]
    [DataRow(144, "Light", 13)]
    [DataRow(96, "Dark", 13)]
    [DataRow(120, "Dark", 13)]
    [DataRow(144, "Dark", 13)]
    [DataRow(96, "Light", 40)]
    [DataRow(120, "Light", 40)]
    [DataRow(144, "Light", 40)]
    [DataRow(96, "Dark", 40)]
    [DataRow(120, "Dark", 40)]
    [DataRow(144, "Dark", 40)]
    public Task 冲突动作悬停焦点按下禁用不改变正文或布局(int dpi, string theme, int size) => RunAsync((dialog, service) =>
    {
        NativeThemePalette palette = NativeTheme.Palette(theme == "Dark");
        nint editor = dialog.ResultHandleForTest;
        _ = NativeMethods.SetFocus(editor);
        _ = NativeMethods.SendMessage(editor, 2160, 3, 1);
        string text = dialog.ResultTextForTest!;
        var selection = (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0));
        var bodyBounds = Bounds(editor);
        int parses = dialog.ResultParseCountForTest;
        var buttons = Enumerable.Range(1, 9).Select(id => Item(dialog, id)).ToArray();
        var bounds = buttons.Select(Bounds).ToArray();
        foreach (nint button in buttons)
        {
            bool primary = button == Item(dialog, 6);
            Assert.AreEqual(primary ? palette.Accent : palette.Panel, ActionPixel(button, 5), $"按钮 {Array.IndexOf(buttons, button) + 1} 默认底色。");
            HoverAction(button);
            Assert.AreEqual(button, dialog.HoveredActionButtonForTest);
            Assert.AreEqual(editor, NativeMethods.GetFocus(), "悬停不得抢走中央结果区焦点。");
            Assert.AreEqual(primary ? palette.Accent : palette.Hover, ActionPixel(button, 5));
            _ = NativeMethods.UpdateWindow(button);
            for (int index = 0; index < 100; index++) HoverAction(button);
            Assert.IsFalse(GetConflictButtonUpdateRectangle(button, out _, false), "同一按钮内移动不得重复失效。");
            _ = NativeMethods.SetFocus(button);
            Assert.AreEqual(palette.Accent, ActionPixel(button, 2), "获得焦点必须显示独立蓝色内框。");
            if (primary) Assert.AreEqual(palette.Panel, ActionPixel(button, 1), "主要动作须加中性外环以区分蓝底与焦点。");
            _ = NativeMethods.SendMessage(button, 0x00F3, 1, 0);
            Assert.AreEqual(primary ? palette.Accent : palette.Hover, ActionPixel(button, 5));
            _ = NativeMethods.SendMessage(button, 0x00F3, 0, 0);
            _ = NativeMethods.EnableWindow(button, false);
            HoverAction(button);
            Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
            Assert.IsFalse(dialog.ActionHoverTrackingForTest);
            Assert.AreEqual(button == Item(dialog, 8) ? palette.Panel : palette.PanelMuted, ActionPixel(button, 5));
            Assert.AreNotEqual(palette.Accent, ActionPixel(button, 2), "禁用不能保留焦点环。");
            _ = NativeMethods.EnableWindow(button, true);
            _ = NativeMethods.SetFocus(editor);
            if (!primary) Assert.AreNotEqual(palette.Accent, ActionPixel(button, 2));
        }
        CollectionAssert.AreEqual(bounds, buttons.Select(Bounds).ToArray());
        Assert.AreEqual(bodyBounds, Bounds(editor));
        Assert.AreEqual(text, dialog.ResultTextForTest);
        Assert.AreEqual(selection, (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0)));
        Assert.AreEqual(parses, dialog.ResultParseCountForTest);
        Assert.IsEmpty(service.Saves);
        return Task.CompletedTask;
    }, unresolved: true, dpi: dpi, theme: theme, size: size, codeSize: 13, physicalPixels: true);

    [TestMethod]
    public Task 冲突动作隐藏关闭销毁和旧移出消息清除各自跟踪() => RunAsync((dialog, service) =>
    {
        nint first = Item(dialog, 4), second = Item(dialog, 5);
        HoverAction(first);
        HoverAction(second);
        _ = NativeMethods.SendMessage(first, NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.AreEqual(second, dialog.HoveredActionButtonForTest);
        _ = NativeMethods.ShowWindow(dialog.HandleForTest, NativeMethods.ShowHide);
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        HoverAction(second);
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        _ = NativeMethods.ShowWindow(dialog.HandleForTest, NativeMethods.ShowNormal);
        HoverAction(second);
        _ = NativeMethods.SendMessage(second, NativeMethods.WindowMessageMouseMove, 0, (nint)(-1));
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        HoverAction(first);
        _ = NativeMethods.EnableWindow(dialog.HandleForTest, false);
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        HoverAction(first);
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        _ = NativeMethods.EnableWindow(dialog.HandleForTest, true);
        HoverAction(first);
        Assert.IsTrue(NativeMethods.DestroyWindow(first));
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        HoverAction(second);
        dialog.CloseForTest();
        Assert.AreEqual((nint)0, dialog.HoveredActionButtonForTest);
        Assert.IsFalse(dialog.ActionHoverTrackingForTest);
        return Task.CompletedTask;
    }, unresolved: true);

    private static void HoverAction(nint button) => NativeMethods.SendMessage(button,
        NativeMethods.WindowMessageMouseMove, 0, (nint)((NativeTheme.Scale(8) << 16) | NativeTheme.Scale(8)));

    private static uint ActionPixel(nint button, int y)
    {
        FramePixels pixels = FramePixels.Capture(button);
        // 背景采样在文字左侧的内距中；字形抗锯齿会覆盖按钮上沿附近的中央像素。
        return pixels.At(y == 5 ? NativeTheme.Scale(8) : pixels.Width / 2, NativeTheme.Scale(y));
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConflictButtonUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle,
        [MarshalAs(UnmanagedType.Bool)] bool erase);
}
