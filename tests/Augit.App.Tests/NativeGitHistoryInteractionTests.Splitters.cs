using System.Reflection;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    private static readonly int[] MainSplitterDirections = [1, -1];
    private static readonly string[] MainSplitterCancelReasons = ["捕获转移", "取消消息", "Esc", "隐藏", "禁用", "折叠底部"];

    [TestMethod]
    [DataRow(96, 13)]
    [DataRow(120, 13)]
    [DataRow(144, 13)]
    [DataRow(96, 40)]
    [DataRow(120, 40)]
    [DataRow(144, 40)]
    public async Task 分隔条拖动无初始跳动且共享显示边界和保存尺寸(int dpi, int size)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ApplicationSettings original = (ApplicationSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
            try
            {
                typeof(MainWindow).GetMethod("ApplyConfirmedSettings", flags)!.Invoke(window,
                    [original, original with { TextFontSize = size }]);
                window.ShowFilesForTest();
                _ = NativeMethods.MoveWindow(window.Handle, 100, 100, NativeTheme.Scale(1645), NativeTheme.Scale(1000), true);
                window.ResizeProjectPanelForTest(NativeTheme.Scale(320));
                nint list = history.HistoryListHandleForTest;
                _ = NativeMethods.SetFocus(list);
                string? selected = history.SelectedCommitHashForTest;
                string? top = history.HistoryListTopHashForTest;
                int reads = service.PageReads, graphBuilds = history.CommitGraphBuildCountForTest;
                int layouts = window.LayoutInvocationCountForTest;
                int project = window.ProjectPanelWidthForTest, bottom = window.BottomPanelHeightForTest;

                Assert.IsFalse(window.DragProjectSplitterForTest(0), "按下后原地移动不能把间隙宽度加到项目窗。");
                Assert.IsFalse(window.DragBottomSplitterForTest(0), "按下后原地移动不能把间隙或表面内缩加到底部窗。");
                Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, "没有实际尺寸变化时不重新布局。");

                int delta = NativeTheme.Scale(24);
                Assert.IsTrue(window.DragProjectSplitterForTest(delta));
                Assert.AreEqual(project + delta, window.ProjectPanelWidthForTest);
                Assert.IsTrue(window.DragBottomSplitterForTest(delta));
                Assert.AreEqual(bottom + delta, window.BottomPanelHeightForTest);

                // 在两端持续移动只改变一次布局；同一边界不能反复重绘全窗。
                foreach (int direction in MainSplitterDirections)
                {
                    _ = window.DragProjectSplitterForTest(NativeTheme.Scale(10000) * direction);
                    _ = window.DragBottomSplitterForTest(NativeTheme.Scale(10000) * direction);
                    layouts = window.LayoutInvocationCountForTest;
                    for (int repeat = 0; repeat < 8; repeat++)
                    {
                        Assert.IsFalse(window.DragProjectSplitterForTest(delta * direction));
                        Assert.IsFalse(window.DragBottomSplitterForTest(delta * direction));
                    }
                    Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
                }
                Assert.AreEqual(MainWindow.DefaultBottomPanelMinimumHeightForTest,
                    window.BottomPanelHeightForTest + MainWindow.SurfaceContentInsetForTest * 2);

                typeof(MainWindow).GetMethod("SaveSettings", flags)!.Invoke(window, null);
                SettingsStore store = (SettingsStore)typeof(MainWindow).GetField("_settingsStore", flags)!.GetValue(window)!;
                ApplicationSettings saved = await store.LoadAsync();
                Assert.AreEqual(NativeTheme.Unscale(window.ProjectPanelWidthForTest), saved.ToolWindows.ProjectPanelWidth);
                Assert.AreEqual(NativeTheme.Unscale(window.BottomPanelHeightForTest + MainWindow.SurfaceContentInsetForTest * 2),
                    saved.ToolWindows.BottomPanelHeight, "保存与实际显示必须使用同一大字号最小高度。");

                // 旧的超大保存尺寸被当前视口夹取后，也应从可见边界立即反向移动。
                window.ResizeProjectPanelForTest(NativeTheme.Scale(2000));
                window.ResizeBottomPanelForTest(NativeTheme.Scale(2000));
                project = window.ProjectPanelWidthForTest;
                bottom = window.BottomPanelHeightForTest;
                Assert.IsTrue(window.DragProjectSplitterForTest(-delta));
                Assert.AreEqual(project - delta, window.ProjectPanelWidthForTest);
                Assert.IsTrue(window.DragBottomSplitterForTest(-delta));
                Assert.AreEqual(bottom - delta, window.BottomPanelHeightForTest);
                Assert.AreEqual(list, NativeMethods.GetFocus());
                Assert.AreEqual(list, history.HistoryListHandleForTest);
                Assert.AreEqual(selected, history.SelectedCommitHashForTest);
                Assert.AreEqual(top, history.HistoryListTopHashForTest);
                Assert.AreEqual(reads, service.PageReads);
                Assert.AreEqual(graphBuilds, history.CommitGraphBuildCountForTest);
            }
            finally
            {
                typeof(MainWindow).GetField("_settings", flags)!.SetValue(window, original);
                typeof(MainWindow).GetMethod("ApplyAppearance", flags)!.Invoke(window, null);
            }
        }, preciseDpi: true);
    }

    [TestMethod]
    public Task 分隔条结束接纳释放位置且取消后忽略旧移动() => RunAsync(async (window, history, service) =>
    {
        _ = NativeMethods.MoveWindow(window.Handle, 100, 100, 1645, 1000, true);
        static nint Pack(int x, int y) => unchecked((nint)((ushort)x | (uint)(ushort)y << 16));
        NativeMethods.Point Begin()
        {
            var bounds = HistoryControlBounds(history.Handle);
            NativeMethods.Point point = new() { X = bounds.Left + NativeTheme.Scale(80), Y = bounds.Top - NativeTheme.Scale(2) };
            Assert.IsTrue(NativeMethods.ScreenToClient(window.Handle, ref point));
            _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageLeftButtonDown, 1, Pack(point.X, point.Y));
            Assert.AreEqual(window.Handle, NativeMethods.GetCapture());
            return point;
        }

        int height = window.BottomPanelHeightForTest;
        NativeMethods.Point start = Begin();
        _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageLeftButtonUp, 0, Pack(start.X, start.Y - 24));
        Assert.AreEqual(height + 24, window.BottomPanelHeightForTest);
        Assert.AreNotEqual(window.Handle, NativeMethods.GetCapture());

        foreach (string reason in MainSplitterCancelReasons)
        {
            start = Begin();
            switch (reason)
            {
                case "捕获转移": _ = NativeMethods.SetCapture(history.HistoryListHandleForTest); break;
                case "取消消息": _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageCancelMode, 0, 0); break;
                case "Esc":
                    window.ActiveDocumentViewForTest!.ShowFindForTest("内容");
                    _ = NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
                    await WaitUntilAsync(() => NativeMethods.GetCapture() != window.Handle);
                    Assert.IsTrue(window.ActiveDocumentViewForTest.FindOverlayVisibleForTest, "Esc 只结束拖动，不能同时关闭查找条。");
                    break;
                case "隐藏": _ = NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowHide); break;
                case "禁用": _ = NativeMethods.EnableWindow(window.Handle, false); break;
                case "折叠底部": _ = NativeMethods.SendMessage(HistoryChild(history.Handle, 23), 0x00F5, 0, 0); break;
            }
            Assert.AreNotEqual(window.Handle, NativeMethods.GetCapture(), reason);
            height = window.BottomPanelHeightForTest;
            int layouts = window.LayoutInvocationCountForTest;
            _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageMouseMove, 1, Pack(start.X, start.Y - 24));
            _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageLeftButtonUp, 0, Pack(start.X, start.Y - 24));
            Assert.AreEqual(height, window.BottomPanelHeightForTest, reason);
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest, reason);
            if (reason == "捕获转移")
            {
                Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetCapture(), "旧拖动不能释放新控件的捕获。");
                _ = NativeMethods.ReleaseCapture();
            }
            _ = NativeMethods.EnableWindow(window.Handle, true);
            _ = NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowNormal);
            if (reason == "折叠底部") window.ShowHistoryForTest();
        }
        Assert.IsTrue(window.DragBottomSplitterForTest(24));
    });

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 底部工具窗口高字体超过参考上限时仍保持有效高度区间(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        int minimum = NativeTheme.Scale(400);
        Assert.AreEqual(minimum, MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(640), minimum));
        Assert.AreEqual(minimum, MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(2000), minimum));
    }
}
