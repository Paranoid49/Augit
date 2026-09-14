using System.Reflection;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    private static readonly int[] HistoryFontSizes = [40, 19, 13];
    private static readonly int[] HistoryWindowWidths = [1024, 1645];
    private static readonly int[] HistoryListIdentifiers = [1, 2, 3];
    private static readonly int[] HistoryTextIdentifiers = [60, 62, 63, 64];
    private static readonly string[] HistorySettingsThemes = ["Dark", "Light"];

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public async Task 历史字号文件历史工具栏不重叠且详情收放保持上下文(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            service.CompleteDiffImmediately = true;
            string? originalSelection = history.SelectedCommitHashForTest;
            window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot!, "a.txt"));
            await WaitUntilAsync(() => history.FileHistoryModeForTest && !history.OperationRunningForTest && history.FileHistoryEditorVisibleForTest);
            string? selected = history.SelectedCommitHashForTest;
            nint list = history.HistoryListHandleForTest;
            int reads = service.PageReads;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            ApplicationSettings current = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
            try
            {
                typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                    [current, current with { TextFontSize = 40 }]);
                _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1024), NativeTheme.Scale(900), true);
                var label = HistoryControlBounds(HistoryChild(history.Handle, 67));
                var clear = HistoryControlBounds(HistoryChild(history.Handle, 37));
                var tab = HistoryControlBounds(HistoryChild(history.Handle, 66));
                var log = HistoryControlBounds(HistoryChild(history.Handle, 60));
                int lineHeight = MeasureHistoryText("国Ag", NativeTheme.UiFont).Height;
                Assert.IsGreaterThanOrEqualTo(lineHeight, label.Bottom - label.Top);
                Assert.IsGreaterThanOrEqualTo(lineHeight, tab.Bottom - tab.Top);
                Assert.IsLessThanOrEqualTo(clear.Left, label.Right);
                Assert.IsLessThanOrEqualTo(tab.Left, log.Right);
                Assert.IsLessThanOrEqualTo(HistoryControlBounds(list).Top, clear.Bottom);
                Assert.IsTrue(history.ToggleDetailsForTest());
                Assert.IsFalse(history.FileHistoryEditorVisibleForTest);
                Assert.IsTrue(history.ToggleDetailsForTest());
                Assert.IsTrue(history.FileHistoryEditorVisibleForTest);
                Assert.AreSame(history, window.HistoryPanelForTest);
                Assert.AreEqual(list, history.HistoryListHandleForTest);
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                Assert.AreEqual(reads, service.PageReads);
                _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 37), 0x00F5, 0, 0);
                Assert.IsFalse(history.FileHistoryModeForTest);
                Assert.AreEqual(originalSelection, history.SelectedCommitHashForTest);
            }
            finally
            {
                typeof(MainWindow).GetField("_settings", flags)!.SetValue(window, current);
                typeof(MainWindow).GetMethod("ApplyAppearance", flags)!.Invoke(window, null);
            }
        });
    }

    [TestMethod]
    public Task 历史字号设置确认保留提交草稿和已打开的比较() => RunAsync(async (window, history, service) =>
    {
        window.ShowGitForTest();
        await WaitUntilAsync(() => window.GitPanelForTest is { RefreshingForTest: false, RepositoryKind: GitRepositoryKind.WorkingTree });
        NativeGitPanel changes = window.GitPanelForTest!;
        changes.SetCommitMessageForTest("fix: 设置后保留提交草稿");
        Task open = OpenFile(history, "a.txt");
        service.Calls[0].Complete();
        await open;
        await WaitUntilAsync(() => window.ComparisonViewForTest?.LoadingForTest == false);
        NativeGitComparisonView comparison = window.ComparisonViewForTest!;
        string body = comparison.BodyTextForTest;
        string? selected = history.SelectedCommitHashForTest;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        ApplicationSettings original = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        MethodInfo apply = typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!;
        foreach (string theme in HistorySettingsThemes)
        {
            apply.Invoke(window, [original, original with { Theme = theme, FontSize = 19 }]);
            Assert.AreSame(changes, window.GitPanelForTest);
            Assert.AreSame(history, window.HistoryPanelForTest);
            Assert.AreSame(comparison, window.ComparisonViewForTest);
            Assert.AreEqual("fix: 设置后保留提交草稿", changes.CommitMessageForTest);
            Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            Assert.AreEqual(body, comparison.BodyTextForTest);
            Assert.HasCount(1, service.Calls, "应用外观不得再次读取已有比较。");
            foreach (object panel in new object[] { changes, history, comparison })
            {
                ApplicationSettings settings = (ApplicationSettings)panel.GetType().GetField("_settings", flags)!.GetValue(panel)!;
                Assert.AreEqual(theme, settings.Theme, "保留面板必须接收新主题，不能继续使用创建时的设置。");
                Assert.AreEqual(19, settings.FontSize);
            }
        }
    });

    [TestMethod]
    public Task 历史字号设置中Git路径改变仍重建服务() => RunAsync(async (window, history, _) =>
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        ApplicationSettings current = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        // 模拟从先前的错误路径切回自动检测，不改动任何系统 Git 配置。
        typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
            [current with { GitExecutablePath = Path.Combine(window.WorkspaceRoot!, "missing.exe") }, current]);
        Assert.AreNotSame(history, window.HistoryPanelForTest);
        await WaitUntilAsync(() => window.HistoryPanelForTest is { OperationRunningForTest: false, EntryCount: 2 });
    });

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 历史字号切换容纳文字并保持列表上下文和固定图标(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            var entries = CreatePagedHistory();
            service.Entries = entries;
            service.ReadPage = (request, _) => Task.FromResult(HistoryPage(entries, request.Page));
            window.RequestHistoryRefreshForTest();
            await WaitUntilAsync(() => history.EntryCount == 100 && !history.OperationRunningForTest);
            nint list = history.HistoryListHandleForTest;
            _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetTopIndex, 48, 0);
            ClickRow(list, 48);
            await WaitUntilAsync(() => history.CommitDetailsLoadedForTest);
            string? selected = history.SelectedCommitHashForTest, top = history.HistoryListTopHashForTest;
            int graphBuilds = history.CommitGraphBuildCountForTest, reads = service.PageReads;
            int resets = history.HistoryListResetCountForTest;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo settingsField = typeof(MainWindow).GetField("_settings", flags)!;
            ApplicationSettings original = (ApplicationSettings)settingsField.GetValue(window)!;
            MethodInfo apply = typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!;
            try
            {
                foreach (int size in HistoryFontSizes)
                {
                    apply.Invoke(window, [original, original with { TextFontSize = size }]);
                    Assert.AreSame(history, window.HistoryPanelForTest, "设置确认不应销毁已有历史上下文。");
                    Assert.AreEqual(list, history.HistoryListHandleForTest);
                    Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                    Assert.AreEqual(top, history.HistoryListTopHashForTest);
                    foreach (int width in HistoryWindowWidths)
                    {
                        _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(width), NativeTheme.Scale(900), true);
                        int textHeight = MeasureHistoryText("国Ag", NativeTheme.UiFont).Height;
                        foreach (int identifier in HistoryListIdentifiers)
                        {
                            nint control = HistoryChild(history.Handle, identifier);
                            int rowHeight = unchecked((int)NativeMethods.SendMessage(control, 0x01A1, 0, 0));
                            Assert.IsGreaterThanOrEqualTo(textHeight + NativeTheme.Scale(4), rowHeight, $"列表 {identifier} 裁切字号 {size}。");
                        }
                        foreach (int identifier in HistoryTextIdentifiers)
                        {
                            var bounds = HistoryControlBounds(HistoryChild(history.Handle, identifier));
                            Assert.IsGreaterThanOrEqualTo(textHeight, bounds.Bottom - bounds.Top, $"文字控件 {identifier} 裁切。");
                        }
                        var title = HistoryControlBounds(HistoryChild(history.Handle, 62));
                        var tab = HistoryControlBounds(HistoryChild(history.Handle, 60));
                        Assert.IsLessThanOrEqualTo(tab.Left, title.Right);
                        Assert.AreEqual(MeasureHistoryText("Git", NativeTheme.UiMediumFont).Width, title.Right - title.Left);
                        var input = HistoryControlBounds(HistoryChild(history.Handle, 63));
                        var listBounds = HistoryControlBounds(list);
                        Assert.IsLessThanOrEqualTo(listBounds.Top, input.Bottom);
                        foreach (nint button in history.VisibleSideToolbarButtonsForTest)
                        {
                            var bounds = HistoryControlBounds(button);
                            Assert.AreEqual(NativeTheme.Scale(28), bounds.Bottom - bounds.Top, "图标命中区不随字号膨胀。");
                        }
                        Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                        Assert.AreEqual(top, history.HistoryListTopHashForTest);
                    }
                }
                Assert.AreEqual(reads, service.PageReads, "字号和窗口布局不应重查 Git。");
                Assert.AreEqual(graphBuilds, history.CommitGraphBuildCountForTest);
                Assert.AreEqual(resets, history.HistoryListResetCountForTest);
                Assert.AreEqual(NativeTheme.Scale(26), unchecked((int)NativeMethods.SendMessage(list, 0x01A1, 0, 0)));
            }
            finally
            {
                settingsField.SetValue(window, original);
                typeof(MainWindow).GetMethod("ApplyAppearance", flags)!.Invoke(window, null);
            }
        }, theme);
    }

    private static NativeMethods.Rectangle HistoryControlBounds(nint control)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle rectangle));
        return rectangle;
    }

    private static (int Width, int Height) MeasureHistoryText(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(0);
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle bounds = default;
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(0, dc);
        }
    }
}
