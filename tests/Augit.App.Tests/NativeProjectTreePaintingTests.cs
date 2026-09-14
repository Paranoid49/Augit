using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeProjectTreePaintingTests
{
    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public async Task 屏幕外节点不能把图标叠在项目树顶缘(int dpi, bool dark)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        for (int i = 0; i < 80; i++)
            await File.WriteAllTextAsync(Path.Combine(workspace, $"文件{i:000}.md"), "正文");
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
                        window.ShowFilesForTest();
                        await window.ExpandTreePathForTestAsync(workspace);
                        window.KeepTreeRootVisibleForTest();
                        await Task.Delay(20);
                        AssertTopPadding(window);
                        Assert.IsTrue(window.SelectTreePathForTest(Path.Combine(workspace, "文件079.md")));
                        await Task.Delay(20);
                        AssertTopPadding(window);
                        window.KeepTreeRootVisibleForTest();
                        await Task.Delay(20);
                        AssertTopPadding(window);
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
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "树绘制测试窗口未退出。");
        }
    }

    private static void AssertTopPadding(MainWindow window)
    {
        nint tree = window.FileTreeHandleForTest;
        Assert.IsTrue(NativeMethods.GetClientRectangle(tree, out NativeMethods.Rectangle rectangle));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = rectangle.Right,
                Height = -rectangle.Bottom,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            _ = NativeMethods.SendMessage(tree, 0x0318, (nuint)dc, 4);
            // 审计宿主使用 ComCtl32 v6，实测会为不可见节点发出空 RECT 通知；测试宿主不保证同一激活上下文。
            // 复用真实节点和真实绘制入口补发该通知，确认它不能污染已经画好的可见区域。
            NativeMethods.TreeViewItem item = new()
            {
                Mask = NativeMethods.TreeViewItemParameter,
                Item = NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0),
            };
            Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(tree, NativeMethods.TreeViewGetItem, 0, ref item));
            NativeMethods.TreeViewCustomDraw notification = new()
            {
                CustomDraw = new()
                {
                    Header = new() { WindowFrom = tree, Code = NativeMethods.NotificationCustomDraw },
                    DrawStage = NativeMethods.CustomDrawItemPrePaint,
                    DeviceContext = dc,
                    ItemSpec = (nuint)item.Item,
                    ItemParameter = item.Parameter,
                },
                Level = 1,
            };
            nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.TreeViewCustomDraw>());
            try
            {
                Marshal.StructureToPtr(notification, buffer, false);
                _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageNotify, 0, buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            for (int y = 0; y < NativeTheme.Scale(5); y++)
            {
                uint expected = NativeMethods.GetPixel(dc, rectangle.Right - NativeTheme.Scale(40), y);
                for (int x = NativeTheme.Scale(8); x < NativeTheme.Scale(42); x++)
                    Assert.AreEqual(expected, NativeMethods.GetPixel(dc, x, y), $"树顶缘 ({x},{y}) 出现屏幕外节点绘制。");
            }
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
