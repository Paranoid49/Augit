using Augit.Core.Git;

namespace Augit.Infrastructure.Git;

public sealed class GitExecutableLocator : IGitRuntimeLocator
{
    private readonly GitCommandRunner _runner;

    public GitExecutableLocator()
        : this(new GitCommandRunner())
    {
    }

    internal GitExecutableLocator(GitCommandRunner runner)
    {
        _runner = runner;
    }

    public async Task<GitRuntimeInfo> ResolveAsync(
        string? configuredExecutablePath,
        CancellationToken cancellationToken = default)
    {
        bool hasConfiguredPath = !string.IsNullOrWhiteSpace(configuredExecutablePath);
        string? executablePath = hasConfiguredPath
            ? ResolveConfiguredPath(configuredExecutablePath!)
            : FindOnSystemPath("git.exe");
        if (executablePath is null)
        {
            return hasConfiguredPath
                ? GitRuntimeInfo.Unavailable(
                    GitRuntimeStatus.InvalidConfiguredPath,
                    "设置的 Git 可执行文件不存在，请检查路径。",
                    configuredExecutablePath)
                : GitRuntimeInfo.Unavailable(
                    GitRuntimeStatus.NotFound,
                    "未找到 Git for Windows，请安装 2.40 或更高版本，或在设置中指定 git.exe。");
        }

        GitCommandResult result = await _runner.RunAsync(
            executablePath,
            Environment.CurrentDirectory,
            ["--version"],
            GitCommandMode.LocalQuery,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return GitRuntimeInfo.Unavailable(
                GitRuntimeStatus.CannotStart,
                result.ErrorMessage,
                executablePath);
        }

        if (!GitVersion.TryParse(result.StandardOutput, out GitVersion version))
        {
            return GitRuntimeInfo.Unavailable(
                GitRuntimeStatus.UnknownVersion,
                "无法识别 Git 版本，Git 模块已禁用。",
                executablePath);
        }

        if (!version.IsSupported)
        {
            return GitRuntimeInfo.Unavailable(
                GitRuntimeStatus.VersionTooOld,
                $"Git {version} 低于最低支持版本 {GitVersion.MinimumSupported}，Git 模块已禁用。",
                executablePath,
                version);
        }

        return GitRuntimeInfo.Available(executablePath, version);
    }

    private static string? ResolveConfiguredPath(string configuredExecutablePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(configuredExecutablePath.Trim());
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindOnSystemPath(string fileName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string item in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string directory = item.Trim('"');
                string candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
