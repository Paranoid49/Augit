using Augit.Infrastructure.Files;

namespace Augit.Shell;

/// <summary>
/// 外壳启动参数。默认加载可执行文件旁的 <c>web</c> 目录（开发布局下回退到仓库内的同名目录）；
/// <c>--mockups</c> 改为加载 HTML 视觉稿，使视觉稿可以在原生窗口内直接被渲染，用于逐场景像素对照。
/// </summary>
internal sealed record ShellOptions(string WebRoot, string WorkspaceRoot, string? Scene, string? Theme, int? Width, int? Height, bool ShowFrame, bool PixelExact, bool NoSessionRestore, string? OpenDocument, string? BlameDocument, string? FileHistoryDocument, string? ConflictDocument, string? DiffDocument, int? Dpi, string? BrowserArguments)
{
    /// <summary>
    /// 是否在命令行显式给出了 <c>--workspace</c>。
    /// 调用方要区分"用户没指定"与"指定了当前目录"：前者才恢复设置里上次打开的目录
    /// （产品规格：启动时恢复上次打开的目录）。
    /// </summary>
    public bool WorkspaceExplicit { get; init; }

    /// <summary>
    /// 主题模式：设置里的 <c>System</c>／<c>Light</c>／<c>Dark</c>，或命令行显式给出的值。
    /// </summary>
    /// <remarks>
    /// <see cref="Theme"/> 保存的是**解析后**的生效主题，分辨不出它来自"跟随系统"还是
    /// 用户显式选择；而系统主题变化时只有前者应该被覆盖。因此这里单独保留模式本身。
    /// 系统主题变化的跟随逻辑见 <see cref="ShellTheme.NextOnSystemChange"/>。
    /// </remarks>
    public string ThemeMode { get; init; } = "System";

    /// <summary>
    /// 审计用的显式设置文件；未指定时使用用户默认配置。
    /// </summary>
    public string? SettingsFile { get; init; }

    /// <summary>审计隔离用的显式 WebView2 用户数据目录（<c>--user-data-folder</c>）。</summary>
    public string? UserDataFolder { get; init; }

    /// <summary>
    /// 是否显式使用隔离状态；这些实例不得激活或覆盖用户窗口登记。
    /// </summary>
    public bool HasIsolatedState => SettingsFile is not null || UserDataFolder is not null;

    public bool ShouldReuseWorkspaceWindow => Scene is null or { Length: 0 } && !HasIsolatedState;

    public static ShellOptions Parse(string[] arguments)
    {
        string? webRoot = null;
        string? workspaceRoot = null;
        bool workspaceExplicit = false;
        string? scene = null;
        string? theme = null;
        string? width = null;
        string? height = null;
        bool mockups = false;
        bool showFrame = true;
        bool pixelExact = false;
        bool noSessionRestore = false;
        string? openDocument = null;
        string? blameDocument = null;
        string? fileHistoryDocument = null;
        string? conflictDocument = null;
        string? diffDocument = null;
        int? dpi = null;
        string? browserArguments = null;
        string? settingsFile = null;
        string? userDataFolder = null;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--web-root":
                    webRoot = Next(arguments, ref index, argument);
                    break;
                case "--workspace":
                    workspaceRoot = Next(arguments, ref index, argument);
                    workspaceExplicit = true;
                    break;
                case "--mockups":
                    mockups = true;
                    break;
                case "--scene":
                    scene = Next(arguments, ref index, argument);
                    break;
                case "--theme":
                    theme = Next(arguments, ref index, argument);
                    break;
                case "--width":
                    width = Next(arguments, ref index, argument);
                    break;
                case "--height":
                    height = Next(arguments, ref index, argument);
                    break;
                case "--no-frame":
                    showFrame = false;
                    break;
                case "--pixel-exact":
                    pixelExact = true;
                    break;
                // 审计用：跳过"恢复上次打开的标签"。逐场景截图必须与启动参数完全对应，
                // 否则上一次运行留下的活动文件会把场景渲染成另一个页面（实测踩过）。
                case "--no-session-restore":
                    noSessionRestore = true;
                    break;
                case "--open":
                    openDocument = Next(arguments, ref index, argument);
                    break;
                case "--blame":
                    blameDocument = Next(arguments, ref index, argument);
                    break;
                case "--file-history":
                    fileHistoryDocument = Next(arguments, ref index, argument);
                    break;
                case "--conflict":
                    conflictDocument = Next(arguments, ref index, argument);
                    break;
                case "--diff":
                    diffDocument = Next(arguments, ref index, argument);
                    break;
                case "--browser-args":
                    browserArguments = Next(arguments, ref index, argument);
                    break;
                // 审计隔离：显式设置文件与 WebView2 用户数据目录，只接受完全绝对路径。
                case "--settings-file":
                    settingsFile = ParseAbsolutePath(arguments, ref index, argument);
                    break;
                case "--user-data-folder":
                    userDataFolder = ParseAbsolutePath(arguments, ref index, argument);
                    break;
                case "--dpi":
                    string dpiText = Next(arguments, ref index, argument);
                    if (!int.TryParse(dpiText, out int dpiValue) || dpiValue is < 72 or > 480)
                    {
                        throw new ArgumentException("--dpi 必须是 72 到 480 之间的整数。");
                    }

                    dpi = dpiValue;
                    break;
                default:
                    throw new ArgumentException($"未知的启动参数：{argument}");
            }
        }

        string root = webRoot is null
            ? FindDefaultWebRoot(mockups)
            : Path.GetFullPath(webRoot);
        return new ShellOptions(
            root,
            Path.GetFullPath(workspaceRoot ?? Environment.CurrentDirectory),
            // 注意：工作区在 Program 里还会按设置解析一次（见 ShellStartup.ResolveWorkspace），
            // 因此这里保留"命令行没给"的原始事实。
            scene,
            theme,
            ParseSize(width, "--width"),
            ParseSize(height, "--height"),
            showFrame,
            pixelExact,
            noSessionRestore,
            openDocument,
            blameDocument,
            fileHistoryDocument,
            conflictDocument,
            diffDocument,
            dpi,
            browserArguments)
        {
            WorkspaceExplicit = workspaceExplicit,
            SettingsFile = settingsFile,
            UserDataFolder = userDataFolder,
        };
    }

    private static string Next(string[] arguments, ref int index, string name)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"{name} 缺少取值。");
        }

        return arguments[++index];
    }

    /// <summary>
    /// 隔离路径必须是完全绝对路径：相对、驱动器相对或根相对路径会随进程当前目录漂移，
    /// 读写的可能不是审计指定的那份状态。
    /// </summary>
    private static string ParseAbsolutePath(string[] arguments, ref int index, string name)
    {
        string value = Next(arguments, ref index, name);
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException($"{name} 必须是完全绝对路径。");
        }

        return Path.GetFullPath(value);
    }

    /// <summary>解析 WebView2 用户数据目录：显式目录优先，否则用固定的默认会话目录。</summary>
    internal static string ResolveWebViewDataFolder(string? explicitUserDataFolder)
    {
        if (explicitUserDataFolder is { Length: > 0 } folder)
        {
            return Path.GetFullPath(folder);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "WebView2",
            "Shell",
            "Session");
    }

    /// <summary>
    /// 是否按默认规则清理历史会话目录。显式目录的父目录可能同时存放用户数据，
    /// 无法推断其中临时文件的归属，因此一律不清理。
    /// </summary>
    internal static bool ShouldCleanDefaultWebViewSessions(string? explicitUserDataFolder)
        => string.IsNullOrEmpty(explicitUserDataFolder);

    /// <summary>
    /// 解析窗口尺寸覆盖。未传时返回 null——调用方需要区分"用户没指定"与"用户指定了默认值"：
    /// 前者要恢复设置里保存的窗口尺寸（§6.6「已恢复窗口尺寸不得被默认值覆盖」），
    /// 后者是审计要固定尺寸，必须原样生效。
    /// </summary>
    private static int? ParseSize(string? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, out int parsed) || parsed < 320)
        {
            throw new ArgumentException($"{name} 必须是大于等于 320 的整数。");
        }

        return parsed;
    }

    /// <summary>
    /// 解析界面资源目录：先在可执行文件旁查找发布布局，其次回退到仓库根目录的开发布局。
    /// 两种模式各自只有一个候选目录，避免 <c>--mockups</c> 意外加载运行界面。
    /// </summary>
    private static string FindDefaultWebRoot(bool mockups)
    {
        string? located = DistributionLayout.FindAncestorDirectory(
            AppContext.BaseDirectory,
            mockups ? ["docs/ux-mockups"] : ["web"],
            "Augit.slnx");
        return located ?? throw new InvalidOperationException(
            "未能从可执行文件位置定位界面资源目录，请使用 --web-root 指定。");
    }
}
