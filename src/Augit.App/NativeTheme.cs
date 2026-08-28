using Microsoft.Win32;

namespace Augit.App;

internal static class NativeTheme
{
    private const int UseImmersiveDarkMode = 20;

    internal static bool IsDark(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static void ApplyToWindow(nint window, bool dark)
    {
        if (window == 0)
        {
            return;
        }

        int enabled = dark ? 1 : 0;
        _ = NativeMethods.SetDwmWindowAttribute(window, UseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    internal static void ApplyToControl(nint control, bool dark)
    {
        if (control != 0)
        {
            _ = NativeMethods.SetWindowTheme(control, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }
}
