using System.Diagnostics;
using System.Text.Json;
using Augit.Infrastructure.Settings;
using Augit.Infrastructure.Terminal;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeTerminalStartupTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("loading-panel")]
    [DataRow("loading-document")]
    [DataRow("hide-panel")]
    [DataRow("history-panel")]
    [DataRow("focus-panel")]
    [DataRow("close-panel")]
    [DataRow("workspace-panel")]
    [DataRow("duplicate-panel")]
    [DataRow("reopen-controller")]
    [DataRow("restore-panel")]
    [DataRow("failure-document")]
    [DataRow("hide-controller")]
    [DataRow("history-document")]
    [DataRow("close-environment")]
    [DataRow("close-controller")]
    [DataRow("close-document")]
    [DataRow("workspace-controller")]
    [DataRow("appclose-panel")]
    [DataRow("appclose-environment")]
    [DataRow("appclose-controller")]
    [DataRow("appclose-document")]
    [DataRow("dispose-controller")]
    public async Task 启动中的终端遵守最后操作并回收资源(string scenario)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string settings = temporary.GetPath("settings.json"), resultPath = temporary.GetPath("result.json");
        await new SettingsStore(settings).SaveAsync(new() { TerminalShell = TerminalShellIds.CommandPrompt, Theme = "Light" });
        string executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../Augit.App.VisualAuditHost/bin/Release/net10.0-windows/win-x64/Augit.App.VisualAuditHost.exe"));
        ProcessStartInfo startInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { settings, workspace, $"terminal-startup-{scenario}", resultPath }) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动终端生命周期审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
            Assert.IsTrue(File.Exists(resultPath), $"审计宿主退出但没有结果：{process.ExitCode}");
            string json = await File.ReadAllTextAsync(resultPath);
            TestContext.WriteLine(json);
            using JsonDocument result = JsonDocument.Parse(json);
            JsonElement root = result.RootElement;
            foreach (string property in new[] { "ShellPids", "BrowserPids" })
                foreach (JsonElement pid in root.GetProperty(property).EnumerateArray())
                    Assert.IsFalse(IsRunning(pid.GetInt32()), $"终端进程 {pid} 未释放。");
            string browserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Augit", "WebView2", "Terminal");
            if (Directory.Exists(browserRoot))
                Assert.IsEmpty(Directory.EnumerateDirectories(browserRoot, $"Session-{process.Id}-*"), "会话浏览器目录未释放。");
            using TerminalSessionLease? lease = new TerminalSessionRegistry().TryAcquire(workspace);
            Assert.IsNotNull(lease, "原工作区的终端会话锁未释放。");
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("Error").ValueKind, json);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static bool IsRunning(int pid)
    {
        try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
