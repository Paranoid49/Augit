using System.Runtime.InteropServices;
using Augit.Core.Documents;

namespace Augit.App.Tests;

public sealed partial class NativeDocumentToolbarTests
{
    public static IEnumerable<object[]> 工具按钮状态矩阵()
    {
        foreach (int dpi in new[] { 96, 120, 144 })
            foreach (bool dark in new[] { false, true })
                foreach (int size in new[] { 13, 40 })
                    foreach (DocumentKind kind in new[] { DocumentKind.Text, DocumentKind.Json, DocumentKind.Png })
                        yield return [dpi, dark, size, kind];
    }

    [TestMethod]
    [DynamicData(nameof(工具按钮状态矩阵))]
    public void 文档图片与查找按钮绘制完整状态且悬停不改变阅读上下文(int dpi, bool dark, int size, DocumentKind kind)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using ViewScope scope = new(kind, dark: dark);
        scope.Appearance(size);
        if (kind == DocumentKind.Text) scope.View.ShowFindForTest("readonly");
        nint focus = NativeMethods.GetFocus();
        int[] commands = kind switch
        {
            DocumentKind.Text => [6, 7, 4, 5, 12, 13, 14, 9, 10, 11],
            DocumentKind.Json => [1, 2, 15],
            _ => [16, 17, 18],
        };
        var bounds = commands.Select(id => Bounds(scope.Child(id))).ToArray();
        nint editor = scope.Child(100);
        if (editor != 0) _ = NativeMethods.SendMessage(editor, 2160, 3, 1);
        var selection = editor == 0 ? default : (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0));
        string query = NativeMethods.GetWindowTextValue(scope.Child(20));
        int scans = scope.View.FindStatusScanCountForTest;
        NativeThemePalette palette = NativeTheme.Palette(dark);
        foreach (int id in commands)
        {
            nint button = scope.Child(id);
            bool selectedMode = kind == DocumentKind.Json && id == 2;
            uint normal = selectedMode ? NativeTheme.DocumentModeSelectedBackground(dark) : palette.Panel;
            Assert.AreEqual(normal, ButtonBackground(button), $"{id} 默认底色。");
            MoveOverButton(button);
            Assert.AreEqual(button, scope.View.HoveredToolbarButtonForTest);
            Assert.AreEqual(selectedMode ? normal : palette.Hover, ButtonBackground(button), $"{id} 悬停底色。");
            Assert.AreEqual(focus, NativeMethods.GetFocus(), "悬停不能抢走正文或输入焦点。");
            _ = NativeMethods.UpdateWindow(button);
            for (int i = 0; i < 200; i++) MoveOverButton(button);
            Assert.IsFalse(GetButtonUpdateRectangle(button, out _, false), "同一按钮内移动不重复失效。");
            _ = NativeMethods.SetFocus(button);
            Assert.AreEqual(palette.Accent, ButtonFocusPixel(button), $"{id} 焦点环必须可见。");
            _ = NativeMethods.SendMessage(button, 0x00F3, 1, 0);
            Assert.AreEqual(selectedMode ? normal : palette.Hover, ButtonBackground(button));
            _ = NativeMethods.SendMessage(button, 0x00F3, 0, 0);
            _ = NativeMethods.EnableWindow(button, false);
            MoveOverButton(button);
            Assert.AreEqual((nint)0, scope.View.HoveredToolbarButtonForTest);
            Assert.IsFalse(scope.View.ToolbarHoverTrackingForTest);
            Assert.AreEqual(normal, ButtonBackground(button), "禁用不能留下悬停底色。");
            Assert.AreNotEqual(palette.Accent, ButtonFocusPixel(button));
            _ = NativeMethods.EnableWindow(button, true);
            _ = NativeMethods.SetFocus(focus);
            Assert.AreNotEqual(palette.Accent, ButtonFocusPixel(button), "失焦清除边框。");
        }
        CollectionAssert.AreEqual(bounds, commands.Select(id => Bounds(scope.Child(id))).ToArray());
        Assert.AreEqual(editor, scope.Child(100));
        if (editor != 0)
        {
            Assert.AreEqual(selection, (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0)));
            Assert.IsTrue(scope.View.IsTextReadOnly);
        }
        Assert.AreEqual(query, NativeMethods.GetWindowTextValue(scope.Child(20)));
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 文本和查找选项使用淡蓝底且悬停不改变开关(bool dark)
    {
        using ViewScope scope = new(DocumentKind.Text, dark: dark);
        scope.View.ShowFindForTest("readonly");
        foreach (int id in new[] { 6, 7, 12, 13, 14 })
        {
            nint button = scope.Child(id);
            _ = NativeMethods.SendMessage(button, 0x00F5, 0, 0);
            Assert.AreEqual(NativeTheme.Palette(dark).AccentSoft, ButtonBackground(button));
            MoveOverButton(button);
            Assert.AreEqual(NativeTheme.Palette(dark).AccentSoft, ButtonBackground(button), "选中态高于悬停。");
            _ = NativeMethods.SendMessage(button, NativeMethods.WindowMessageMouseLeave, 0, 0);
            Assert.AreEqual(NativeTheme.Palette(dark).AccentSoft, ButtonBackground(button));
            _ = NativeMethods.SendMessage(button, 0x00F5, 0, 0);
            Assert.AreEqual(NativeTheme.Palette(dark).Panel, ButtonBackground(button));
        }
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 文档隐藏查找收起与销毁清除跟踪且文档间不串状态()
    {
        using ViewScope first = new(DocumentKind.Text), second = new(DocumentKind.Json);
        first.View.ShowFindForTest("readonly");
        MoveOverButton(first.Child(12));
        MoveOverButton(second.Child(1));
        Assert.AreEqual(first.Child(12), first.View.HoveredToolbarButtonForTest);
        Assert.AreEqual(second.Child(1), second.View.HoveredToolbarButtonForTest);
        first.View.HideFind();
        Assert.AreEqual((nint)0, first.View.HoveredToolbarButtonForTest);
        Assert.IsFalse(first.View.ToolbarHoverTrackingForTest);
        Assert.AreEqual(second.Child(1), second.View.HoveredToolbarButtonForTest);
        MoveOverButton(second.Child(2));
        _ = NativeMethods.SendMessage(second.Child(1), NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.AreEqual(second.Child(2), second.View.HoveredToolbarButtonForTest, "旧按钮晚到移出不清除新按钮。");
        second.View.SetVisible(false);
        Assert.AreEqual((nint)0, second.View.HoveredToolbarButtonForTest);
        MoveOverButton(second.Child(2));
        Assert.AreEqual((nint)0, second.View.HoveredToolbarButtonForTest);
        second.View.SetVisible(true);
        MoveOverButton(second.Child(2));
        _ = NativeMethods.SendMessage(second.Child(2), NativeMethods.WindowMessageMouseMove, 0, (nint)(-1));
        Assert.AreEqual((nint)0, second.View.HoveredToolbarButtonForTest);
        MoveOverButton(second.Child(1));
        Assert.IsTrue(NativeMethods.DestroyWindow(second.Child(1)));
        Assert.AreEqual((nint)0, second.View.HoveredToolbarButtonForTest);
        MoveOverButton(second.Child(2));
        second.View.Dispose();
        Assert.AreEqual((nint)0, second.View.HoveredToolbarButtonForTest);
        Assert.IsFalse(second.View.ToolbarHoverTrackingForTest);
    }

    private static void MoveOverButton(nint button) => NativeMethods.SendMessage(button,
        NativeMethods.WindowMessageMouseMove, 0, (nint)((NativeTheme.Scale(8) << 16) | NativeTheme.Scale(8)));
    private static uint ButtonBackground(nint button) => ReadButtonPixel(button, NativeTheme.Scale(4));
    private static uint ButtonFocusPixel(nint button) => ReadButtonPixel(button, NativeTheme.Scale(2));
    private static uint ReadButtonPixel(nint button, int y)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(button, out NativeMethods.Rectangle client));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = client.Right,
                Height = -client.Bottom,
                Planes = 1,
                BitCount = 32,
            }
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        Assert.AreNotEqual((nint)0, bitmap);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            _ = NativeMethods.SendMessage(button, 0x0318, (nuint)dc, 0x000C);
            return NativeMethods.GetPixel(dc, client.Right / 2, y);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetButtonUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
