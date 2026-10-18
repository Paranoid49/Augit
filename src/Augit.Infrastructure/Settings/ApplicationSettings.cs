using System.Text.Json.Serialization;

namespace Augit.Infrastructure.Settings;

public sealed record ApplicationSettings
{
    /// <summary>2026-10-15 用户裁决：日常入口迁移到真实外壳测试采用的外观基准。</summary>
    public int AppearanceVersion { get; init; } = 1;

    /// <summary>测试与日常启动共用的界面字号基准，旧配置由 SettingsStore 一次迁移。</summary>
    public const double DefaultUiFontSize = 12;

    public int Version { get; init; } = 1;

    public string? LastWorkspace { get; init; }

    public string Theme { get; init; } = "System";

    public string TextFontFamily { get; init; } = "Microsoft YaHei UI";

    /// <summary>正文固定使用随包分发的开源 JetBrains Mono。</summary>
    public string MonospaceFontFamily { get; init; } = "JetBrains Mono";

    /// <summary>等宽字号；独立于界面字号，默认 13px（`docs/design-system.md` §5.4）。</summary>
    public double FontSize { get; init; } = 13;

    /// <summary>迁移后独立保存的界面字号，用户后续调整不会被每次启动覆盖。</summary>
    public double? TextFontSize { get; init; } = DefaultUiFontSize;

    /// <summary>界面字号；旧配置没有独立值时沿用共用字号。</summary>
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
    /// <summary>面板尺寸所属工作区；与标签会话的 LastWorkspace 分开，避免仅拖动时转移旧标签。</summary>
    public string? WorkspaceRoot { get; init; }

    public double? ProjectPanelWidth { get; init; }

    public double? BottomPanelHeight { get; init; }
}
