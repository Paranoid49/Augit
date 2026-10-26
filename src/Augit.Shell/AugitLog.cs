using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Augit.Shell;

/// <summary>
/// 本地诊断日志。只写本机文件，不上传；用于定位启动/桥接/未处理异常导致的退出。
///
/// 设计边界：
/// - 日志只记录版本、系统、启动参数、工作区路径、异常类型与堆栈；不记录文件内容、Git 输出或凭据。
/// - 目录不可写、磁盘满或并发追加失败时静默忽略，诊断不能反过来影响产品。
/// - 单文件超过 4 MB 时轮换为 <c>augit.log.1</c>，避免长期运行时无限增长。
/// </summary>
internal static class AugitLog
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _logPath;
    private static bool _initialized;

    public static string? LogPath => _logPath;

    public static void Initialize(string[] arguments)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Augit",
                "logs");
            Directory.CreateDirectory(directory);
            _logPath = Path.Combine(directory, "augit.log");
        }
        catch
        {
            _logPath = null;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Error("unhandled-exception", eventArgs.ExceptionObject as Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Info("process-exit");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Error("unobserved-task-exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        Info($"startup version={Version()} os={RuntimeInformation.OSDescription} runtime={RuntimeInformation.FrameworkDescription}");
        Info($"startup process={Environment.ProcessPath} cwd={Environment.CurrentDirectory}");
        Info($"startup args={string.Join(' ', arguments.Select(QuoteForLog))}");
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string context, Exception? exception) => Write("ERROR", context, exception);

    private static string Version()
    {
        try
        {
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string QuoteForLog(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }

    private static void Write(string level, string message, Exception? exception)
    {
        string? path = _logPath;
        if (path is null)
        {
            return;
        }

        try
        {
            string line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            line += Environment.NewLine;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaximumLogBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }

                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 诊断失败不能影响产品。
        }
    }
}
