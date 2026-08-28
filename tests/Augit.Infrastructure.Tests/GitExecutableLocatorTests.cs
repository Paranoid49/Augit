using Augit.Core.Git;
using Augit.Infrastructure.Git;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class GitExecutableLocatorTests
{
    [TestMethod]
    public async Task 未配置时从系统路径找到受支持Git()
    {
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);

        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        Assert.IsNotNull(runtime.ExecutablePath);
        Assert.IsTrue(Path.IsPathFullyQualified(runtime.ExecutablePath));
        Assert.IsTrue(runtime.Version >= GitVersion.MinimumSupported);
    }

    [TestMethod]
    public async Task 用户指定路径优先于系统路径()
    {
        GitRuntimeInfo systemRuntime = await GitTestEnvironment.GetRuntimeAsync();

        GitRuntimeInfo configuredRuntime = await new GitExecutableLocator()
            .ResolveAsync(systemRuntime.ExecutablePath);

        Assert.IsTrue(configuredRuntime.IsAvailable, configuredRuntime.UnavailableReason);
        Assert.AreEqual(systemRuntime.ExecutablePath, configuredRuntime.ExecutablePath);
    }

    [TestMethod]
    public async Task 无效用户路径不静默回退系统Git()
    {
        using TemporaryDirectory temporary = new();
        string missingPath = temporary.GetPath("missing-git.exe");

        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(missingPath);

        Assert.AreEqual(GitRuntimeStatus.InvalidConfiguredPath, runtime.Status);
        Assert.IsFalse(runtime.IsAvailable);
        Assert.AreEqual(missingPath, runtime.ExecutablePath);
    }
}
