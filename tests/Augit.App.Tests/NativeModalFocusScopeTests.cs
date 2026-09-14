namespace Augit.App.Tests;

using Augit.Infrastructure.Settings;

[TestClass]
public sealed class NativeModalFocusScopeTests
{
    [TestMethod]
    public void Tooltip同时为图标按钮注册辅助技术名称且不改变可见文字()
    {
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();
        nint iconButton = NativeMethods.CreateWindow(
            0,
            NativeMethods.ButtonClass,
            UiText.CloseSymbol,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.ButtonOwnerDraw,
            0,
            0,
            32,
            32,
            window.Handle,
            0,
            NativeMethods.GetModuleHandle(null),
            0);

        try
        {
            Assert.AreNotEqual((nint)0, iconButton);
            using NativeToolTip toolTip = new(window.Handle);

            Assert.AreEqual(UiText.CloseSymbol, NativeAccessibility.GetNameForTest(iconButton));

            toolTip.Add(iconButton, UiText.Close);

            Assert.IsTrue(toolTip.ContainsForTest(iconButton));
            Assert.AreEqual(UiText.CloseSymbol, NativeMethods.GetWindowTextValue(iconButton));
            Assert.AreEqual(UiText.Close, NativeAccessibility.GetNameForTest(iconButton));
        }
        finally
        {
            if (iconButton != 0)
            {
                _ = NativeMethods.DestroyWindow(iconButton);
            }
        }
    }

    [TestMethod]
    public void 模态焦点工具类型可创建并释放()
    {
        using NativeModalFocusScope scope = new(0);

        scope.Restore();
        scope.Dispose();
    }

    [TestMethod]
    public void 模态窗口关闭后恢复真实主窗口工作区焦点()
    {
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();
        nint originalWorkArea = NativeMethods.CreateWindow(
            0,
            NativeMethods.ButtonClass,
            "原工作区焦点",
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop,
            0,
            0,
            80,
            24,
            window.Handle,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        nint modalWorkArea = NativeMethods.CreateWindow(
            0,
            NativeMethods.ButtonClass,
            "模态窗口焦点",
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop,
            0,
            30,
            80,
            24,
            window.Handle,
            0,
            NativeMethods.GetModuleHandle(null),
            0);

        try
        {
            Assert.AreNotEqual((nint)0, originalWorkArea);
            Assert.AreNotEqual((nint)0, modalWorkArea);
            _ = NativeMethods.SetFocus(originalWorkArea);
            Assert.AreEqual(originalWorkArea, NativeMethods.GetFocus());

            using NativeModalFocusScope scope = new(window.Handle);

            // 模拟模态窗口打开后焦点暂时离开工作区，再验证关闭时能回到原控件。
            _ = NativeMethods.SetFocus(modalWorkArea);
            Assert.AreEqual(modalWorkArea, NativeMethods.GetFocus());
            scope.Restore();

            Assert.AreEqual(originalWorkArea, NativeMethods.GetFocus());
        }
        finally
        {
            if (modalWorkArea != 0)
            {
                _ = NativeMethods.DestroyWindow(modalWorkArea);
            }

            if (originalWorkArea != 0)
            {
                _ = NativeMethods.DestroyWindow(originalWorkArea);
            }
        }
    }
}
