using Augit.Infrastructure.Interop;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class ExternalProgramLauncherTests
{
    [TestMethod]
    public void 外部终端只通过工作目录传递路径()
    {
        using TemporaryDirectory temporary = new();

        System.Diagnostics.ProcessStartInfo startInfo = ExternalProgramLauncher.CreateTerminalStartInfo(temporary.FullPath);

        Assert.AreEqual("powershell.exe", startInfo.FileName);
        Assert.AreEqual(temporary.FullPath, startInfo.WorkingDirectory);
        Assert.AreEqual("-NoExit", startInfo.ArgumentList.Single());
    }

    [TestMethod]
    public void 新窗口使用当前程序并只传递Worktree目录()
    {
        using TemporaryDirectory temporary = new();
        string executable = temporary.GetPath("Augit.exe");

        System.Diagnostics.ProcessStartInfo startInfo = ExternalProgramLauncher.CreateAugitWorkspaceStartInfo(
            executable,
            temporary.FullPath);

        Assert.AreEqual(executable, startInfo.FileName);
        Assert.AreEqual(temporary.FullPath, startInfo.WorkingDirectory);
        Assert.AreEqual(temporary.FullPath, startInfo.ArgumentList.Single());
        Assert.IsTrue(startInfo.UseShellExecute);
    }
}
