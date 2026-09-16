using System;
using System.IO;
using Augit.Infrastructure.Files;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class DistributionLayoutTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "augit-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    // 便携版布局：没有仓库标记文件，界面资源就在可执行文件旁边。
    // 这是发布产物唯一可用的定位方式，如果退回成"只向上找仓库"便携版会直接启动失败。
    [TestMethod]
    public void 便携版在可执行文件旁解析出界面资源目录()
    {
        string appDirectory = Path.Combine(_root, "Augit-0.1.0-win-x64-portable");
        Directory.CreateDirectory(Path.Combine(appDirectory, "web", "src"));

        string? located = DistributionLayout.FindAncestorDirectory(appDirectory, ["web"], "Augit.slnx");

        Assert.AreEqual(Path.Combine(appDirectory, "web"), located);
    }

    // 开发布局：可执行文件在 bin 深处，资源目录在仓库根目录，需要向上回溯。
    [TestMethod]
    public void 开发布局向上回溯到仓库根目录()
    {
        string repository = Path.Combine(_root, "Augit");
        Directory.CreateDirectory(Path.Combine(repository, "web"));
        File.WriteAllText(Path.Combine(repository, "Augit.slnx"), "<Solution />");
        string appDirectory = Path.Combine(repository, "src", "Augit.Shell", "bin", "Release", "net10.0-windows");
        Directory.CreateDirectory(appDirectory);

        string? located = DistributionLayout.FindAncestorDirectory(appDirectory, ["web"], "Augit.slnx");

        Assert.AreEqual(Path.Combine(repository, "web"), located);
    }

    // 视觉稿模式加载 docs/ux-mockups，发布产物不含该目录，必须按候选顺序回退。
    [TestMethod]
    public void 存在多个候选时按顺序取第一个()
    {
        string appDirectory = Path.Combine(_root, "installed");
        Directory.CreateDirectory(Path.Combine(appDirectory, "web"));
        Directory.CreateDirectory(Path.Combine(appDirectory, "docs", "ux-mockups"));

        string? located = DistributionLayout.FindAncestorDirectory(
            appDirectory,
            ["web", "docs/ux-mockups"],
            "Augit.slnx");

        Assert.AreEqual(Path.Combine(appDirectory, "web"), located);
    }

    // 便携版目录里同时存在 web 与 docs/ux-mockups 时按调用方顺序返回。
    [TestMethod]
    public void 候选顺序由调用方决定()
    {
        string appDirectory = Path.Combine(_root, "installed");
        Directory.CreateDirectory(Path.Combine(appDirectory, "web"));
        Directory.CreateDirectory(Path.Combine(appDirectory, "docs", "ux-mockups"));

        string? located = DistributionLayout.FindAncestorDirectory(
            appDirectory,
            ["docs/ux-mockups", "web"],
            "Augit.slnx");

        Assert.AreEqual(Path.Combine(appDirectory, "docs", "ux-mockups"), located);
    }

    // 找不到任何候选时返回 null，由调用方决定如何报错。
    [TestMethod]
    public void 找不到候选目录时返回空()
    {
        string appDirectory = Path.Combine(_root, "installed");
        Directory.CreateDirectory(appDirectory);

        Assert.IsNull(DistributionLayout.FindAncestorDirectory(appDirectory, ["web"], "Augit.slnx"));
    }

    // 仓库根目录存在但没有候选目录时立即返回 null，不得继续越过仓库向上命中无关目录。
    [TestMethod]
    public void 回溯不越过仓库根目录()
    {
        string repository = Path.Combine(_root, "Augit");
        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, "Augit.slnx"), "<Solution />");
        string nested = Path.Combine(repository, "src", "Augit.Shell");
        Directory.CreateDirectory(nested);

        // 在仓库之外再放一个同名目录，用来验证回溯不会越过仓库标记。
        Directory.CreateDirectory(Path.Combine(_root, "docs", "ux-mockups"));

        Assert.IsNull(DistributionLayout.FindAncestorDirectory(nested, ["docs/ux-mockups"], "Augit.slnx"));
    }

    [TestMethod]
    public void 拒绝空候选列表()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => DistributionLayout.FindAncestorDirectory(_root, [], "Augit.slnx"));
    }
}
