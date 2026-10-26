using Augit.Infrastructure.Files;
using Augit.Infrastructure.Settings;

namespace Augit.Shell;

/// <summary>
/// 启动期的工作区解析（产品规格 §3：启动时恢复上次打开的目录）。
///
/// 命令行显式给出的 <c>--workspace</c> 永远优先；没有给时才用设置里记录的最近目录，
/// 并且**必须仍然存在且是本地目录**——否则退回进程当前目录，
/// 不能因为一个失效的路径让外壳打不开（审计与多场景启动都依赖这条兜底）。
/// </summary>
internal static class ShellStartup
{
    public static ShellOptions ResolveWorkspace(ShellOptions options, ApplicationSettings settings)
    {
        if (options.WorkspaceExplicit)
        {
            return options;
        }

        string? last = settings.LastWorkspace;
        if (string.IsNullOrWhiteSpace(last))
        {
            return options;
        }

        if (!WorkspaceDirectoryService.ValidateRoot(last).IsValid)
        {
            return options;
        }

        return options with { WorkspaceRoot = Path.GetFullPath(last) };
    }

    /// <summary>
    /// 首次启动（没有可用的最近目录）且进程当前目录就是程序目录时，不使用程序目录作为工作区。
    ///
    /// 双击便携包/开始菜单快捷方式时，Windows 会把当前目录设为程序目录。直接把它当工作区会把
    /// <c>Augit.exe</c>、<c>coreclr.dll</c> 和发布目录里的二进制全部当作用户项目，并自动进入
    /// “创建 Git 仓库”状态；这既不是产品规格的启动页，也让首次使用落在错误的目录上。
    /// 这里改用 <c>%LOCALAPPDATA%\Augit\Welcome</c> 空目录，用户随后通过「打开目录」选择项目。
    /// 显式 <c>--workspace</c>（包括 <c>augit .</c>）始终优先，不受影响。
    /// </summary>
    public static ShellOptions ResolveLaunchWorkspace(ShellOptions options)
    {
        string welcome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "Welcome");
        return ResolveLaunchWorkspace(options, AppContext.BaseDirectory, welcome);
    }

    internal static ShellOptions ResolveLaunchWorkspace(
        ShellOptions options,
        string appDirectory,
        string welcomeDirectory)
    {
        if (options.WorkspaceExplicit)
        {
            return options;
        }

        string app = Normalize(appDirectory);
        string welcome = Normalize(welcomeDirectory);
        string root = Normalize(options.WorkspaceRoot);
        if (!string.Equals(root, app, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(root, welcome, StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        try
        {
            Directory.CreateDirectory(welcomeDirectory);
            AugitLog.Info("startup-workspace=welcome");
            return options with { WorkspaceRoot = Path.GetFullPath(welcomeDirectory), Welcome = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AugitLog.Error("startup-welcome-failed", exception);
            return options;
        }
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
