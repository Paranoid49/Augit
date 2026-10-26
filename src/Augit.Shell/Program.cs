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
        AugitLog.Initialize(arguments);
        try
        {
            // 便携包缺少 WebView2 时先给出缺失项与官方下载入口；不自动安装共享运行时。
            if (!ShellStartupProbe.HasWebView2Runtime())
            {
                _ = MessageBox(0, ShellFailureText.MissingWebView2(), "Augit", 0x00000010);
                return 1;
            }

            ShellOptions options = ShellOptions.Parse(arguments);
            // 设置只读一次：主题与窗口摆放共用同一份快照，避免启动时读两遍文件。
            // 审计可显式指定设置文件，隔离运行不得读写用户设置。
            ApplicationSettings settings = LoadSettings(options.SettingsFile);
            // 未显式指定 --theme 时按设置解析主题；设置里的 System 跟随 Windows。
            // 另外单独保留"是否跟随系统"这一事实：Theme 此时已是解析结果，分辨不出它来自
            // 显式选择还是跟随系统，而系统主题变化时只有后者才应该被覆盖。
            string themeMode = options.Theme is { Length: > 0 } ? options.Theme : settings.Theme;
            options = options with { Theme = ResolveTheme(options.Theme, settings), ThemeMode = themeMode };
            // 未显式指定 --workspace 时恢复设置里上次打开的目录（产品规格 §3）。
            options = ShellStartup.ResolveWorkspace(options, settings);
            // 首次启动且当前目录是程序目录时，避免把发布目录当工作区（见 ShellStartup）。
            options = ShellStartup.ResolveLaunchWorkspace(options);
            AugitLog.Info($"options workspace={options.WorkspaceRoot} theme={options.Theme} isolated={options.HasIsolatedState}");

            // 同一工作区已经打开时激活原窗口并退出（产品规格 §2：
            // 「再次打开同一目录时，在 1 秒内激活已有窗口并退出新进程」）。
            // 显式场景跳过复用；隔离实例不激活或覆盖用户窗口登记。
            bool reuseWorkspaceWindow = options.ShouldReuseWorkspaceWindow;
            if (reuseWorkspaceWindow && WorkspaceWindowRegistry.TryActivate(options.WorkspaceRoot))
            {
                return 0;
            }

            using ShellWindow window = new(options, settings.Window);
            window.Show();
            // 登记窗口句柄，供后续同工作区实例激活本窗口。
            if (!options.HasIsolatedState)
            {
                _ = WorkspaceWindowRegistry.Register(window.Handle, options.WorkspaceRoot);
            }

            return ShellWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            AugitLog.Error("startup", exception);
            _ = MessageBox(0, ShellFailureText.Startup(exception), "Augit", 0x00000010);
            return 1;
        }
    }

    /// <summary>
    /// 读取用户设置。读取失败（文件损坏、无权限）时返回默认设置：
    /// 启动不能因为设置文件而失败，此时窗口用默认尺寸与位置。
    /// <paramref name="settingsFile"/> 为审计指定的设置文件，null 时用默认设置。
    /// </summary>
    internal static ApplicationSettings LoadSettings(string? settingsFile)
    {
        try
        {
            SettingsStore store = new(settingsFile);
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

        return ShellTheme.FromSystemDark(ShellSystemTheme.DetectDark());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
