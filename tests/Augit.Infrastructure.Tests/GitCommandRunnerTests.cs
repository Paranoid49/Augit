using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitCommandRunnerTests
{
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
