using System.Diagnostics;
using Augit.Core.Files;
using Augit.Infrastructure.Files;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class WorkspaceDirectoryServiceTests
{
    private static readonly string[] ExpectedEntryNames = ["Dir2", "dir10", ".env", "File2.txt", "file10.txt"];

    [TestMethod]
    public void 枚举时隐藏Git并按目录和自然顺序排列()
    {
        using TemporaryDirectory temporary = new();
        Directory.CreateDirectory(temporary.GetPath(".git"));
        Directory.CreateDirectory(temporary.GetPath("dir10"));
        Directory.CreateDirectory(temporary.GetPath("Dir2"));
        File.WriteAllText(temporary.GetPath("file10.txt"), string.Empty);
        File.WriteAllText(temporary.GetPath("File2.txt"), string.Empty);
        File.WriteAllText(temporary.GetPath(".env"), string.Empty);

        IReadOnlyList<WorkspaceEntry> entries = WorkspaceDirectoryService.EnumerateChildren(temporary.FullPath);

        CollectionAssert.AreEqual(ExpectedEntryNames, entries.Select(entry => entry.Name).ToArray());
        Assert.IsFalse(entries.Any(entry => entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void 接受本机固定磁盘目录并拒绝网络路径()
    {
        using TemporaryDirectory temporary = new();

        WorkspaceValidationResult local = WorkspaceDirectoryService.ValidateRoot(temporary.FullPath);
        WorkspaceValidationResult network = WorkspaceDirectoryService.ValidateRoot("\\\\server\\share");

        Assert.IsTrue(local.IsValid);
        Assert.IsFalse(network.IsValid);
    }

    [TestMethod]
    public void 目录联接不可展开且越界资源被拒绝()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string outside = temporary.GetPath("outside");
        string junction = Path.Combine(workspace, "linked");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "outside.txt"), "outside");
        CreateJunction(junction, outside);
        try
        {
            IReadOnlyList<WorkspaceEntry> entries = WorkspaceDirectoryService.EnumerateChildren(workspace);
            Assert.HasCount(1, entries);
            WorkspaceEntry entry = entries[0];

            Assert.IsTrue(entry.IsDirectory);
            Assert.IsTrue(entry.IsReparsePoint);
            Assert.IsFalse(entry.CanExpand);
            Assert.IsNull(SafeLocalPathResolver.ResolveWithinWorkspace(
                workspace,
                Path.Combine(junction, "outside.txt")));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, false);
            }
        }
    }

    private static void CreateJunction(string junction, string target)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/D");
        startInfo.ArgumentList.Add("/C");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(target);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动目录联接测试进程。");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, output);
    }
}
