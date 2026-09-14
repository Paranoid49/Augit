using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitHistoryInteractionTests
{
    private static readonly int[] HistoryFocusButtonIdentifiers = [25, 26, 27, 10, 28, 15, 29, 24, 30, 31, 32, 36, 22, 23];
    [TestMethod]
    [DataRow(13)]
    [DataRow(32)]
    public Task 历史工具栏键盘操作保留提交且局部切换详情(int key) => RunAsync(async (window, history, service) =>
    {
        history.SetBounds(0, 0, NativeTheme.Scale(2000), NativeTheme.Scale(360));
        string? commit = history.SelectedCommitHashForTest;
        int reads = service.PageReads, layouts = window.LayoutInvocationCountForTest;
        nint details = HistoryChild(history.Handle, 36);
        _ = NativeMethods.SetFocus(details);
        PostKey(details, key);
        await WaitUntilAsync(() => !history.DetailsShownForTest);
        Assert.AreEqual(details, NativeMethods.GetFocus());
        PostKey(details, key);
        await WaitUntilAsync(() => history.DetailsShownForTest);

        nint search = HistoryChild(history.Handle, 28);
        _ = NativeMethods.SetFocus(search);
        PostKey(search, key);
        await WaitUntilAsync(() => history.FilterHasFocusForTest);
        nint author = HistoryChild(history.Handle, 30);
        _ = NativeMethods.SetFocus(author);
        PostKey(author, key);
        await WaitUntilAsync(() => history.FilterKindIndexForTest == 2 && history.FilterHasFocusForTest);
        Assert.AreEqual(commit, history.SelectedCommitHashForTest);
        Assert.AreEqual(reads, service.PageReads);
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
    });

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Light")]
    public async Task 历史工具栏收放与溢出菜单保留可见焦点及上下文(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync(async (window, history, service) =>
        {
            void Resize(int width, int height) => history.SetBounds(0, 0, NativeTheme.Scale(width), NativeTheme.Scale(height));
            Resize(2000, 360);
            string? selected = history.SelectedCommitHashForTest, document = window.ActiveDocumentPathForTest;
            int reads = service.PageReads, builds = history.CommitGraphBuildCountForTest;
            nint locate = HistoryChild(history.Handle, 29), overflow = HistoryChild(history.Handle, 44);
            _ = NativeMethods.SetFocus(locate);
            Resize(2000, 180);
            Assert.AreEqual(overflow, NativeMethods.GetFocus());
            PostKey(overflow, NativeMethods.VirtualKeyEnter);
            await WaitUntilAsync(() => history.ToolbarPopupForTest is { Handle: not 0 });
            NativeToolbarPopup popup = history.ToolbarPopupForTest!;
            nint search = popup.ButtonsForTest[3];
            _ = NativeMethods.SetFocus(search);
            PostKey(search, NativeMethods.VirtualKeyEnter);
            await WaitUntilAsync(() => popup.Handle == 0 && history.FilterHasFocusForTest);
            _ = NativeMethods.SetFocus(overflow);
            Resize(2000, 360);
            Assert.AreEqual(locate, NativeMethods.GetFocus());

            nint path = HistoryChild(history.Handle, 32), filters = HistoryChild(history.Handle, 43);
            _ = NativeMethods.SetFocus(path);
            Resize(620, 360);
            Assert.AreEqual(filters, NativeMethods.GetFocus());
            Resize(2000, 360);
            Assert.AreEqual(path, NativeMethods.GetFocus());
            _ = NativeMethods.SetFocus(history.HistoryListHandleForTest);
            Resize(620, 180);
            Assert.AreEqual(history.HistoryListHandleForTest, NativeMethods.GetFocus());

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo settings = typeof(MainWindow).GetField("_settings", flags)!;
            ApplicationSettings original = (ApplicationSettings)settings.GetValue(window)!;
            MethodInfo apply = typeof(MainWindow).GetMethod("ApplyAppearance", flags)!;
            try
            {
                settings.SetValue(window, original with { TextFontSize = 40 });
                apply.Invoke(window, null);
                Resize(2000, 180);
                Assert.IsTrue(NativeMethods.IsWindowVisible(HistoryChild(history.Handle, 25)));
                Assert.IsTrue(history.ToolbarOverflowVisibleForTest);
                var panel = HistoryControlBounds(history.Handle);
                foreach (nint button in history.VisibleSideToolbarButtonsForTest)
                {
                    var bounds = HistoryControlBounds(button);
                    Assert.IsLessThanOrEqualTo(panel.Bottom - NativeTheme.Scale(4), bounds.Bottom);
                }
            }
            finally
            {
                settings.SetValue(window, original);
                apply.Invoke(window, null);
            }
            Assert.AreEqual(selected, history.SelectedCommitHashForTest);
            Assert.AreEqual(document, window.ActiveDocumentPathForTest);
            Assert.AreEqual(reads, service.PageReads);
            Assert.AreEqual(builds, history.CommitGraphBuildCountForTest);
        }, theme);
    }

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Light")]
    public async Task 历史工具栏焦点框随真实焦点显示并在离开后清除(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunAsync((_, history, _) =>
        {
            history.SetBounds(0, 0, NativeTheme.Scale(2000), NativeTheme.Scale(360));
            uint accent = NativeTheme.Palette(theme == "Dark").Accent;
            foreach (int identifier in HistoryFocusButtonIdentifiers)
            {
                nint button = HistoryChild(history.Handle, identifier);
                if (!NativeMethods.IsWindowEnabled(button)) continue;
                _ = NativeMethods.SetFocus(button);
                Assert.AreEqual(button, NativeMethods.GetFocus());
                Assert.AreEqual(accent, CaptureToolbarFocusPixel(button), $"按钮 {identifier} 缺少焦点框。");
                _ = NativeMethods.SetFocus(history.HistoryListHandleForTest);
                Assert.AreNotEqual(accent, CaptureToolbarFocusPixel(button), $"按钮 {identifier} 遗留焦点框。");
            }
            return Task.CompletedTask;
        }, theme);
    }

    private static uint CaptureToolbarFocusPixel(nint button)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(button, out var bounds));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = bounds.Right,
                Height = -bounds.Bottom,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        Assert.AreNotEqual((nint)0, bitmap);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            _ = NativeMethods.SendMessage(button, 0x0318, (nuint)dc, 0x000C);
            return NativeMethods.GetPixel(dc, NativeTheme.Scale(8), NativeTheme.Scale(2));
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
