using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeTitleBarInteractionTests
{
    private static readonly string[] Controls = ["_openFolderButton", "_cloneButton", "_recentWorkspacesButton",
        "_currentFileButton", "_quickOpenButton", "_settingsButton", "_minimizeButton", "_maximizeButton", "_closeButton"];

    public static IEnumerable<object[]> 状态矩阵()
    {
        foreach (int dpi in new[] { 96, 120, 144 })
            foreach (bool dark in new[] { false, true })
                foreach (int size in new[] { 13, 40 }) yield return [dpi, dark, size];
    }

    [TestMethod]
    [DynamicData(nameof(状态矩阵))]
    public Task 标题栏背景悬停焦点按下和禁用遵守主题且不重排(int dpi, bool dark, int size) => RunAsync(dpi, dark, size, window =>
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        nint tree = window.FileTreeHandleForTest;
        _ = NativeMethods.SetFocus(tree);
        int layouts = window.LayoutInvocationCountForTest;
        foreach (string field in Controls)
        {
            nint button = Control(window, field);
            _ = NativeMethods.SendMessage(button, NativeMethods.WindowMessageMouseLeave, 0, 0);
            uint normal = field == "_cloneButton" ? Mix(palette.Chrome, palette.Accent, 8) : palette.Chrome;
            uint hovered = Mix(palette.Chrome,
                field is "_cloneButton" or "_recentWorkspacesButton" ? palette.Accent : palette.Text,
                field is "_cloneButton" or "_recentWorkspacesButton" ? 12 : field == "_currentFileButton" ? 7 : 8);
            Assert.AreEqual(normal, Background(button), $"{field} 默认背景。");
            MoveInside(button);
            Assert.AreEqual(button, window.HoveredTitleBarButtonForTest);
            Assert.AreEqual(hovered, Background(button), $"{field} 悬停背景。");
            Assert.AreEqual(tree, NativeMethods.GetFocus(), "悬停不能抢焦点。");
            _ = NativeMethods.UpdateWindow(button);
            for (int i = 0; i < 200; i++) MoveInside(button);
            Assert.IsFalse(GetUpdateRectangle(button, out _, false), "同一按钮内移动不能反复失效。");

            _ = NativeMethods.SetFocus(button);
            Assert.IsTrue(HasFocusBorder(button, palette.Accent), $"{field} 必须绘制实际焦点环。");
            _ = NativeMethods.SendMessage(button, 0x00F3, 1, 0);
            Assert.AreEqual(field == "_closeButton" ? 0x001C2BC4u : hovered, Background(button), $"{field} 按下背景。");
            Assert.IsTrue(HasFocusBorder(button, palette.Accent));
            _ = NativeMethods.SendMessage(button, 0x00F3, 0, 0);
            _ = NativeMethods.EnableWindow(button, false);
            Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
            Assert.IsFalse(window.TitleBarHoverTrackingForTest);
            MoveInside(button);
            Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
            Assert.AreEqual(normal, Background(button));
            Assert.IsFalse(HasFocusBorder(button, palette.Accent), "禁用不能保留焦点环。");
            _ = NativeMethods.EnableWindow(button, true);
            _ = NativeMethods.SetFocus(tree);
            Assert.IsFalse(HasFocusBorder(button, palette.Accent), "失焦清除旧边框。");
        }
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(0, window.LoadedDocumentCountForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 主菜单复用原入口状态且Enter与空格均能切换(bool dark) => RunAsync(96, dark, 13, window =>
    {
        nint menu = Control(window, "_openFolderButton"), workspace = Control(window, "_cloneButton");
        NativeThemePalette palette = NativeTheme.Palette(dark);
        _ = NativeMethods.SetFocus(menu);
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsTrue(window.MainMenuOpenForTest);
        Assert.AreEqual(workspace, Control(window, "_cloneButton"));
        Assert.AreEqual("文件", NativeMethods.GetWindowTextValue(workspace));
        Assert.AreEqual(palette.Chrome, Background(workspace), "内嵌菜单不能沿用工作区蓝色底。");
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, (nint)(1L << 30));
        Assert.IsTrue(window.MainMenuOpenForTest, "长按不重复切换。");
        MoveInside(workspace);
        Assert.AreEqual(Mix(palette.Chrome, palette.Text, 8), Background(workspace));
        _ = NativeMethods.SetFocus(workspace);
        Assert.IsTrue(HasFocusBorder(workspace, palette.Accent));
        Assert.IsTrue(window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape));
        Assert.IsFalse(window.MainMenuOpenForTest);
        _ = NativeMethods.SendMessage(workspace, NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.AreEqual(Mix(palette.Chrome, palette.Accent, 8), Background(workspace));
        _ = NativeMethods.SetFocus(menu);
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeySpace, 0);
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyUp, NativeMethods.VirtualKeySpace, 0);
        Assert.IsTrue(window.MainMenuOpenForTest, "保留原生空格激活。");
        window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape);
        _ = NativeMethods.EnableWindow(menu, false);
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsFalse(window.MainMenuOpenForTest, "禁用时 Enter 不得执行。");
        _ = NativeMethods.EnableWindow(menu, true);
        _ = NativeMethods.SetFocus(workspace);
        _ = NativeMethods.SendMessage(menu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsFalse(window.MainMenuOpenForTest, "发给非焦点按钮的 Enter 不得执行。");
    });

    [TestMethod]
    public Task 旧按钮移出不得清除新悬停且隐藏销毁释放跟踪() => RunAsync(96, false, 13, window =>
    {
        nint first = Control(window, "_cloneButton"), second = Control(window, "_quickOpenButton");
        MoveInside(first);
        MoveInside(second);
        _ = NativeMethods.SendMessage(first, NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.AreEqual(second, window.HoveredTitleBarButtonForTest);
        _ = NativeMethods.ShowWindow(second, NativeMethods.ShowHide);
        Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
        Assert.IsFalse(window.TitleBarHoverTrackingForTest);
        MoveInside(second);
        Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
        _ = NativeMethods.ShowWindow(second, NativeMethods.ShowNormal);
        MoveInside(second);
        _ = NativeMethods.SendMessage(second, NativeMethods.WindowMessageMouseMove, 0, (nint)(-1));
        Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
        MoveInside(second);
        Assert.IsTrue(NativeMethods.DestroyWindow(second));
        Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
        Assert.IsFalse(window.TitleBarHoverTrackingForTest);
        MoveInside(first);
        window.Close();
        Assert.AreEqual((nint)0, window.HoveredTitleBarButtonForTest);
        Assert.IsFalse(window.TitleBarHoverTrackingForTest);
    });

    private static async Task RunAsync(int dpi, bool dark, int size, Action<MainWindow> verify)
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            string family = NativeTheme.UiFontFamilyForTest;
            double previousSize = NativeTheme.UiFontSizeForTest;
            try
            {
                using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")),
                    new() { Theme = dark ? "Dark" : "Light", TextFontSize = size, Window = new() { Width = 1180, Height = 760 } });
                window.Show();
                try { verify(window); }
                finally { window.Close(); }
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { NativeTheme.ConfigureUiTypography(family, previousSize); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "标题栏测试线程未退出。"); }
    }

    private static nint Control(MainWindow window, string field) =>
        (nint)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static uint Mix(uint background, uint foreground, int percent)
    {
        uint result = 0;
        for (int shift = 0; shift <= 16; shift += 8)
            result |= (uint)Math.Round(((background >> shift & 255) * (100 - percent) + (foreground >> shift & 255) * percent) / 100d) << shift;
        return result;
    }
    private static void MoveInside(nint button) =>
        NativeMethods.SendMessage(button, NativeMethods.WindowMessageMouseMove, 0, (nint)((NativeTheme.Scale(8) << 16) | NativeTheme.Scale(8)));
    private static uint Background(nint button) => Capture(button, (dc, rectangle) =>
        NativeMethods.GetPixel(dc, rectangle.Right / 2, NativeTheme.Scale(3)));
    private static bool HasFocusBorder(nint button, uint accent) => Capture(button, (dc, rectangle) =>
    {
        for (int y = 0; y < NativeTheme.Scale(2); y++)
            if (NativeMethods.GetPixel(dc, rectangle.Right / 2, y) == accent) return true;
        return false;
    });
    private static T Capture<T>(nint button, Func<nint, NativeMethods.Rectangle, T> read)
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
            return read(dc, client);
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
    private static extern bool GetUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
