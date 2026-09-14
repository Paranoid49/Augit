using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeModalScrimTests
{
    [TestMethod]
    public void 模态遮罩使用稳定透明度并覆盖完整客户区()
    {
        Assert.AreEqual((byte)82, NativeModalScrim.LightAlphaForTest);
        Assert.AreEqual((byte)112, NativeModalScrim.DarkAlphaForTest);
        Assert.AreEqual(0x00F6F1EEU, NativeModalScrim.ScrimColorForTest(false));
        Assert.AreEqual(NativeTheme.Palette(true).Chrome, NativeModalScrim.ScrimColorForTest(true));
        NativeMethods.Rectangle owner = new() { Left = 0, Top = 0, Right = 1180, Bottom = 760 };

        Assert.IsTrue(NativeModalScrim.CoversOwnerForTest(owner, owner));
        Assert.IsFalse(NativeModalScrim.CoversOwnerForTest(
            owner,
            new() { Left = 0, Top = 0, Right = 1179, Bottom = 760 }));
    }

    [TestMethod]
    [DataRow(false, 96)]
    [DataRow(true, 96)]
    [DataRow(false, 120)]
    [DataRow(true, 120)]
    [DataRow(false, 144)]
    [DataRow(true, 144)]
    public void 模态遮罩跟随宿主尺寸并在关闭后释放子类回调(bool dark, int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();
        int registrations = NativeModalScrim.RegistrationCountForTest;
        nint handle;
        using (NativeModalScrim scrim = NativeModalScrim.Begin(window.Handle, dark))
        {
            handle = scrim.HandleForTest;
            Assert.IsTrue(ReadLayeredAttributes(handle, out _, out byte alpha, out uint flags));
            Assert.AreEqual(dark ? (byte)112 : (byte)82, alpha);
            Assert.AreEqual(NativeMethods.LayeredWindowAttributesAlpha, flags);
            Assert.IsTrue(NativeMethods.GetClientRectangle(window.Handle, out var owner));
            Assert.IsTrue(NativeMethods.GetClientRectangle(scrim.HandleForTest, out var overlay));
            Assert.IsTrue(NativeModalScrim.CoversOwnerForTest(owner, overlay));
            int resized = scrim.ResizeCountForTest;
            nint focus = NativeMethods.GetFocus();
            _ = NativeMethods.SetWindowPosition(window.Handle, 0, 0, 0, NativeTheme.Scale(1100), NativeTheme.Scale(680),
                NativeMethods.SetWindowPositionNoActivate | NativeMethods.SetWindowPositionNoZOrder);
            _ = NativeMethods.UpdateWindow(window.Handle);
            Assert.IsTrue(NativeMethods.GetClientRectangle(window.Handle, out owner));
            Assert.IsTrue(NativeMethods.GetClientRectangle(scrim.HandleForTest, out overlay));
            Assert.IsTrue(NativeModalScrim.CoversOwnerForTest(owner, overlay));
            Assert.IsGreaterThan(resized, scrim.ResizeCountForTest);
            Assert.AreEqual(focus, NativeMethods.GetFocus(), "调整遮罩不能抢占焦点。");
            resized = scrim.ResizeCountForTest;
            for (int repeat = 0; repeat < 5; repeat++)
                _ = NativeMethods.SendMessage(window.Handle, NativeMethods.WindowMessageSize, 0, 0);
            Assert.AreEqual(resized, scrim.ResizeCountForTest, "相同客户区不应继续调整遮罩尺寸。");
        }
        Assert.IsFalse(NativeMethods.IsWindow(handle));
        Assert.AreEqual(registrations, NativeModalScrim.RegistrationCountForTest);
    }

    [TestMethod]
    public void 宿主先销毁时遮罩回调立即释放且重复释放安全()
    {
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();
        int registrations = NativeModalScrim.RegistrationCountForTest;
        using NativeModalScrim scrim = NativeModalScrim.Begin(window.Handle, false);
        nint handle = scrim.HandleForTest;
        window.Close();
        Assert.IsFalse(NativeMethods.IsWindow(handle));
        Assert.AreEqual(registrations, NativeModalScrim.RegistrationCountForTest);
        scrim.Dispose();
        Assert.AreEqual(registrations, NativeModalScrim.RegistrationCountForTest);
    }

    [DllImport("user32.dll", EntryPoint = "GetLayeredWindowAttributes")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadLayeredAttributes(nint window, out uint key, out byte alpha, out uint flags);
}
