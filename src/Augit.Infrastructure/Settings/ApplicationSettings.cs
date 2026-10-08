using System.Text.Json.Serialization;

namespace Augit.Infrastructure.Settings;

public sealed record ApplicationSettings
{
    /// <summary>
    /// 全新配置的界面字号初值：12px。
    ///
    /// 12px 取自本次对照用的 PyCharm 2026.2 Islands Light 前台配置
    /// （Microsoft YaHei UI、175% DPI；见 `docs/ux-spec.md` §4.3 与 `docs/product-spec.md` 第 165 行），
    /// 是**本次对照基线**，不是对所有用户强制生效的值：
    /// 已经有 `settings.json` 的用户由 <see cref="SettingsStore"/> 沿用自己保存的字号
    /// （旧配置只有一个共用字号时沿用该值，见 `docs/design-system.md` §5.3 末），
    /// 只有全新配置才落到这个初值。
    ///
    /// 权威对照：平台只在用户勾选「覆盖默认字体」时使用 `UISettings.fontSize2D`，否则回退到系统字体度量
    /// （`platform/platform-impl/src/com/intellij/ide/ui/laf/LafManagerImpl.kt:857-872`；
    /// 见 `docs/nui-behavior/07-theme-dpi-dialogs.md` §3）；编辑器等宽字号是另一套独立设置
    /// （`EditorColorsScheme` / `editor-font.xml`），与界面字号互不覆盖，对应下面的 <see cref="FontSize"/>。
    /// </summary>
    public const double DefaultUiFontSize = 12;

    public int Version { get; init; } = 1;

    public string? LastWorkspace { get; init; }

    public string Theme { get; init; } = "System";

    public string TextFontFamily { get; init; } = "Microsoft YaHei UI";

    public string MonospaceFontFamily { get; init; } = "Cascadia Mono";

    /// <summary>等宽字号；独立于界面字号，默认 13px（`docs/design-system.md` §5.4）。</summary>
    public double FontSize { get; init; } = 13;

    /// <summary>
    /// 独立保存的界面字号。null 表示旧配置（文件里没有 `textFontSize` 键）：
    /// 两类文字先沿用共用的 <see cref="FontSize"/>，避免升级后现有用户的字号被改动
    /// （`docs/design-system.md` §5.3 末）。全新配置的初值是 <see cref="DefaultUiFontSize"/>。
    /// </summary>
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
    public double? ProjectPanelWidth { get; init; }

    public double? BottomPanelHeight { get; init; }
}
