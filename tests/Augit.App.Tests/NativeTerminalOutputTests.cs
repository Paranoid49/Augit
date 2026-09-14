using System.Diagnostics;
using System.Text.Json;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeTerminalOutputTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(TerminalShellIds.CommandPrompt, "Light")]
    [DataRow(TerminalShellIds.CommandPrompt, "Dark")]
    [DataRow(TerminalShellIds.WindowsPowerShell, "Light")]
    [DataRow(TerminalShellIds.WindowsPowerShell, "Dark")]
    public async Task 图形宿主打开后无需按键即可显示提示符并收到命令输出(string shell, string theme)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string settings = temporary.GetPath("settings.json"), resultPath = temporary.GetPath("result.json");
        await new SettingsStore(settings).SaveAsync(new() { TerminalShell = shell, Theme = theme });
        string executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../Augit.App.VisualAuditHost/bin/Release/net10.0-windows/win-x64/Augit.App.VisualAuditHost.exe"));
        ProcessStartInfo startInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { settings, workspace, "terminal-output", resultPath }) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动图形终端审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
            Assert.IsTrue(File.Exists(resultPath), $"审计宿主退出但没有结果：{process.ExitCode}");
            string json = await File.ReadAllTextAsync(resultPath);
            TestContext.WriteLine($"Shell={shell} Theme={theme} {json}");
            using JsonDocument result = JsonDocument.Parse(json);
            JsonElement root = result.RootElement;
            Assert.IsFalse(IsRunning(root.GetProperty("ShellPid").GetInt32()), "Shell 未释放。");
            Assert.IsFalse(IsRunning(root.GetProperty("BrowserPid").GetInt32()), "专用浏览器未释放。");
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("Error").ValueKind, json);
            Assert.IsTrue(File.Exists(Path.ChangeExtension(resultPath, ".terminal.png")), "必须取得真实终端正文截图。");
            string browserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Augit", "WebView2", "Terminal");
            if (Directory.Exists(browserRoot))
                Assert.IsEmpty(Directory.EnumerateDirectories(browserRoot, $"Session-{process.Id}-*"), "会话浏览器目录未释放。");
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
        if (pid <= 0) return false;
        try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
