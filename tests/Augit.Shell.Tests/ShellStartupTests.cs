using Augit.Infrastructure.Settings;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// 启动期工作区解析（产品规格 §3：启动时恢复上次打开的目录）。
///
/// 关键区分：命令行显式给的 <c>--workspace</c> 必须优先；只有没给时才用设置里的最近目录，
/// 而且那条路径必须仍然可用，否则退回当前目录——一个失效路径不能让外壳打不开。
/// </summary>
[TestClass]
public sealed class ShellStartupTests
{
    [TestMethod]
    public void 命令行显式给出的工作区优先于设置里的最近目录()
    {
        using TemporaryDirectory explicitDirectory = new();
        using TemporaryDirectory lastDirectory = new();
        ShellOptions options = ShellOptions.Parse(["--workspace", explicitDirectory.FullPath]);

        ShellOptions resolved = ShellStartup.ResolveWorkspace(
            options,
            new ApplicationSettings { LastWorkspace = lastDirectory.FullPath });

        Assert.AreEqual(Path.GetFullPath(explicitDirectory.FullPath), resolved.WorkspaceRoot);
        Assert.AreNotEqual(Path.GetFullPath(lastDirectory.FullPath), resolved.WorkspaceRoot);
    }

    [TestMethod]
    public void 没有显式参数时恢复设置里的最近目录()
    {
        using TemporaryDirectory lastDirectory = new();
        ShellOptions options = ShellOptions.Parse([]);
        Assert.IsFalse(options.WorkspaceExplicit, "没传 --workspace 时不应标记为显式。");

        ShellOptions resolved = ShellStartup.ResolveWorkspace(
            options,
            new ApplicationSettings { LastWorkspace = lastDirectory.FullPath });

        Assert.AreEqual(Path.GetFullPath(lastDirectory.FullPath), resolved.WorkspaceRoot);
    }

    [TestMethod]
    public void 最近目录已不存在时退回当前目录()
    {
        string missing = Path.Combine(Path.GetTempPath(), "Augit.Tests", "gone-" + Guid.NewGuid().ToString("N"));
        ShellOptions options = ShellOptions.Parse([]);

        ShellOptions resolved = ShellStartup.ResolveWorkspace(
            options,
            new ApplicationSettings { LastWorkspace = missing });

        Assert.AreNotEqual(Path.GetFullPath(missing), resolved.WorkspaceRoot);
        Assert.AreEqual(Path.GetFullPath(Environment.CurrentDirectory), resolved.WorkspaceRoot);
    }

    [TestMethod]
    public void 没有最近目录时保持原值()
    {
        ShellOptions options = ShellOptions.Parse([]);

        Assert.AreEqual(options.WorkspaceRoot, ShellStartup.ResolveWorkspace(options, new ApplicationSettings()).WorkspaceRoot);
        Assert.AreEqual(options.WorkspaceRoot, ShellStartup.ResolveWorkspace(
            options, new ApplicationSettings { LastWorkspace = "" }).WorkspaceRoot);
    }

    [TestMethod]
    public void 显式参数被记录()
    {
        using TemporaryDirectory directory = new();
        Assert.IsTrue(ShellOptions.Parse(["--workspace", directory.FullPath]).WorkspaceExplicit);
        Assert.IsFalse(ShellOptions.Parse(["--theme", "dark"]).WorkspaceExplicit);
    }

    [TestMethod]
    public void 首次启动且当前目录是程序目录时使用欢迎空目录()
    {
        using TemporaryDirectory application = new();
        using TemporaryDirectory welcome = new();
        ShellOptions options = ShellOptions.Parse([]) with { WorkspaceRoot = application.FullPath };

        ShellOptions resolved = ShellStartup.ResolveLaunchWorkspace(
            options,
            application.FullPath,
            welcome.FullPath);

        Assert.AreEqual(Path.GetFullPath(welcome.FullPath), resolved.WorkspaceRoot);
        Assert.IsTrue(resolved.Welcome);
    }

    [TestMethod]
    public void 显式工作区或有最近目录时不使用欢迎空目录()
    {
        using TemporaryDirectory application = new();
        using TemporaryDirectory welcome = new();
        using TemporaryDirectory explicitDirectory = new();
        ShellOptions explicitOptions = ShellOptions.Parse(["--workspace", explicitDirectory.FullPath]);

        ShellOptions explicitResolved = ShellStartup.ResolveLaunchWorkspace(
            explicitOptions,
            application.FullPath,
            welcome.FullPath);

        Assert.AreEqual(Path.GetFullPath(explicitDirectory.FullPath), explicitResolved.WorkspaceRoot);

        ShellOptions lastOptions = ShellOptions.Parse([]) with { WorkspaceRoot = explicitDirectory.FullPath };
        ShellOptions lastResolved = ShellStartup.ResolveLaunchWorkspace(
            lastOptions,
            application.FullPath,
            welcome.FullPath);

        Assert.AreEqual(Path.GetFullPath(explicitDirectory.FullPath), lastResolved.WorkspaceRoot);

        // 旧版本可能把发布目录保存成 LastWorkspace；rc.4 仍要把这种值当作欢迎页。
        ShellOptions staleOptions = ShellOptions.Parse([]) with { WorkspaceRoot = application.FullPath };
        ShellOptions staleResolved = ShellStartup.ResolveLaunchWorkspace(
            staleOptions,
            application.FullPath,
            welcome.FullPath);

        Assert.AreEqual(Path.GetFullPath(welcome.FullPath), staleResolved.WorkspaceRoot);
        Assert.IsTrue(staleResolved.Welcome);
    }
}
