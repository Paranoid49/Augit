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

            // 同一工作区已经打开时激活原窗口并退出（产品规格 §2：
            // 「再次打开同一目录时，在 1 秒内激活已有窗口并退出新进程」）。
            // 审计与多场景对照需要并发实例，因此显式场景参数会跳过该行为。
            if (options.Scene is null or { Length: 0 } && TryActivateExistingWindow(options.WorkspaceRoot))
            {
                return 0;
            }
            using ShellWindow window = new(options);
            window.Show();
            // 登记窗口句柄，供后续同工作区实例激活本窗口。
            _ = RegisterWindow(window.Handle, options.WorkspaceRoot);
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

    /// <summary>
    /// 同一工作区的单实例登记表。
    /// 用内存映射登记首个实例的窗口句柄：窗口标题同步在 WebView2 下不可靠
    /// （已实测），因此按句柄定位而不是按标题匹配。
    /// </summary>
    private static string RegistryName(string workspaceRoot)
    {
        // 用工作区全路径的稳定散列作为名字，避免路径中的非法字符。
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(workspaceRoot).ToLowerInvariant()));
        return "Augit.Shell." + Convert.ToHexString(hash, 0, 16);
    }

    /// <summary>把本窗口句柄登记到该工作区的共享内存，供后续实例激活。</summary>
    private static System.IO.MemoryMappedFiles.MemoryMappedFile? RegisterWindow(nint window, string workspaceRoot)
    {
        try
        {
            System.IO.MemoryMappedFiles.MemoryMappedFile file =
                System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(RegistryName(workspaceRoot), sizeof(long));
            using System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = file.CreateViewAccessor();
            accessor.Write(0, window);
            return file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 该工作区已有实例时激活其窗口并返回 true。
    /// 只在登记的句柄仍是本应用的有效窗口时激活：进程崩溃留下的陈旧句柄会被忽略，
    /// 此时新实例正常启动并接管登记。
    /// </summary>
    private static bool TryActivateExistingWindow(string workspaceRoot)
    {
        try
        {
            using System.IO.MemoryMappedFiles.MemoryMappedFile? file =
                System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(RegistryName(workspaceRoot));
            using System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = file.CreateViewAccessor();
            long window = accessor.ReadInt64(0);
            if (window == 0)
            {
                return false;
            }

            nint handle = (nint)window;
            if (!IsWindow(handle) || !IsWindowVisible(handle))
            {
                return false;
            }

            _ = ShowWindow(handle, SwRestore);
            _ = SetForegroundWindow(handle);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
