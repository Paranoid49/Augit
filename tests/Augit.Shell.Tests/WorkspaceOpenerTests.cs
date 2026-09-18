using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// 「打开工作区」的决策（产品规格 §2：同一目录已经打开时激活原窗口；
/// 单窗口不管理多个仓库，因此不同目录用新窗口）。
///
/// 激活与启动由调用方注入：真去激活窗口或启动进程会污染开发机，不适合放进测试，
/// 但四种结果（当前目录 / 已激活 / 新启动 / 无效）都必须可判定。
/// </summary>
[TestClass]
public sealed class WorkspaceOpenerTests
{
    [TestMethod]
    public void 请求当前工作区时不激活也不启动()
    {
        using TemporaryDirectory current = new();
        bool activated = false;
        bool launched = false;

        WorkspaceOpenResult result = WorkspaceOpener.Open(
            current.FullPath, current.FullPath, _ => activated = true, _ => launched = true);

        Assert.AreEqual(WorkspaceOpenOutcome.Current, result.Outcome);
        Assert.IsFalse(activated);
        Assert.IsFalse(launched);
    }

    [TestMethod]
    public void 已有窗口时激活它而不启动新窗口()
    {
        using TemporaryDirectory current = new();
        using TemporaryDirectory other = new();
        string? activatedPath = null;
        bool launched = false;

        WorkspaceOpenResult result = WorkspaceOpener.Open(
            current.FullPath, other.FullPath, path => { activatedPath = path; return true; }, _ => launched = true);

        Assert.AreEqual(WorkspaceOpenOutcome.Activated, result.Outcome);
        Assert.AreEqual(Path.GetFullPath(other.FullPath), activatedPath);
        Assert.IsFalse(launched);
    }

    [TestMethod]
    public void 没有已有窗口时启动新窗口()
    {
        using TemporaryDirectory current = new();
        using TemporaryDirectory other = new();
        string? launchedPath = null;

        WorkspaceOpenResult result = WorkspaceOpener.Open(
            current.FullPath, other.FullPath, _ => false, path => { launchedPath = path; return true; });

        Assert.AreEqual(WorkspaceOpenOutcome.Launched, result.Outcome);
        Assert.AreEqual(Path.GetFullPath(other.FullPath), launchedPath);
    }

    [TestMethod]
    public void 启动失败时给出原因()
    {
        using TemporaryDirectory current = new();
        using TemporaryDirectory other = new();

        WorkspaceOpenResult result = WorkspaceOpener.Open(
            current.FullPath, other.FullPath, _ => false, _ => false);

        Assert.AreEqual(WorkspaceOpenOutcome.Invalid, result.Outcome);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reason));
    }

    [TestMethod]
    public void 目录不存在时不激活也不启动()
    {
        using TemporaryDirectory current = new();
        string missing = Path.Combine(Path.GetTempPath(), "Augit.Tests", "missing-" + Guid.NewGuid().ToString("N"));
        bool called = false;

        WorkspaceOpenResult result = WorkspaceOpener.Open(
            current.FullPath, missing, _ => { called = true; return true; }, _ => { called = true; return true; });

        Assert.AreEqual(WorkspaceOpenOutcome.Invalid, result.Outcome);
        Assert.IsFalse(called, "无效路径不得触发激活或启动。");
        StringAssert.Contains(result.Reason, "目录");
    }
}
