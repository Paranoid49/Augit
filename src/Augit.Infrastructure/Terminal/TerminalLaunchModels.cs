using Augit.Infrastructure.Settings;

namespace Augit.Infrastructure.Terminal;

public sealed record TerminalLaunchInfo(
    string ShellId,
    string DisplayName,
    string ExecutablePath,
    string Arguments)
{
    public string CommandLine => $"\"{ExecutablePath}\"{(Arguments.Length == 0 ? string.Empty : $" {Arguments}")}";
}

public sealed record TerminalLaunchResult(
    bool IsSuccess,
    TerminalLaunchInfo? LaunchInfo,
    string? ErrorMessage)
{
    public static TerminalLaunchResult Success(TerminalLaunchInfo launchInfo) => new(true, launchInfo, null);

    public static TerminalLaunchResult Failure(string errorMessage) => new(false, null, errorMessage);
}

public static class TerminalShellResolver
{
    public static TerminalLaunchResult Resolve(ApplicationSettings settings, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string shell = TerminalShellIds.Normalize(settings.TerminalShell);
        return shell switch
        {
            TerminalShellIds.PowerShell7 => ResolveKnown(
                shell,
                "PowerShell 7",
                FindProgram("pwsh.exe", workingDirectory, [
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "PowerShell",
                        "7",
                        "pwsh.exe"),
                ]),
                "-NoLogo -NoExit"),
            TerminalShellIds.CommandPrompt => ResolveKnown(
                shell,
                "CMD",
                FindProgram(
                    Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    workingDirectory,
                    [Path.Combine(Environment.SystemDirectory, "cmd.exe")]),
                "/Q"),
            TerminalShellIds.GitBash => ResolveKnown(
                shell,
                "Git Bash",
                FindGitBash(settings.GitExecutablePath, workingDirectory),
                "--login -i"),
            TerminalShellIds.Wsl => ResolveKnown(
                shell,
                "WSL",
                FindProgram(
                    "wsl.exe",
                    workingDirectory,
                    [Path.Combine(Environment.SystemDirectory, "wsl.exe")]),
                string.Empty),
            TerminalShellIds.Custom => ResolveCustom(settings.TerminalCustomCommand, workingDirectory),
            _ => ResolveKnown(
                TerminalShellIds.WindowsPowerShell,
                "Windows PowerShell",
                FindProgram(
                    "powershell.exe",
                    workingDirectory,
                    [Path.Combine(
                        Environment.SystemDirectory,
                        "WindowsPowerShell",
                        "v1.0",
                        "powershell.exe")]),
                "-NoExit"),
        };
    }

    internal static bool TrySplitCommandLine(
        string command,
        out string? executable,
        out string arguments)
    {
        executable = null;
        arguments = string.Empty;
        string value = command.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] == '"')
        {
            int closingQuote = value.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return false;
            }

            executable = value[1..closingQuote];
            arguments = value[(closingQuote + 1)..].TrimStart();
            return executable.IndexOf('"') < 0;
        }

        int separator = value.IndexOfAny([' ', '\t']);
        executable = separator < 0 ? value : value[..separator];
        arguments = separator < 0 ? string.Empty : value[separator..].TrimStart();
        return executable.Length > 0;
    }

    private static TerminalLaunchResult ResolveCustom(string? command, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command)
            || !TrySplitCommandLine(command, out string? executable, out string arguments))
        {
            return TerminalLaunchResult.Failure("自定义终端启动命令为空或格式无效。");
        }

        string? resolved = FindProgram(executable!, workingDirectory, []);
        string extension = resolved is null ? string.Empty : Path.GetExtension(resolved);
        if (resolved is not null
            && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            string? commandPrompt = FindProgram(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                workingDirectory,
                [Path.Combine(Environment.SystemDirectory, "cmd.exe")]);
            string batchArguments = $"/D /Q /K call \"{resolved}\"{(arguments.Length == 0 ? string.Empty : $" {arguments}")}";
            return ResolveKnown(TerminalShellIds.Custom, "自定义终端", commandPrompt, batchArguments);
        }

        return ResolveKnown(TerminalShellIds.Custom, "自定义终端", resolved, arguments);
    }

    private static TerminalLaunchResult ResolveKnown(
        string shellId,
        string displayName,
        string? executable,
        string arguments)
    {
        return executable is null
            ? TerminalLaunchResult.Failure($"未找到已配置的 {displayName}，终端没有启动，也没有切换到其他 Shell。")
            : TerminalLaunchResult.Success(new(shellId, displayName, executable, arguments));
    }

    private static string? FindGitBash(string? configuredGit, string workingDirectory)
    {
        List<string> candidates = [];
        string? git = string.IsNullOrWhiteSpace(configuredGit)
            ? FindProgram("git.exe", workingDirectory, [])
            : FindProgram(configuredGit, workingDirectory, []);
        if (git is not null)
        {
            DirectoryInfo? directory = Directory.GetParent(git);
            if (directory is not null)
            {
                string root = directory.Name is "cmd" or "bin"
                    ? directory.Parent?.FullName ?? directory.FullName
                    : directory.FullName;
                candidates.Add(Path.Combine(root, "bin", "bash.exe"));
                candidates.Add(Path.Combine(root, "usr", "bin", "bash.exe"));
            }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Git", "bin", "bash.exe"));
            candidates.Add(Path.Combine(programFiles, "Git", "usr", "bin", "bash.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindProgram(
        string program,
        string workingDirectory,
        IReadOnlyList<string> preferredPaths)
    {
        foreach (string preferred in preferredPaths)
        {
            if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            {
                return Path.GetFullPath(preferred);
            }
        }

        string expanded = Environment.ExpandEnvironmentVariables(program.Trim());
        if (Path.IsPathRooted(expanded) || expanded.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            try
            {
                string candidate = Path.IsPathRooted(expanded)
                    ? Path.GetFullPath(expanded)
                    : Path.GetFullPath(expanded, workingDirectory);
                return File.Exists(candidate) ? candidate : null;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        string[] extensions = Path.HasExtension(expanded)
            ? [string.Empty]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string extension in extensions)
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim('"'), expanded + extension);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }
}
