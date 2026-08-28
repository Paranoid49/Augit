using Augit.Infrastructure.Settings;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task 设置仅写入指定本地目录并可恢复()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string settingsPath = temporary.GetPath("local/Augit/settings.json");
        SettingsStore store = new(settingsPath);
        ApplicationSettings expected = new()
        {
            LastWorkspace = workspace,
            Theme = "Dark",
            GitExecutablePath = @"C:\Program Files\Git\cmd\git.exe",
            TerminalShell = TerminalShellIds.Custom,
            TerminalCustomCommand = "  custom-shell.exe --login  ",
            OpenFiles = [Path.Combine(workspace, "one.txt")],
            ExpandedDirectories = [workspace],
            RecentWorkspaces = [workspace, workspace],
        };

        await store.SaveAsync(expected);
        ApplicationSettings actual = await store.LoadAsync();

        Assert.AreEqual("Dark", actual.Theme);
        Assert.AreEqual(expected.GitExecutablePath, actual.GitExecutablePath);
        Assert.AreEqual(TerminalShellIds.Custom, actual.TerminalShell);
        Assert.AreEqual("custom-shell.exe --login", actual.TerminalCustomCommand);
        Assert.HasCount(1, actual.OpenFiles);
        Assert.HasCount(1, actual.ExpandedDirectories);
        Assert.HasCount(1, actual.RecentWorkspaces);
        Assert.IsFalse(File.Exists(settingsPath + ".tmp"));
    }

    [TestMethod]
    public async Task 损坏的设置文件安全回退默认值()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(settingsPath, "{损坏");

        ApplicationSettings actual = await new SettingsStore(settingsPath).LoadAsync();

        Assert.AreEqual("System", actual.Theme);
        Assert.IsNull(actual.LastWorkspace);
        Assert.AreEqual(TerminalShellIds.WindowsPowerShell, actual.TerminalShell);
    }

    [TestMethod]
    public async Task 保存时限制最近目录并修正未知终端类型()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        string[] recent = Enumerable.Range(0, 12)
            .Select(index => temporary.GetPath($"workspace-{index}"))
            .ToArray();
        SettingsStore store = new(settingsPath);

        await store.SaveAsync(new()
        {
            TerminalShell = "Unknown",
            TerminalCustomCommand = "   ",
            RecentWorkspaces = recent,
        });
        ApplicationSettings actual = await store.LoadAsync();

        Assert.AreEqual(TerminalShellIds.WindowsPowerShell, actual.TerminalShell);
        Assert.IsNull(actual.TerminalCustomCommand);
        Assert.HasCount(10, actual.RecentWorkspaces);
    }
}
