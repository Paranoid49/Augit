namespace Augit.Shell;

/// <summary>
/// 外壳启动参数。默认加载仓库内的 <c>web</c> 目录；<c>--mockups</c> 改为加载 HTML 视觉稿，
/// 使视觉稿可以在原生窗口内直接被渲染，用于逐场景像素对照。
/// </summary>
internal sealed record ShellOptions(string WebRoot, string WorkspaceRoot, string? Scene, string? Theme, int? Width, int? Height, bool ShowFrame, bool PixelExact, string? OpenDocument)
{
    private const string DefaultWidth = "1180";
    private const string DefaultHeight = "760";

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
                default:
                    throw new ArgumentException($"未知的启动参数：{argument}");
            }
        }

        string root = webRoot is null
            ? Path.Combine(FindRepositoryRoot(), mockups ? "docs" : "web")
            : Path.GetFullPath(webRoot);
        return new ShellOptions(
            root,
            Path.GetFullPath(workspaceRoot ?? Environment.CurrentDirectory),
            scene,
            theme,
            ParseSize(width, DefaultWidth, "--width"),
            ParseSize(height, DefaultHeight, "--height"),
            showFrame,
            pixelExact,
            openDocument);
    }

    private static string Next(string[] arguments, ref int index, string name)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"{name} 缺少取值。");
        }

        return arguments[++index];
    }

    private static int? ParseSize(string? value, string fallback, string name)
    {
        string effective = value ?? fallback;
        if (!int.TryParse(effective, out int parsed) || parsed < 320)
        {
            throw new ArgumentException($"{name} 必须是大于等于 320 的整数。");
        }

        return parsed;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Augit.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能从可执行文件位置定位仓库根目录，请使用 --web-root 指定界面资源目录。");
    }
}
