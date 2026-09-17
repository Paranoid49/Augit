using System;
using System.Diagnostics;
using System.IO;
using Augit.Infrastructure.Files;

namespace Augit.Infrastructure.Tests;

/// <summary>
/// 工作区内路径解析的边界测试。
/// </summary>
/// <remarks>
/// 宿主侧"用系统程序打开仓库内路径"（规格 §5.4 项目树菜单）依赖这个解析器。
/// 它必须挡住两类越界：<c>..</c> 之类的字面越界，以及**工作区内的符号链接**指向外部
/// ——后者只比较字面路径是挡不住的，会让"在外部终端打开"把终端开在工作区之外。
/// </remarks>
[TestClass]
public sealed class SafeLocalPathResolverTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "augit-safe-" + Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public void 工作区内的已存在路径正常解析()
    {
        string workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(Path.Combine(workspace, "docs"));
        File.WriteAllText(Path.Combine(workspace, "docs", "a.txt"), "x");

        string? resolved = SafeLocalPathResolver.ResolveWithinWorkspace(
            workspace,
            Path.Combine(workspace, "docs", "a.txt"));

        Assert.IsNotNull(resolved);
        Assert.AreEqual(Path.Combine(workspace, "docs", "a.txt"), resolved);
    }

    // 字面越界：`..` 逃出工作区。
    [TestMethod]
    public void 字面越界被拒绝()
    {
        string workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "x");

        string? resolved = SafeLocalPathResolver.ResolveWithinWorkspace(
            workspace,
            Path.Combine(workspace, "..", "outside.txt"));

        Assert.IsNull(resolved);
    }

    // 符号链接越界：路径字面上在工作区内，但链接目标在外面。
    // 这是"只比较字面路径"挡不住的情形，也是宿主启动外部程序必须防的那一类。
    [TestMethod]
    public void 工作区内的目录链接指向外部时被拒绝()
    {
        string workspace = Path.Combine(_root, "ws");
        string outside = Path.Combine(_root, "outside");
        string junction = Path.Combine(workspace, "linked");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "x");
        if (!TryCreateJunction(junction, outside))
        {
            Assert.Inconclusive("当前环境无法创建目录联接，跳过。");
        }

        try
        {
            // 链接本身与链接下的文件都必须被拒绝。
            Assert.IsNull(SafeLocalPathResolver.ResolveWithinWorkspace(workspace, junction));
            Assert.IsNull(SafeLocalPathResolver.ResolveWithinWorkspace(
                workspace,
                Path.Combine(junction, "secret.txt")));
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, false);
            }
        }
    }

    // 不存在的路径一律拒绝：调用方据此如实报错，而不是启动一个指向空路径的程序。
    [TestMethod]
    public void 不存在的路径被拒绝()
    {
        string workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(workspace);

        Assert.IsNull(SafeLocalPathResolver.ResolveWithinWorkspace(
            workspace,
            Path.Combine(workspace, "missing.txt")));
    }

    /// <summary>用 mklink /J 创建目录联接（不需要管理员权限）。</summary>
    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process?.WaitForExit(10000);
            return process?.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
