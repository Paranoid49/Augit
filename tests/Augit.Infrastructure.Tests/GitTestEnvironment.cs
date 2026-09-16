using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

internal static class GitTestEnvironment
{
    public static async Task<GitRuntimeInfo> GetRuntimeAsync()
    {
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        return runtime;
    }

    public static async Task<GitCommandResult> RunAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        GitCommandResult result = await new GitCommandRunner().RunAsync(
            runtime.ExecutablePath!,
            workingDirectory,
            arguments,
            GitCommandMode.LocalWrite);
        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        return result;
    }

    /// <summary>解析版本为完整哈希，供历史比较等需要两版本的测试使用。</summary>
    public static async Task<string> RevParseAsync(
        GitRuntimeInfo runtime,
        string repositoryPath,
        string revision)
    {
        GitCommandResult result = await RunAsync(
            runtime,
            repositoryPath,
            "rev-parse",
            "--verify",
            "--end-of-options",
            $"{revision}^{{commit}}");
        return result.StandardOutput.Trim();
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
        await RunAsync(
            runtime,
            repositoryPath,
            "-c",
            "user.name=Augit Tests",
            "-c",
            "user.email=augit-tests@example.invalid",
            "commit",
            "-m",
            message);
    }
}
