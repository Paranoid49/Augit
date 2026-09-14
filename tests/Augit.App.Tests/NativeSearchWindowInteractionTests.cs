using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeSearchWindowInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task 主窗口组词保留浮层且单击预览回车转正式关闭恢复焦点(bool textMode)
    {
        using TemporaryDirectory temporary = new();
        string first = temporary.GetPath("alpha.txt"), second = temporary.GetPath("beta.txt");
        await File.WriteAllTextAsync(first, "alpha\n原始文件");
        await File.WriteAllTextAsync(second, "第一行\nbeta\n第三行");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    NativeSearchPanel? panel = null;
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(temporary.FullPath));
                        await window.OpenDocumentForTestAsync(first);
                        window.ShowFilesForTest();
                        nint originalFocus = NativeMethods.GetFocus();
                        Assert.IsTrue(window.HandleApplicationShortcutForTest(textMode ? 'F' : 'P', control: true, shift: textMode));
                        panel = (NativeSearchPanel)typeof(MainWindow).GetField("_searchPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        nint edit = Child(panel.Handle, 1), list = Child(panel.Handle, 2);
                        _ = NativeMethods.SetWindowText(edit, "alpha");
                        await WaitUntilAsync(() => panel.SearchCompletedForTest);
                        Assert.AreEqual(first, window.ActiveDocumentPathForTest, "搜索完成不能自动打开首项。");
                        int starts = panel.SearchStartCountForTest;
                        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
                        _ = NativeMethods.SetWindowText(edit, "beta");
                        foreach (int key in new[] { NativeMethods.VirtualKeyEnter, NativeMethods.VirtualKeyEscape, NativeMethods.VirtualKeyTab })
                        {
                            _ = NativeMethods.PostMessage(edit, NativeMethods.WindowMessageKeyDown, (nuint)key, 0);
                            await Task.Delay(25);
                            Assert.IsTrue(window.SearchPanelVisibleForTest, "组词按键必须由输入法处理。");
                            Assert.AreEqual(edit, NativeMethods.GetFocus());
                        }
                        Assert.AreEqual(starts, panel.SearchStartCountForTest);
                        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
                        await WaitUntilAsync(() => panel.SearchCompletedForTest);
                        Assert.AreEqual(1, panel.ResultCount);
                        Assert.IsTrue(window.PreviewFirstSearchResultForTest());
                        await WaitUntilAsync(() => window.ActiveDocumentPathForTest == second && window.ActiveDocumentText?.Contains("beta", StringComparison.Ordinal) == true);
                        Assert.IsTrue(window.ActiveDocumentIsPreviewForTest);
                        Assert.IsTrue(window.SearchPanelVisibleForTest);
                        Assert.AreEqual(list, NativeMethods.GetFocus());
                        _ = NativeMethods.PostMessage(list, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                        await WaitUntilAsync(() => !window.SearchPanelVisibleForTest && !window.ActiveDocumentIsPreviewForTest);
                        Assert.AreEqual(second, window.ActiveDocumentPathForTest);
                        Assert.AreEqual(2, window.OpenDocumentCount);
                        Assert.AreEqual(originalFocus, NativeMethods.GetFocus());
                        if (textMode)
                        {
                            nint editor = Child(window.ActiveDocumentViewForTest!.Handle, 100);
                            nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
                            Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor, 2166, (nuint)caret, 0), "正式打开必须定位命中行。");
                        }
                        await panel.SearchWorkerForTest;
                        Assert.IsTrue(window.HandleApplicationShortcutForTest('P', control: true));
                        panel = (NativeSearchPanel)typeof(MainWindow).GetField("_searchPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        nint reopened = NativeMethods.GetFocus();
                        _ = NativeMethods.PostMessage(reopened, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
                        await WaitUntilAsync(() => !window.SearchPanelVisibleForTest);
                        Assert.AreEqual(originalFocus, NativeMethods.GetFocus());
                        Assert.AreEqual(second, window.ActiveDocumentPathForTest);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        window.CloseSearchForTest();
                        if (panel is not null) await panel.SearchWorkerForTest;
                        window.Close();
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "搜索主窗口测试线程必须退出。");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, timer.ElapsedMilliseconds, "搜索主窗口状态等待超时。");
            await Task.Delay(10);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint Child(nint window, int identifier);
}
