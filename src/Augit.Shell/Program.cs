using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.Shell;

/// <summary>
/// 应用入口：解析启动参数并运行原生窗口消息循环。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            ShellOptions options = ShellOptions.Parse(arguments);
            // 未显式指定 --theme 时按设置解析主题；设置里的 System 跟随 Windows。
            options = options with { Theme = ResolveTheme(options.Theme) };
            using ShellWindow window = new(options);
            window.Show();
            return ShellWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            _ = MessageBox(0, exception.ToString(), "Augit", 0x00000010);
            return 1;
        }
    }

    /// <summary>
    /// 解析生效主题：命令行优先，其次读取设置文件；设置为 System 时跟随 Windows 应用主题。
    /// 读取失败时回退到深色，与视觉稿基线一致。
    /// </summary>
    private static string ResolveTheme(string? explicitTheme)
    {
        if (explicitTheme is { Length: > 0 })
        {
            return explicitTheme;
        }

        string configured;
        try
        {
            SettingsStore store = new();
            configured = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult().Theme;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return "Dark";
        }

        if (configured.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        if (configured.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        return IsWindowsDark() ? "Dark" : "Light";
    }

    /// <summary>读取 Windows 的「应用模式」设置（个性化 → 颜色）。</summary>
    private static bool IsWindowsDark()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
