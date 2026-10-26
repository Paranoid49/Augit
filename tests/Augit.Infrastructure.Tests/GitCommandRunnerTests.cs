using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitCommandRunnerTests
{
    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    public async Task 同一读取器并行查询取消后真实进程均已退出(int count)
    {
        using TemporaryDirectory temporary = new();
        using CancellationTokenSource cancellation = new();
        // 托管 Windows runner 上多个 Windows PowerShell 并发启动可能明显慢于本地；
        // 给“所有进程都已启动”的观察窗口留出余量，避免把启动慢误判成取消清理失败。
        GitCommandRunner runner = new(TimeSpan.FromSeconds(120), 1024);
        string[] identities = Enumerable.Range(0, count).Select(index => temporary.GetPath($"query-{index}.pid")).ToArray();
        Task<GitCommandResult>[] tasks = identities.Select(file => runner.RunAsync(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            temporary.FullPath,
            ["-NoProfile", "-Command", $"[IO.File]::WriteAllText('{file.Replace("'", "''", StringComparison.Ordinal)}', [string]$PID); Start-Sleep -Seconds 120"],
            GitCommandMode.LocalQuery,
            cancellation.Token)).ToArray();
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(45);
            while (!identities.All(File.Exists) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.IsTrue(identities.All(File.Exists), "所有查询必须均实际启动后再验证取消。");
            int[] processIds = identities.Select(file => int.Parse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            cancellation.Cancel();
            GitCommandResult[] results = await Task.WhenAll(tasks);
            Assert.IsTrue(results.All(result => result.FailureKind == GitOperationFailureKind.Cancelled));
            foreach (int processId in processIds)
            {
                try
                {
                    using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
                    Assert.IsTrue(process.HasExited, "返回后不得保留本次查询进程。");
                }
                catch (ArgumentException)
                {
                    // 进程身份已不存在，符合清理要求。
                }
            }
        }
        finally
        {
            cancellation.Cancel();
            await Task.WhenAll(tasks);
        }
    }

    [TestMethod]
    public async Task 输出超过边界时继续排空但只保留限定长度()
    {
        GitRuntimeInfo runtime = await GitTestEnvironment.GetRuntimeAsync();
        using TemporaryDirectory temporary = new();
        GitCommandRunner runner = new(TimeSpan.FromSeconds(30), 8);

        GitCommandResult result = await runner.RunAsync(
            runtime.ExecutablePath!,
            temporary.FullPath,
            ["--version"],
            GitCommandMode.LocalQuery);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.HasCount(8, result.StandardOutput);
    }

    [TestMethod]
    public async Task 本地查询超时后结束整个进程树()
    {
        using TemporaryDirectory temporary = new();
        GitCommandRunner runner = new(TimeSpan.FromMilliseconds(100), 1024);

        GitCommandResult result = await runner.RunAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            temporary.FullPath,
            ["/d", "/c", "ping 127.0.0.1 -n 10 >nul"],
            GitCommandMode.LocalQuery);

        Assert.AreEqual(GitOperationFailureKind.TimedOut, result.FailureKind);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task 本地写操作可使用单次显式超时()
    {
        using TemporaryDirectory temporary = new();
        GitCommandRunner runner = new(TimeSpan.FromSeconds(30), 1024);

        GitCommandResult result = await runner.RunWithTimeoutAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            temporary.FullPath,
            ["/d", "/c", "ping 127.0.0.1 -n 10 >nul"],
            GitCommandMode.LocalWrite,
            TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(GitOperationFailureKind.TimedOut, result.FailureKind);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task 写操作只响应用户取消并结束整个进程树()
    {
        using TemporaryDirectory temporary = new();
        GitCommandRunner runner = new(TimeSpan.FromSeconds(30), 1024);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
            GitCommandResult result = await runner.RunAsync(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                temporary.FullPath,
                ["/d", "/c", "ping 127.0.0.1 -n 10 >nul"],
                GitCommandMode.LocalWrite,
                cancellation.Token);

            Assert.AreEqual(GitOperationFailureKind.Cancelled, result.FailureKind);
            Assert.IsFalse(result.IsSuccess);
        }
    }

    [TestMethod]
    public async Task 失败命令写到标准输出的凭据也会脱敏()
    {
        using TemporaryDirectory temporary = new();

        GitCommandResult result = await new GitCommandRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            temporary.FullPath,
            ["/d", "/c", "echo https://alice:secret@example.invalid/repo.git & exit /b 1"],
            GitCommandMode.Network);

        Assert.IsFalse(result.IsSuccess);
        Assert.DoesNotContain("secret", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("https://***@example.invalid", result.ErrorMessage, StringComparison.Ordinal);
    }
}
