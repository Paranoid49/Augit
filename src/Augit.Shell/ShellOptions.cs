using Augit.Infrastructure.Files;

namespace Augit.Shell;

/// <summary>
/// 外壳启动参数。默认加载可执行文件旁的 <c>web</c> 目录（开发布局下回退到仓库内的同名目录）；
/// <c>--mockups</c> 改为加载 HTML 视觉稿，使视觉稿可以在原生窗口内直接被渲染，用于逐场景像素对照。
/// </summary>
internal sealed record ShellOptions(string WebRoot, string WorkspaceRoot, string? Scene, string? Theme, int? Width, int? Height, bool ShowFrame, bool PixelExact, string? OpenDocument, string? BlameDocument, string? FileHistoryDocument, string? ConflictDocument, string? DiffDocument, int? Dpi, string? BrowserArguments)
{
    public static ShellOptions Parse(string[] arguments)
    {
        string? webRoot = null;
        string? workspaceRoot = null;
        string? scene = null;
        string? theme = null;
        string? width = null;
        string? height = null;
        bool mockups = false;
        bool showFrame = true;
        bool pixelExact = false;
        string? openDocument = null;
        string? blameDocument = null;
        string? fileHistoryDocument = null;
        string? conflictDocument = null;
        string? diffDocument = null;
        int? dpi = null;
        string? browserArguments = null;

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
            scene,
            theme,
            ParseSize(width, "--width"),
            ParseSize(height, "--height"),
            showFrame,
            pixelExact,
            openDocument,
            blameDocument,
            fileHistoryDocument,
            conflictDocument,
            diffDocument,
            dpi,
            browserArguments);
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
