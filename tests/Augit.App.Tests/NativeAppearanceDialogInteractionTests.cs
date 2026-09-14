using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeAppearanceDialogInteractionTests
{
    [TestMethod]
    public async Task 设置窗口应用两套字号后刷新自身字体并保留另一个字号()
    {
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        int comboSurfacesBefore = NativeComboBoxTheme.RegisteredCountForTest;
        TaskCompletionSource<ApplicationSettings?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread uiThread = new(() =>
        {
            nint owner = NativeMethods.CreateWindow(
                0, NativeMethods.StaticClass, "设置测试宿主",
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                40, 40, 1180, 760, 0, 0, NativeMethods.GetModuleHandle(null), 0);
            try
            {
                closed.SetResult(NativeAppearanceDialog.Show(owner, new() { TextFontSize = 13, FontSize = 19 }));
            }
            catch (Exception exception)
            {
                closed.SetException(exception);
            }
            finally
            {
                _ = NativeMethods.DestroyWindow(owner);
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        nint dialog = 0;
        uiThread.Start();
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !closed.Task.IsCompleted)
            {
                dialog = FindWindow("Augit.AppearanceDialog.Native", null);
                _ = NativeMethods.GetWindowThreadProcessId(dialog, out uint processId);
                // 对话框在所有控件创建、布局完成后才显示；不能用中途出现的单个 HWND 判断就绪。
                if (processId == Environment.ProcessId && NativeMethods.IsWindowVisible(dialog)
                    && GetDialogItem(dialog, 517) != 0 && GetDialogItem(dialog, 513) != 0)
                {
                    break;
                }
                dialog = 0;
                await Task.Delay(20);
            }
            if (closed.Task.IsCompleted)
            {
                _ = await closed.Task;
            }
            Assert.AreNotEqual(0, dialog);
            Assert.IsGreaterThanOrEqualTo(
                comboSurfacesBefore + 2,
                NativeComboBoxTheme.RegisteredCountForTest,
                "设置窗口的主题和终端选择框必须注册统一主题表面。");
            nint textSize = GetDialogItem(dialog, 517);
            nint codeSize = GetDialogItem(dialog, 513);
            Assert.AreEqual("13", NativeMethods.GetWindowTextValue(textSize));
            Assert.AreEqual("19", NativeMethods.GetWindowTextValue(codeSize));
            _ = NativeMethods.SetWindowText(textSize, "15");
            _ = NativeMethods.SendMessage(dialog, NativeMethods.WindowMessageCommand, 502, 0);
            Assert.AreEqual(15d, NativeTheme.UiFontSizeForTest);
            Assert.AreEqual("19", NativeMethods.GetWindowTextValue(codeSize));
            Assert.AreEqual(NativeTheme.UiFont, NativeMethods.SendMessage(textSize, 0x0031, 0, 0));

            _ = NativeMethods.SetWindowText(codeSize, "22");
            _ = NativeMethods.SendMessage(dialog, NativeMethods.WindowMessageCommand, 501, 0);
            ApplicationSettings? result = await closed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsNotNull(result);
            Assert.AreEqual(15d, result.UiFontSize);
            Assert.AreEqual(22d, result.FontSize);
        }
        finally
        {
            if (dialog != 0 && NativeMethods.IsWindow(dialog))
            {
                _ = NativeMethods.PostMessage(dialog, NativeMethods.WindowMessageClose, 0, 0);
            }
            Assert.IsTrue(uiThread.Join(TimeSpan.FromSeconds(8)), "设置窗口线程未退出。");
            Assert.AreEqual(comboSurfacesBefore, NativeComboBoxTheme.RegisteredCountForTest);
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    public async Task 大字号设置窗口滚动后切换分类回到正文起点()
    {
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        TaskCompletionSource<ApplicationSettings?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread uiThread = new(() =>
        {
            nint owner = NativeMethods.CreateWindow(
                0, NativeMethods.StaticClass, "设置滚动测试宿主",
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                40, 40, 1024, 640, 0, 0, NativeMethods.GetModuleHandle(null), 0);
            try
            {
                ApplicationSettings settings = new() { TextFontSize = 40, FontSize = 13 };
                NativeTheme.ConfigureUiTypography(settings.TextFontFamily, settings.UiFontSize);
                closed.SetResult(NativeAppearanceDialog.Show(owner, settings));
            }
            catch (Exception exception)
            {
                closed.SetException(exception);
            }
            finally
            {
                _ = NativeMethods.DestroyWindow(owner);
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        nint dialog = 0;
        uiThread.Start();
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !closed.Task.IsCompleted)
            {
                dialog = FindWindow("Augit.AppearanceDialog.Native", null);
                _ = NativeMethods.GetWindowThreadProcessId(dialog, out uint processId);
                if (processId == Environment.ProcessId && NativeMethods.IsWindowVisible(dialog)
                    && GetDialogItem(dialog, 514) != 0)
                {
                    break;
                }
                dialog = 0;
                await Task.Delay(20);
            }

            Assert.AreNotEqual(0, dialog);
            for (int index = 0; index < 8; index++)
            {
                _ = NativeMethods.SendMessage(dialog, NativeMethods.WindowMessageVerticalScroll, 1, 0);
            }

            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(dialog, 1));
            _ = NativeMethods.SendMessage(dialog, NativeMethods.WindowMessageCommand, 203, 0);
            Assert.AreEqual(0, NativeMethods.GetScrollPosition(dialog, 1));
            Assert.IsTrue(NativeMethods.IsWindowVisible(GetDialogItem(dialog, 514)));
        }
        finally
        {
            if (dialog != 0 && NativeMethods.IsWindow(dialog))
            {
                _ = NativeMethods.PostMessage(dialog, NativeMethods.WindowMessageClose, 0, 0);
            }
            Assert.IsTrue(uiThread.Join(TimeSpan.FromSeconds(8)), "设置窗口线程未退出。");
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? title);

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint GetDialogItem(nint window, int identifier);
}
