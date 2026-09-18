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
            // 设置只读一次：主题与窗口摆放共用同一份快照，避免启动时读两遍文件。
            ApplicationSettings settings = LoadSettings();
            // 未显式指定 --theme 时按设置解析主题；设置里的 System 跟随 Windows。
            options = options with { Theme = ResolveTheme(options.Theme, settings) };
            // 未显式指定 --workspace 时恢复设置里上次打开的目录（产品规格 §3）。
            options = ShellStartup.ResolveWorkspace(options, settings);

            // 同一工作区已经打开时激活原窗口并退出（产品规格 §2：
            // 「再次打开同一目录时，在 1 秒内激活已有窗口并退出新进程」）。
            // 审计与多场景对照需要并发实例，因此显式场景参数会跳过该行为。
            if (options.Scene is null or { Length: 0 } && WorkspaceWindowRegistry.TryActivate(options.WorkspaceRoot))
            {
                return 0;
            }
            using ShellWindow window = new(options, settings.Window);
            window.Show();
            // 登记窗口句柄，供后续同工作区实例激活本窗口。
            _ = WorkspaceWindowRegistry.Register(window.Handle, options.WorkspaceRoot);
            return ShellWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            _ = MessageBox(0, exception.ToString(), "Augit", 0x00000010);
            return 1;
        }
    }

    /// <summary>
    /// 读取用户设置。读取失败（文件损坏、无权限）时返回默认设置：
    /// 启动不能因为设置文件而失败，此时窗口用默认尺寸与位置。
    /// </summary>
    private static ApplicationSettings LoadSettings()
    {
        try
        {
            SettingsStore store = new();
            return store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new();
        }
    }

    /// <summary>
    /// 解析生效主题：命令行优先，其次设置；设置为 System 时跟随 Windows 应用主题。
    /// </summary>
    private static string ResolveTheme(string? explicitTheme, ApplicationSettings settings)
    {
        if (explicitTheme is { Length: > 0 })
        {
            return explicitTheme;
        }

        string configured = settings.Theme;

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
