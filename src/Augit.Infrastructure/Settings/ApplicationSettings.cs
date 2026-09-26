using System.Text.Json.Serialization;

namespace Augit.Infrastructure.Settings;

public sealed record ApplicationSettings
{
    public int Version { get; init; } = 1;

    public string? LastWorkspace { get; init; }

    public string Theme { get; init; } = "System";

    public string TextFontFamily { get; init; } = "Microsoft YaHei UI";

    public string MonospaceFontFamily { get; init; } = "Cascadia Mono";

    public double FontSize { get; init; } = 13;

    public double? TextFontSize { get; init; }

    // 旧配置只有共用字号，首次分开调整前沿用该值，避免升级后文字突然变小。
    [JsonIgnore]
    public double UiFontSize => TextFontSize ?? FontSize;

    public string? GitExecutablePath { get; init; }

    public string TerminalShell { get; init; } = TerminalShellIds.WindowsPowerShell;

    public string? TerminalCustomCommand { get; init; }

    /// <summary>
    /// 分支面板是否显示标签（权威 `git.branches.show.tags`，
    /// `GitBranchesTreeShowTagsAction` 的 `isSelected` 直接读 `GitVcsSettings.showTags()`，**默认 true**）。
    ///
    /// 登记差异：权威持久化在**项目级** `GitVcsSettings`（workspace 文件）；Augit 的设置文件是应用级，
    /// 因此这一项是应用级偏好（跨工作区共享），见 `09-icons.md` 第 181 轮。
    /// </summary>
    public bool ShowGitBranchesTags { get; init; } = true;

    /// <summary>
    /// 分支面板是否按目录（引用名的 `/` 前缀）分组（权威 `git.branches.group.by.directory`，
    /// `com.intellij.vcs.git.branch.GitGroupBranchByDirectoryAction`，
    /// `plugins/git4idea/shared/resources/intellij.vcs.git.shared.xml:51-53`）。
    ///
    /// **默认 true**：权威把分组键存在 `GitVcsSettings.branchSettings` 的 `groupingKeyIds` 里，
    /// 而它的默认值就是 `GROUPING_BY_DIRECTORY`
    /// （`platform/dvcs-impl/shared/src/com/intellij/dvcs/branch/DvcsBranchSettings.kt:22-23,26-28`
    /// 的 `stringSet(defaultGroupingKey.id)`）。
    ///
    /// 登记差异：权威持久化在**项目级** `GitVcsSettings`（workspace 文件）；Augit 的设置文件是应用级，
    /// 因此这一项是应用级偏好（跨工作区共享），与 `ShowGitBranchesTags` 同一口径。
    /// </summary>
    public bool GroupBranchesByDirectory { get; init; } = true;

    /// <summary>
    /// 大文件只读预览的警告横幅是否永久关闭（权威 `LargeFileNotificationProvider` 的
    /// `label.dont.show` 动作把 `DISABLE_KEY` = "large.file.editor.notification.disabled" 写进
    /// `PropertiesComponent`，属**应用级**偏好；`LargeFileNotificationProvider.java:38-58`）。
    /// 默认 false（显示）。
    /// </summary>
    public bool HideLargeFileWarning { get; init; }

    public WindowPlacementSettings Window { get; init; } = new();

    public ToolWindowLayoutSettings ToolWindows { get; init; } = new();

    public string[] OpenFiles { get; init; } = [];

    public string? ActiveFile { get; init; }

    public string[] ExpandedDirectories { get; init; } = [];

    public string[] RecentWorkspaces { get; init; } = [];
}

public static class TerminalShellIds
{
    public const string WindowsPowerShell = "WindowsPowerShell";

    public const string PowerShell7 = "PowerShell7";

    public const string CommandPrompt = "CommandPrompt";

    public const string GitBash = "GitBash";

    public const string Wsl = "Wsl";

    public const string Custom = "Custom";

    public static string Normalize(string? shell)
    {
        return shell is PowerShell7 or CommandPrompt or GitBash or Wsl or Custom
            ? shell
            : WindowsPowerShell;
    }
}

public sealed record WindowPlacementSettings
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public double Width { get; init; } = 1180;

    public double Height { get; init; } = 760;

    public bool IsMaximized { get; init; }
}

public sealed record ToolWindowLayoutSettings
{
    public double? ProjectPanelWidth { get; init; }

    public double? BottomPanelHeight { get; init; }
}
