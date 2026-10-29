using Augit.Shell;

namespace Augit.Shell.Tests;

/// <summary>
/// 任务栏悬停/Alt-Tab 标题按工作区目录名显示，避免多个 Augit 窗口都叫 "Augit"。
/// </summary>
[TestClass]
public sealed class ShellWindowTitleTests
{
    [TestMethod]
    public void 项目窗口标题取工作区目录名()
    {
        Assert.AreEqual(
            "ops-server-report-resend",
            ShellWindowTitle.Resolve(@"D:\project\ops-server-report-resend\", false, null, false));
    }

    [TestMethod]
    public void 欢迎页和视觉稿场景保留产品名()
    {
        Assert.AreEqual("Augit", ShellWindowTitle.Resolve(@"D:\project\demo", true, null, false));
        Assert.AreEqual("Augit", ShellWindowTitle.Resolve(@"D:\project\demo", false, "main-project", false));
    }

    [TestMethod]
    public void 设置窗口使用Settings标题()
    {
        Assert.AreEqual("Settings", ShellWindowTitle.Resolve(@"D:\project\demo", false, null, true));
    }

    [TestMethod]
    public void 根路径或空路径回退到产品名()
    {
        Assert.AreEqual("Augit", ShellWindowTitle.Resolve(@"C:\", false, null, false));
        Assert.AreEqual("Augit", ShellWindowTitle.Resolve("", false, null, false));
        Assert.AreEqual("Augit", ShellWindowTitle.Resolve(null!, false, null, false));
    }

    [TestMethod]
    public void 带空格和尾部分隔符的项目名完整保留()
    {
        Assert.AreEqual(
            "ops server report",
            ShellWindowTitle.Resolve(@"D:\project\ops server report/", false, null, false));
    }
}
