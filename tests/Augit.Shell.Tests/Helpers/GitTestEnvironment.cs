using System.Diagnostics;
using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Shell.Tests.Helpers;

/// <summary>一次 Git 调用的结果（宿主测试只需要成功与否和标准输出）。</summary>
internal sealed record GitCall(bool Success, string StandardOutput, string ErrorMessage);

/// <summary>
/// 外壳测试用的最小 Git 工具。刻意不复用基础设施测试的同名工具：
/// 那些类型是 `Augit.Infrastructure` 的内部契约，只对基础设施测试程序集可见。
/// </summary>
internal static class GitTestEnvironment
{
    public static async Task<GitRuntimeInfo> GetRuntimeAsync()
    {
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        return runtime;
    }

    public static async Task RunAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        GitCall call = await RunRawAsync(runtime, workingDirectory, arguments);
        Assert.IsTrue(call.Success, call.ErrorMessage);
    }

    public static async Task<GitCall> RunRawAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new(runtime.ExecutablePath!)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new GitCall(process.ExitCode == 0, standardOutput, standardError);
    }

    public static async Task CommitFileAsync(
        GitRuntimeInfo runtime,
        string repositoryPath,
        string relativePath,
        string content,
        string message)
    {
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, relativePath), content);
        await RunAsync(runtime, repositoryPath, "add", "--", relativePath);
        await RunAsync(runtime, repositoryPath, "commit", "-m", message);
    }
}
