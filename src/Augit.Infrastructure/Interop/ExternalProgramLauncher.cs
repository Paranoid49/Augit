using System.ComponentModel;
using System.Diagnostics;

namespace Augit.Infrastructure.Interop;

public static class ExternalProgramLauncher
{
    public static ExternalLaunchResult OpenWithDefaultApplication(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Start(new()
        {
            FileName = Path.GetFullPath(filePath),
            UseShellExecute = true,
        });
    }

    public static ExternalLaunchResult OpenUriWithDefaultApplication(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return ExternalLaunchResult.Failure("此链接协议不受支持。");
        }

        return Start(new()
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
    }

    public static ExternalLaunchResult RevealInExplorer(string path)
    {
        ProcessStartInfo startInfo = CreateExplorerStartInfo(path);
        return Start(startInfo);
    }

    public static ExternalLaunchResult OpenExternalTerminal(string path)
    {
        ProcessStartInfo startInfo = CreateTerminalStartInfo(path);
        return Start(startInfo);
    }

    public static ExternalLaunchResult OpenAugitWorkspace(string executablePath, string workspacePath)
    {
        ProcessStartInfo startInfo = CreateAugitWorkspaceStartInfo(executablePath, workspacePath);
        return Start(startInfo);
    }

    public static ProcessStartInfo CreateExplorerStartInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        ProcessStartInfo startInfo = new()
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };
        if (File.Exists(fullPath))
        {
            startInfo.ArgumentList.Add($"/select,{fullPath}");
        }
        else
        {
            startInfo.ArgumentList.Add(fullPath);
        }

        return startInfo;
    }

    public static ProcessStartInfo CreateTerminalStartInfo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath) ?? fullPath;
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            WorkingDirectory = directory,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("-NoExit");
        return startInfo;
    }

    public static ProcessStartInfo CreateAugitWorkspaceStartInfo(string executablePath, string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        string fullWorkspacePath = Path.GetFullPath(workspacePath);
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetFullPath(executablePath),
            WorkingDirectory = fullWorkspacePath,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(fullWorkspacePath);
        return startInfo;
    }

    private static ExternalLaunchResult Start(ProcessStartInfo startInfo)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            return process is null
                ? ExternalLaunchResult.Failure("系统未能启动外部程序。")
                : ExternalLaunchResult.Success();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return ExternalLaunchResult.Failure("无法启动外部程序，请检查系统关联和权限。");
        }
    }
}

public sealed record ExternalLaunchResult(bool IsSuccess, string? ErrorMessage)
{
    public static ExternalLaunchResult Success() => new(true, null);

    public static ExternalLaunchResult Failure(string errorMessage) => new(false, errorMessage);
}
