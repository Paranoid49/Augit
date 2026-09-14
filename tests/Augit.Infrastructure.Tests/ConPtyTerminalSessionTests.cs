using System.Diagnostics;
using System.Text.Json;
using Augit.Infrastructure.Settings;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class ConPtyTerminalSessionTests
{
    [TestMethod]
    public async Task WinExe宿主可以收发并回收完整ConPty进程树()
    {
        TerminalTestResult result = await RunTerminalHostAsync(TerminalShellIds.CommandPrompt);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.ForegroundDetected);
        Assert.IsTrue(result.ShellExited);
        Assert.IsTrue(result.ChildExited);
    }

    [TestMethod]
    public async Task WindowsPowerShell可在ConPty中保持交互会话()
    {
        TerminalTestResult result = await RunTerminalHostAsync(TerminalShellIds.WindowsPowerShell);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsTrue(result.ForegroundDetected);
        Assert.IsTrue(result.ShellExited);
        Assert.IsTrue(result.ChildExited);
    }

    private static async Task<TerminalTestResult> RunTerminalHostAsync(string shellId)
    {
        using TemporaryDirectory temporary = new();
        string resultPath = temporary.GetPath("result.json");
        string executable = GetTestHostPath();
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add(temporary.FullPath);
        startInfo.ArgumentList.Add(shellId);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 ConPTY 测试宿主。");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new AssertFailedException("ConPTY 测试宿主超时，已结束其整个进程树。");
        }

        if (!File.Exists(resultPath))
        {
            throw new AssertFailedException($"ConPTY 测试宿主退出码为 {process.ExitCode}，但没有结果文件。");
        }

        string json = await File.ReadAllTextAsync(resultPath);
        TerminalTestResult? result = JsonSerializer.Deserialize<TerminalTestResult>(json);
        if (result is null)
        {
            throw new AssertFailedException("ConPTY 测试宿主没有返回有效结果。");
        }

        if (process.ExitCode != 0)
        {
            throw new AssertFailedException(result.ErrorMessage ?? $"ConPTY 测试宿主退出码为 {process.ExitCode}。");
        }

        return result;
    }

    private static string GetTestHostPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !directory.Name.Equals("tests", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("无法定位 ConPTY 测试宿主目录。");
        }

        return Path.Combine(
            directory.FullName,
            "Augit.Terminal.TestHost",
            "bin",
            "Release",
            "net10.0-windows",
            "win-x64",
            "Augit.Terminal.TestHost.exe");
    }

    private sealed record TerminalTestResult(
        bool IsSuccess,
        bool ForegroundDetected,
        bool ShellExited,
        bool ChildExited,
        string? ErrorMessage);
}
