using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeMainWindowTypographyTests
{
    private static readonly string[] TextControls = ["_cloneButton", "_recentWorkspacesButton", "_currentFileButton", "_projectHeader", "_statusBar"];
    private static readonly string[] RailControls = ["_filesButton", "_gitButton", "_searchButton"];
    private static readonly string[] MenuControls = ["_cloneButton", "_recentWorkspacesButton", "_currentFileButton", "_quickOpenButton", "_settingsButton"];
    private static readonly int[] ResizedFonts = [40, 19, 13];

    public static IEnumerable<object[]> 字号主题矩阵()
    {
        foreach (int dpi in new[] { 96, 120, 144 })
            foreach (int size in new[] { 9, 13, 19, 40 })
                foreach (string theme in new[] { "Light", "Dark" })
                    yield return [dpi, size, theme];
    }

    [TestMethod]
    [DataRow(96, 13)]
    [DataRow(120, 13)]
    [DataRow(144, 13)]
    [DataRow(96, 40)]
    [DataRow(120, 40)]
    [DataRow(144, 40)]
    public void 大字号底部工具窗口最小高度随实际行高扩展(int dpi, int size)
    {
        using IDisposable audit = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            int minimum = MainWindow.DefaultBottomPanelMinimumHeightForTest;
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight * 4 + NativeTheme.Scale(80), minimum);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(180), minimum);
            Assert.IsGreaterThanOrEqualTo(minimum, MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(654)));
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    [DynamicData(nameof(字号主题矩阵))]
    public Task 主窗口各文字区域容纳实际字高且图标不随字号放大(int dpi, int size, string theme) =>
        RunAsync(dpi, size, theme, async (window, workspace) =>
        {
            string path = Path.Combine(workspace, "文件060.txt");
            await window.OpenDocumentForTestAsync(path);
            int fontHeight = Measure("国Ag", NativeTheme.UiFont).Height;
            Assert.IsLessThanOrEqualTo(NativeTheme.Scale(17), Measure("A", NativeTheme.SmallBrandFont).Height);
            Assert.IsLessThanOrEqualTo(NativeTheme.Scale(20), Measure("A", NativeTheme.BrandFont).Height);
            foreach (string field in TextControls)
            {
                NativeMethods.Rectangle bounds = Bounds(Control(window, field));
                Assert.IsGreaterThanOrEqualTo(fontHeight + NativeTheme.Scale(2), bounds.Bottom - bounds.Top, $"{field} 必须完整容纳界面文字。");
            }
            NativeMethods.Rectangle tabs = Bounds(Control(window, "_documentTabs"));
            Assert.IsGreaterThanOrEqualTo(fontHeight + NativeTheme.Scale(14), tabs.Bottom - tabs.Top, "标签须在背景内保留完整文字和上下留白。");
            Assert.IsGreaterThanOrEqualTo(fontHeight + NativeTheme.Scale(8), window.FileTreeItemHeightForTest);
            Assert.AreEqual(0, window.FileTreeItemHeightForTest % 2);
            if (size == 13)
            {
                Assert.AreEqual(NativeTheme.Scale(44), MainWindow.ToolbarHeightForTest);
                Assert.AreEqual(NativeTheme.Scale(42), MainWindow.TabHeightForTest);
                Assert.AreEqual(NativeTheme.Scale(22), MainWindow.StatusHeightForTest);
                int rowHeight = NativeTheme.Scale(27);
                Assert.AreEqual(rowHeight + (rowHeight & 1), window.FileTreeItemHeightForTest);
            }
            int formatWidth = (int)typeof(MainWindow).GetField("_statusFormatWidth", Fields)!.GetValue(window)!;
            Assert.AreEqual(window.StatusFieldsForTest.Sum(text => Measure(text, NativeTheme.UiFont).Width + NativeTheme.Scale(12)), formatWidth);
            nint document = Document(window).Handle;
            NativeMethods.Rectangle breadcrumb = Bounds(GetDialogItem(document, 30));
            Assert.IsGreaterThanOrEqualTo(fontHeight + NativeTheme.Scale(4), breadcrumb.Bottom - breadcrumb.Top);
            foreach (string field in RailControls)
            {
                NativeMethods.Rectangle icon = Bounds(Control(window, field));
                Assert.AreEqual(NativeTheme.Scale(32), icon.Right - icon.Left);
                Assert.AreEqual(NativeTheme.Scale(32), icon.Bottom - icon.Top);
            }
            var toolbar = Bounds(Control(window, "_cloneButton"));
            var treeHeader = Bounds(Control(window, "_projectHeader"));
            Assert.IsLessThanOrEqualTo(treeHeader.Top, toolbar.Bottom);
            Assert.IsLessThanOrEqualTo(tabs.Bottom, breadcrumb.Top);
            Assert.IsTrue(window.ActiveDocumentIsReadOnly);
        });

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public Task 应用字号后复用标签正文并保持树选择和滚动锚点(int dpi) =>
        RunAsync(dpi, 13, "Light", async (window, workspace) =>
        {
            string path = Path.Combine(workspace, "文件060.txt");
            await window.OpenDocumentForTestAsync(path);
            Assert.IsTrue(window.SelectTreePathForTest(path));
            nint tree = window.FileTreeHandleForTest;
            nint selected = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0);
            _ = NativeMethods.SendMessage(tree, NativeMethods.TreeViewSelectItem, NativeMethods.TreeViewFirstVisible, selected);
            nint first = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0);
            NativeDocumentView view = Document(window);
            nint editor = GetDialogItem(view.Handle, 100);
            nint originalCodeSize = NativeMethods.SendMessage(editor, 2062, 32, 0);
            Assert.IsGreaterThan((nint)0, originalCodeSize);
            _ = NativeMethods.SendMessage(editor, 2160, 10, 5);
            _ = NativeMethods.SendMessage(editor, 2613, 30, 0);
            var textPosition = (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0), NativeMethods.SendMessage(editor, 2152, 0, 0));
            _ = NativeMethods.SetFocus(tree);
            int defaultRow = window.FileTreeItemHeightForTest;
            int defaultTabHeight = Height(Control(window, "_documentTabs"));
            foreach (int size in ResizedFonts)
            {
                ApplicationSettings settings = (ApplicationSettings)typeof(MainWindow).GetField("_settings", Fields)!.GetValue(window)!;
                typeof(MainWindow).GetField("_settings", Fields)!.SetValue(window, settings with { TextFontSize = size });
                typeof(MainWindow).GetMethod("ApplyAppearance", Fields)!.Invoke(window, null);
                Assert.AreEqual(view, Document(window));
                Assert.AreEqual(editor, GetDialogItem(view.Handle, 100));
                Assert.AreEqual(tree, NativeMethods.GetFocus());
                Assert.AreEqual(selected, NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0));
                Assert.AreEqual(first, NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewFirstVisible, 0));
                Assert.AreEqual(textPosition, (NativeMethods.SendMessage(editor, 2008, 0, 0), NativeMethods.SendMessage(editor, 2009, 0, 0), NativeMethods.SendMessage(editor, 2152, 0, 0)));
                Assert.AreEqual(originalCodeSize, NativeMethods.SendMessage(editor, 2062, 32, 0));
                Assert.IsGreaterThanOrEqualTo(Measure("国Ag", NativeTheme.UiFont).Height + NativeTheme.Scale(8), window.FileTreeItemHeightForTest);
            }
            Assert.AreEqual(defaultRow, window.FileTreeItemHeightForTest);
            Assert.AreEqual(defaultTabHeight, Height(Control(window, "_documentTabs")));
        });

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public Task 大字号主菜单各项完整显示且不覆盖窗口按钮(int dpi) =>
        RunAsync(dpi, 40, "Dark", (window, workspace) =>
        {
            _ = NativeMethods.SendMessage(Control(window, "_openFolderButton"), 0x00F5, 0, 0);
            int previousRight = 0;
            foreach (string field in MenuControls)
            {
                nint control = Control(window, field);
                NativeMethods.Rectangle bounds = Bounds(control);
                var text = Measure(NativeMethods.GetWindowTextValue(control), NativeTheme.UiFont);
                Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(12), bounds.Right - bounds.Left);
                Assert.IsGreaterThanOrEqualTo(text.Height + NativeTheme.Scale(4), bounds.Bottom - bounds.Top);
                Assert.IsGreaterThanOrEqualTo(previousRight, bounds.Left);
                previousRight = bounds.Right;
            }
            Assert.IsLessThanOrEqualTo(Bounds(Control(window, "_minimizeButton")).Left, previousRight);
            return Task.CompletedTask;
        });

    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private static nint Control(MainWindow window, string field) => (nint)typeof(MainWindow).GetField(field, Fields)!.GetValue(window)!;
    private static NativeDocumentView Document(MainWindow window) => (NativeDocumentView)typeof(MainWindow).GetProperty("ActiveDocument", Fields)!.GetValue(window)!;
    private static int Height(nint control) { var bounds = Bounds(control); return bounds.Bottom - bounds.Top; }
    private static NativeMethods.Rectangle Bounds(nint control)
    {
        Assert.AreNotEqual((nint)0, control);
        Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle bounds));
        return bounds;
    }
    private static (int Width, int Height) Measure(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(0);
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(0, dc); }
    }

    private static async Task RunAsync(int dpi, int size, string theme, Func<MainWindow, string, Task> scenario)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string content = string.Join('\n', Enumerable.Range(0, 150).Select(i => $"只读文本第 {i} 行"));
        for (int i = 0; i < 100; i++) await File.WriteAllTextAsync(Path.Combine(workspace, $"文件{i:000}.txt"), content);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            string previousFamily = NativeTheme.UiFontFamilyForTest;
            double previousSize = NativeTheme.UiFontSizeForTest;
            Exception? failure = null;
            try
            {
                using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")),
                    new() { Theme = theme, FontSize = 13, TextFontSize = size, Window = new() { Width = 1024, Height = 640 } });
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        window.ShowFilesForTest();
                        await window.ExpandTreePathForTestAsync(workspace);
                        await scenario(window, workspace);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "字号测试窗口没有退出。");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint GetDialogItem(nint window, int identifier);
}
