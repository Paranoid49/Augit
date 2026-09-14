using System.Reflection;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeTitleBarInteractionTests
{
    private static readonly string[] FrameControls = ["_filesButton", "_gitButton", "_searchButton", "_terminalButton", "_historyButton",
        "_locateActiveFileButton", "_collapseTreeButton", "_treeOptionsButton", "_hideProjectButton"];

    [TestMethod]
    [DynamicData(nameof(状态矩阵))]
    public Task 全局入口与项目标题动作状态清晰且悬停不改变布局(int dpi, bool dark, int size) => RunAsync(dpi, dark, size, window =>
    {
        using TemporaryDirectory workspace = new();
        PrepareFrameWorkspace(window, workspace.FullPath);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        nint tree = window.FileTreeHandleForTest;
        _ = NativeMethods.SetFocus(tree);
        int layouts = window.LayoutInvocationCountForTest;
        foreach (string field in FrameControls)
        {
            nint button = Control(window, field);
            // 空工作区的 Git 动作可能禁用；先验证启用态，再单独验证禁用态，不执行其命令。
            _ = NativeMethods.EnableWindow(button, true);
            bool rail = Array.IndexOf(FrameControls, field) < 5, active = field == "_filesButton";
            uint normal = active ? palette.Accent : rail ? palette.Chrome : palette.Panel;
            Assert.AreEqual(normal, Background(button), field);
            MoveInside(button);
            Assert.AreEqual(button, window.HoveredFrameButtonForTest, $"{field} 可见={NativeMethods.IsWindowVisible(button)} 启用={NativeMethods.IsWindowEnabled(button)}");
            Assert.AreEqual(active ? palette.Accent : palette.Hover, Background(button), $"{field} 悬停不得覆盖激活态。");
            Assert.AreEqual(tree, NativeMethods.GetFocus());
            _ = NativeMethods.UpdateWindow(button);
            for (int i = 0; i < 200; i++) MoveInside(button);
            Assert.IsFalse(GetUpdateRectangle(button, out _, false));
            _ = NativeMethods.SetFocus(button);
            Assert.AreEqual(palette.Accent, Capture(button, (dc, rect) => NativeMethods.GetPixel(dc, rect.Right / 2, NativeTheme.Scale(2))));
            if (active)
                Assert.AreEqual(palette.Panel, Capture(button, (dc, rect) => NativeMethods.GetPixel(dc, rect.Right / 2, NativeTheme.Scale(1))), "激活蓝底之外须能看清焦点中性边。");
            _ = NativeMethods.EnableWindow(button, false);
            MoveInside(button);
            Assert.AreEqual((nint)0, window.HoveredFrameButtonForTest);
            Assert.AreEqual(rail ? palette.Chrome : palette.Panel, Background(button));
            _ = NativeMethods.EnableWindow(button, true);
            _ = NativeMethods.SetFocus(tree);
        }
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(0, window.LoadedDocumentCountForTest);
    });

    [TestMethod]
    [DynamicData(nameof(状态矩阵))]
    public Task 终端标题按字宽分配且工具按钮有焦点和局部反馈(int dpi, bool dark, int size) => RunAsync(dpi, dark, size, window =>
    {
        using NativeTerminalPanel panel = CreateTerminalHeader(window);
        nint title = TerminalControl(panel, "_titleLabel"), session = TerminalControl(panel, "_sessionLabel");
        nint close = TerminalControl(panel, "_sessionCloseButton"), more = TerminalControl(panel, "_moreButton"), hide = TerminalControl(panel, "_closeButton");
        _ = NativeMethods.SetWindowText(session, "Windows PowerShell 自定义长名称");
        panel.ApplyAppearance(new() { Theme = dark ? "Dark" : "Light", TextFontSize = size });
        panel.SetBounds(20, 100, NativeTheme.Scale(820), NativeTheme.Scale(400));
        Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + NativeTheme.Scale(4), Height(session));
        Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + NativeTheme.Scale(4), Height(title));
        int previousRight = 0;
        foreach (nint control in new[] { title, session, close, more, hide })
        {
            Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds));
            Assert.IsGreaterThanOrEqualTo(previousRight, bounds.Left, "标题文字与动作不得重叠。");
            previousRight = bounds.Right;
        }
        _ = NativeMethods.SetFocus(window.FileTreeHandleForTest);
        int layouts = window.LayoutInvocationCountForTest;
        NativeThemePalette palette = NativeTheme.Palette(dark);
        foreach (nint button in new[] { close, more, hide })
        {
            Assert.AreEqual(NativeTheme.Scale(24), Height(button), "图标不跟随大字号变大。");
            MoveInside(button);
            Assert.AreEqual(palette.Hover, Capture(button, (dc, rect) => NativeMethods.GetPixel(dc, rect.Right / 2, NativeTheme.Scale(4))));
            _ = NativeMethods.UpdateWindow(button);
            for (int i = 0; i < 200; i++) MoveInside(button);
            Assert.IsFalse(GetUpdateRectangle(button, out _, false));
            _ = NativeMethods.SetFocus(button);
            Assert.AreEqual(palette.Accent, Capture(button, (dc, rect) => NativeMethods.GetPixel(dc, rect.Right / 2, NativeTheme.Scale(2))));
            _ = NativeMethods.EnableWindow(button, false);
            Assert.AreEqual((nint)0, window.HoveredFrameButtonForTest);
            MoveInside(button);
            Assert.AreEqual((nint)0, window.HoveredFrameButtonForTest);
            Assert.AreEqual(button == close ? palette.PanelMuted : palette.Panel,
                Capture(button, (dc, rect) => NativeMethods.GetPixel(dc, rect.Right / 2, NativeTheme.Scale(4))));
            _ = NativeMethods.EnableWindow(button, true);
        }
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(0, panel.BrowserProcessId, "标题测试不得加载 WebView2。");
        Assert.AreEqual(0, panel.ShellProcessId, "标题测试不得启动 Shell 或 WSL。");
        MoveInside(more);
        panel.SetVisible(false);
        Assert.AreEqual((nint)0, window.HoveredFrameButtonForTest);
        Assert.IsFalse(window.FrameButtonHoverTrackingForTest);
        panel.SetVisible(true);
        MoveInside(more);
        panel.Dispose();
        Assert.AreEqual((nint)0, window.HoveredFrameButtonForTest);
        Assert.IsFalse(window.FrameButtonHoverTrackingForTest);
    });

    [TestMethod]
    public Task 主框架Enter保留命令且终端标题按视觉顺序循环Tab() => RunAsync(96, false, 13, window =>
    {
        using TemporaryDirectory workspace = new();
        PrepareFrameWorkspace(window, workspace.FullPath);
        nint files = Control(window, "_filesButton");
        _ = NativeMethods.SetFocus(files);
        _ = NativeMethods.SendMessage(files, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsFalse(NativeMethods.IsWindowVisible(window.FileTreeHandleForTest));
        _ = NativeMethods.SendMessage(files, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        Assert.IsTrue(NativeMethods.IsWindowVisible(window.FileTreeHandleForTest));
        using NativeTerminalPanel panel = CreateTerminalHeader(window);
        typeof(MainWindow).GetField("_terminalPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, panel);
        panel.SetBounds(20, 200, 800, 300);
        nint[] controls = [TerminalControl(panel, "_sessionCloseButton"), TerminalControl(panel, "_moreButton"), TerminalControl(panel, "_closeButton")];
        _ = NativeMethods.SetFocus(controls[0]);
        MethodInfo navigate = typeof(MainWindow).GetMethod("HandleTabNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (nint expected in new[] { controls[1], controls[2], controls[0] })
        {
            Assert.IsTrue((bool)navigate.Invoke(window, [false])!);
            Assert.AreEqual(expected, NativeMethods.GetFocus());
        }
        Assert.IsTrue((bool)navigate.Invoke(window, [true])!);
        Assert.AreEqual(controls[2], NativeMethods.GetFocus());
        _ = NativeMethods.EnableWindow(controls[1], false);
        _ = NativeMethods.SetFocus(controls[0]);
        Assert.IsTrue((bool)navigate.Invoke(window, [false])!);
        Assert.AreEqual(controls[2], NativeMethods.GetFocus());
    });

    private static int Height(nint control)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds));
        return bounds.Bottom - bounds.Top;
    }
    private static void PrepareFrameWorkspace(MainWindow window, string path)
    {
        // 仅建立标题与工具入口的工作区外壳；不创建文件监听或调用 Git。
        typeof(MainWindow).GetField("_workspaceRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, path);
        window.ShowFilesForTest();
    }
    private static nint TerminalControl(NativeTerminalPanel panel, string name) =>
        (nint)typeof(NativeTerminalPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
    private static NativeTerminalPanel CreateTerminalHeader(MainWindow window)
    {
        int Command(string name) => (int)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        ConstructorInfo constructor = typeof(NativeTerminalPanel).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (NativeTerminalPanel)constructor.Invoke([window.Handle, Command("CommandHideTerminal"),
            Command("TerminalSessionCloseControlIdentifier"), Command("CommandTerminalMore"), (Action<string>)(_ => { })]);
    }
}
