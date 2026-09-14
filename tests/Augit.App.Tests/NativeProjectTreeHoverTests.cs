using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeProjectTreeHoverTests
{
    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public Task 悬停只绘制当前行且不覆盖焦点选择或打开正文(int dpi, bool dark) => RunAsync(dpi, dark, (window, workspace) =>
    {
        nint tree = window.FileTreeHandleForTest, editor = NativeMethods.GetFocus();
        string selectedPath = Path.Combine(workspace, "文件000.txt");
        string hoverPath = Path.Combine(workspace, "文件001.txt");
        var (_, hoverRow) = SelectRow(window, hoverPath);
        var (_, selectedRow) = SelectRow(window, selectedPath);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        int layouts = window.LayoutInvocationCountForTest;
        int documents = window.LoadedDocumentCountForTest;
        _ = NativeMethods.UpdateWindow(tree);

        MoveToRow(tree, hoverRow);
        Assert.AreEqual(hoverPath, window.HoveredTreePathForTest);
        Assert.AreEqual(editor, NativeMethods.GetFocus());
        Assert.AreEqual(selectedPath, window.SelectedTreePathForTest);
        Assert.IsTrue(GetUpdateRectangle(tree, out NativeMethods.Rectangle update, false));
        Assert.AreEqual(hoverRow.Top, update.Top, "悬停不能使整个树失效。");
        Assert.AreEqual(hoverRow.Bottom, update.Bottom);
        Assert.AreEqual(palette.Hover, CaptureBackground(tree, hoverRow));
        Assert.AreEqual(palette.SelectionInactive, CaptureBackground(tree, selectedRow));

        _ = NativeMethods.UpdateWindow(tree);
        for (int i = 0; i < 200; i++) MoveToRow(tree, hoverRow);
        Assert.IsFalse(GetUpdateRectangle(tree, out _, false), "同一行内鼠标移动不重复重绘。");
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        Assert.AreEqual(documents, window.LoadedDocumentCountForTest);

        _ = NativeMethods.SetFocus(tree);
        MoveToRow(tree, selectedRow);
        Assert.AreEqual(palette.AccentSoft, CaptureBackground(tree, selectedRow), "悬停不能覆盖焦点选中态。");
        Assert.AreEqual(palette.Panel, CaptureBackground(tree, hoverRow), "旧悬停行恢复普通底色。");
        _ = NativeMethods.SetFocus(editor);
        Assert.AreEqual(palette.SelectionInactive, CaptureBackground(tree, selectedRow));
        _ = NativeMethods.SendMessage(tree, NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.IsNull(window.HoveredTreePathForTest);
        Assert.IsFalse(window.TreeHoverTrackingForTest);
        Assert.AreEqual(palette.SelectionInactive, CaptureBackground(tree, selectedRow));
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 滚动和删除后悬停重新命中且隐藏清理跟踪(bool dark) => RunAsync(96, dark, (window, workspace) =>
    {
        nint tree = window.FileTreeHandleForTest;
        string oldPath = Path.Combine(workspace, "文件001.txt");
        var (_, row) = SelectRow(window, oldPath);
        MoveToRow(tree, row);
        Assert.AreEqual(oldPath, window.HoveredTreePathForTest);
        _ = NativeMethods.SendMessage(tree, NativeMethods.WindowMessageVerticalScroll, 3, 0);
        Assert.IsNotNull(window.HoveredTreePathForTest);
        Assert.AreNotEqual(oldPath, window.HoveredTreePathForTest, "鼠标未动时，悬停也必须跟随实际滚动后的行。");
        Assert.AreEqual(NativeTheme.Palette(dark).Hover, CaptureBackground(tree, row));

        string removedPath = window.HoveredTreePathForTest!;
        var (removed, _) = SelectRow(window, removedPath);
        _ = NativeMethods.SendMessage(tree, NativeMethods.TreeViewDeleteItem, 0, removed);
        Assert.AreNotEqual(removedPath, window.HoveredTreePathForTest);
        _ = NativeMethods.ShowWindow(tree, NativeMethods.ShowHide);
        Assert.IsNull(window.HoveredTreePathForTest);
        Assert.IsFalse(window.TreeHoverTrackingForTest);
        MoveToRow(tree, row);
        Assert.IsNull(window.HoveredTreePathForTest, "隐藏控件不能接纳晚到的鼠标移动。");
        _ = NativeMethods.ShowWindow(tree, NativeMethods.ShowNormal);
        Assert.IsNull(window.HoveredTreePathForTest, "重新显示不恢复过期节点。");
        MoveToRow(tree, row);
        Assert.IsNotNull(window.HoveredTreePathForTest);
        _ = NativeMethods.SendMessage(tree, NativeMethods.WindowMessageMouseMove, 0, unchecked((nint)(-1)));
        Assert.IsNull(window.HoveredTreePathForTest, "客户区外的坐标不产生行悬停。");
    });

    [TestMethod]
    public Task 树销毁释放悬停节点和鼠标跟踪() => RunAsync(96, false, (window, workspace) =>
    {
        nint tree = window.FileTreeHandleForTest;
        var (_, row) = SelectRow(window, Path.Combine(workspace, "文件001.txt"));
        MoveToRow(tree, row);
        Assert.IsNotNull(window.HoveredTreePathForTest);
        Assert.IsTrue(NativeMethods.DestroyWindow(tree));
        Assert.IsFalse(window.TreeHoverTrackingForTest);
        Assert.IsNull(window.HoveredTreePathForTest);
    });

    private static async Task RunAsync(int dpi, bool dark, Action<MainWindow, string> verify)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        for (int i = 0; i < 80; i++)
            await File.WriteAllTextAsync(Path.Combine(workspace, $"文件{i:000}.txt"), "只读正文");
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            try
            {
                using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")),
                    new() { Theme = dark ? "Dark" : "Light", Window = new() { Width = 1024, Height = 640 } });
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    try
                    {
                        Assert.IsTrue(await window.OpenWorkspaceAsync(workspace));
                        await window.ExpandTreePathForTestAsync(workspace);
                        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "文件000.txt"));
                        window.KeepTreeRootVisibleForTest();
                        verify(window, workspace);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                if (failure is null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "悬停测试窗口未退出。");
        }
    }

    private static (nint Item, NativeMethods.Rectangle Row) SelectRow(MainWindow window, string path)
    {
        Assert.IsTrue(window.SelectTreePathForTest(path));
        nint tree = window.FileTreeHandleForTest;
        nint item = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0);
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rectangle>());
        try
        {
            Marshal.WriteIntPtr(buffer, item);
            Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetItemRectangle, 0, buffer));
            return (item, Marshal.PtrToStructure<NativeMethods.Rectangle>(buffer));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void MoveToRow(nint tree, NativeMethods.Rectangle row)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(tree, out NativeMethods.Rectangle client));
        int x = client.Right - NativeTheme.Scale(18), y = (row.Top + row.Bottom) / 2;
        _ = NativeMethods.SendMessage(tree, NativeMethods.WindowMessageMouseMove, 0, (nint)((y << 16) | (x & 0xffff)));
    }

    private static uint CaptureBackground(nint tree, NativeMethods.Rectangle row)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(tree, out NativeMethods.Rectangle client));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = client.Right,
                Height = -client.Bottom,
                Planes = 1,
                BitCount = 32
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        Assert.AreNotEqual((nint)0, bitmap);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            _ = NativeMethods.SendMessage(tree, 0x0318, (nuint)dc, 0x000C);
            return NativeMethods.GetPixel(dc, client.Right - NativeTheme.Scale(18), (row.Top + row.Bottom) / 2);
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
