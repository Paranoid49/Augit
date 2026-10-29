namespace Augit.Shell;

/// <summary>
/// 原生窗口标题（也是 Windows 任务栏悬停缩略图和 Alt-Tab 的标签）。
///
/// 项目窗口必须显示工作区目录名，而不是固定的 "Augit"：同一进程可能同时打开多个
/// 项目，任务栏悬停时用固定产品名完全分不清窗口。欢迎页/视觉稿场景仍用产品名；
/// 设置子窗口用 "Settings"。
/// </summary>
internal static class ShellWindowTitle
{
    public static string Resolve(ShellOptions options, bool settingsWindow)
        => Resolve(options.WorkspaceRoot, options.Welcome, options.Scene, settingsWindow);

    internal static string Resolve(string workspaceRoot, bool welcome, string? scene, bool settingsWindow)
    {
        if (settingsWindow)
        {
            return "Settings";
        }

        // 欢迎页和 --scene 视觉稿没有对应项目，保留产品名。
        if (welcome || !string.IsNullOrEmpty(scene))
        {
            return "Augit";
        }

        string root = (workspaceRoot ?? string.Empty).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (root.Length == 0)
        {
            return "Augit";
        }

        string name = Path.GetFileName(root);
        return string.IsNullOrWhiteSpace(name) ? "Augit" : name;
    }
}
