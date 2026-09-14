using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class TerminalShellResolverTests
{
    [TestMethod]
    public void 首次默认解析WindowsPowerShell且不静默切换()
    {
        using TemporaryDirectory temporary = new();

        TerminalLaunchResult result = TerminalShellResolver.Resolve(new(), temporary.FullPath);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.AreEqual(TerminalShellIds.WindowsPowerShell, result.LaunchInfo!.ShellId);
        StringAssert.EndsWith(result.LaunchInfo.ExecutablePath, "powershell.exe");
        StringAssert.Contains(result.LaunchInfo.Arguments, "-NoExit");
        Assert.IsFalse(result.LaunchInfo.Arguments.Contains("-NoLogo", StringComparison.Ordinal));
    }

    [TestMethod]
    public void 无效自定义命令返回明确失败()
    {
        using TemporaryDirectory temporary = new();
        ApplicationSettings settings = new()
        {
            TerminalShell = TerminalShellIds.Custom,
            TerminalCustomCommand = "Z:\\不存在\\missing-shell.exe --login",
        };

        TerminalLaunchResult result = TerminalShellResolver.Resolve(settings, temporary.FullPath);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage, "自定义终端");
        Assert.IsNull(result.LaunchInfo);
    }

    [TestMethod]
    public void 自定义命令保留带空格路径后的参数()
    {
        bool parsed = TerminalShellResolver.TrySplitCommandLine(
            "\"C:\\Program Files\\Shell\\shell.exe\" --login -i",
            out string? executable,
            out string arguments);

        Assert.IsTrue(parsed);
        Assert.AreEqual("C:\\Program Files\\Shell\\shell.exe", executable);
        Assert.AreEqual("--login -i", arguments);
    }

    [TestMethod]
    public void 自定义批处理命令通过系统Cmd启动并保持会话()
    {
        using TemporaryDirectory temporary = new();
        string script = temporary.GetPath("custom shell.cmd");
        File.WriteAllText(script, "@echo off\r\n");
        ApplicationSettings settings = new()
        {
            TerminalShell = TerminalShellIds.Custom,
            TerminalCustomCommand = $"\"{script}\" --login",
        };

        TerminalLaunchResult result = TerminalShellResolver.Resolve(settings, temporary.FullPath);

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        StringAssert.EndsWith(result.LaunchInfo!.ExecutablePath, "cmd.exe");
        StringAssert.Contains(result.LaunchInfo.Arguments, "/K call");
        StringAssert.Contains(result.LaunchInfo.Arguments, $"\"{script}\" --login");
    }

    [TestMethod]
    public void 前台命令状态在带Ansi颜色的Shell提示符后清除()
    {
        TerminalPromptTracker tracker = new(TerminalShellIds.PowerShell7);

        tracker.OnInput("Get-ChildItem\r");
        tracker.OnOutput("输出\r\n\u001b[32mPS C:\\repo> \u001b[0m");

        Assert.IsFalse(tracker.MayBeRunning);
    }
}
