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
}
