using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Augit.Shell;

/// <summary>
/// 新版 Augit.exe 的 <c>--apply-update</c> 模式。
///
/// 正在运行的主程序无法覆盖自身，所以主程序退出前启动暂存目录里的**新版本**
/// Augit.exe，由它等待父进程退出、备份旧目录、复制新文件并重启目标程序。
/// 该模式必须在 WebView2 探测和正常窗口初始化之前执行。
/// </summary>
internal static class UpdateApplier
{
    private const string ApplySwitch = "--apply-update";
    private const int ParentExitTimeoutMilliseconds = 90_000;

    public static bool IsUpdateInvocation(string[] arguments)
        => arguments.Any(argument => string.Equals(argument, ApplySwitch, StringComparison.Ordinal));

    public static int Run(string[] arguments)
    {
        UpdateArguments parsed;
        try
        {
            parsed = UpdateArguments.Parse(arguments);
            Log($"update mode start staging={AppContext.BaseDirectory} target={parsed.Target}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        try
        {
            WaitForParent(parsed.ParentProcessId);
            Apply(parsed);
            return 0;
        }
        catch (Exception exception)
        {
            Log($"update failed: {exception}");
            ShowFailure(exception.Message);
            TryRestartOld(parsed);
            return 1;
        }
    }

    private static void Apply(UpdateArguments arguments)
    {
        string target = NormalizeDirectory(arguments.Target);
        string staging = NormalizeDirectory(AppContext.BaseDirectory);
        if (string.Equals(target, staging, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新暂存目录不能与安装目录相同。");
        }
        if (staging.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装目录不能包含更新暂存目录。");
        }
        if (IsRoot(target))
        {
            throw new InvalidOperationException("拒绝更新磁盘根目录。");
        }
        string targetExecutable = Path.Combine(target, "Augit.exe");
        if (!File.Exists(targetExecutable))
        {
            throw new DirectoryNotFoundException($"目标目录缺少 Augit.exe：{target}");
        }
        if (!File.Exists(Path.Combine(staging, "Augit.exe")))
        {
            throw new DirectoryNotFoundException("暂存目录缺少 Augit.exe。");
        }
        if (Directory.Exists(Path.Combine(target, ".git")))
        {
            throw new InvalidOperationException("目标目录看起来是源码目录，拒绝执行便携版更新。");
        }

        string backup = target + ".update-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        if (Directory.Exists(backup))
        {
            throw new IOException($"更新备份目录已存在：{backup}");
        }

        Log($"moving target to backup {backup}");
        MoveDirectoryWithRetry(target, backup);
        try
        {
            Directory.CreateDirectory(target);
            CopyDirectory(staging, target);
            string restart = Path.Combine(target, "Augit.exe");
            Log($"starting updated app {restart}");
            StartAndVerify(restart, target);
            // 只有新进程挺过启动检查才删除备份；目标程序启动时不能抢在检查前删。
            TryDeleteDirectory(backup);
        }
        catch
        {
            Log("new version failed; rolling back");
            TryDeleteDirectory(target);
            if (Directory.Exists(backup))
            {
                Directory.Move(backup, target);
            }
            throw;
        }

        Log("update applied; backup removed");
    }

    private static void MoveDirectoryWithRetry(string source, string destination)
    {
        IOException? last = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException exception)
            {
                last = exception;
                Thread.Sleep(250);
            }
        }
        throw last ?? new IOException("无法移动旧程序目录。");
    }

    private static void StartAndVerify(string executable, string workingDirectory)
    {
        using Process process = Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("无法启动更新后的 Augit。");

        // 等待真实窗口出现，而不是只相信进程创建成功。应用启动失败时通常很快退出；
        // 如果进程存活但一直没有窗口，也必须回滚，避免用户得到一个不可用的新版本。
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (process.HasExited)
            {
                if (process.ExitCode == 0)
                {
                    return;
                }
                throw new InvalidOperationException($"更新后的 Augit 启动失败，退出码 {process.ExitCode}。");
            }
            process.Refresh();
            if (process.MainWindowHandle != 0)
            {
                return;
            }
            Thread.Sleep(250);
        }
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (InvalidOperationException)
        {
            // 进程已退出。
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        throw new TimeoutException("更新后的 Augit 在 10 秒内没有显示窗口。");
    }

    private static void WaitForParent(int parentProcessId)
    {
        if (parentProcessId <= 0)
        {
            throw new ArgumentException("缺少有效的父进程 PID。");
        }
        try
        {
            using Process parent = Process.GetProcessById(parentProcessId);
            if (!parent.WaitForExit(ParentExitTimeoutMilliseconds))
            {
                throw new TimeoutException("等待旧 Augit 退出超时。");
            }
        }
        catch (ArgumentException)
        {
            // 父进程已经退出；这正是正常情况。
        }
    }

    private static void TryRestartOld(UpdateArguments arguments)
    {
        try
        {
            string target = NormalizeDirectory(arguments.Target);
            string executable = Path.Combine(target, "Augit.exe");
            if (File.Exists(executable))
            {
                _ = Process.Start(new ProcessStartInfo(executable)
                {
                    WorkingDirectory = target,
                    UseShellExecute = false,
                });
            }
        }
        catch (Exception exception)
        {
            Log($"restart old failed: {exception.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // 回滚失败由上层错误处理；不再掩盖原始更新错误。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsRoot(string path)
        => string.Equals(path, Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void Log(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Augit",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "update.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ShowFailure(string message)
    {
        try
        {
            _ = MessageBox(0, $"Augit 更新失败，已尝试恢复旧版本。\n\n{message}", "Augit Update", 0x00000010);
        }
        catch (DllNotFoundException)
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint window, string text, string caption, uint type);

    private sealed record UpdateArguments(int ParentProcessId, string Target)
    {
        public static UpdateArguments Parse(string[] arguments)
        {
            int parentProcessId = 0;
            string? target = null;
            for (int index = 0; index < arguments.Length; index++)
            {
                switch (arguments[index])
                {
                    case "--parent-pid":
                        if (++index >= arguments.Length
                            || !int.TryParse(arguments[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentProcessId)
                            || parentProcessId <= 0)
                        {
                            throw new ArgumentException("无效的 --parent-pid。");
                        }
                        break;
                    case "--target":
                        if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]))
                        {
                            throw new ArgumentException("无效的 --target。");
                        }
                        target = arguments[index];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                throw new ArgumentException("缺少 --target。");
            }
            return new UpdateArguments(parentProcessId, target);
        }
    }
}
