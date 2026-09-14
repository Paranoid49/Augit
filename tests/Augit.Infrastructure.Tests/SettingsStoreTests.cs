using Augit.Infrastructure.Settings;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task 新配置使用中文界面字体而旧字体选择继续保留()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        SettingsStore store = new(path);
        ApplicationSettings fresh = await store.LoadAsync();
        Assert.AreEqual("Microsoft YaHei UI", fresh.TextFontFamily);
        await File.WriteAllTextAsync(path, "{\"textFontFamily\":\"Segoe UI Variable Text\",\"monospaceFontFamily\":\"Consolas\"}");
        ApplicationSettings existing = await store.LoadAsync();
        await store.SaveAsync(existing);
        ApplicationSettings reloaded = await store.LoadAsync();
        Assert.AreEqual("Segoe UI Variable Text", reloaded.TextFontFamily);
        Assert.AreEqual("Consolas", reloaded.MonospaceFontFamily);
    }

    [TestMethod]
    public async Task 字体字号分别保存且旧配置保持原有字号()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(path, "{\"fontSize\":17}");
        SettingsStore store = new(path);
        ApplicationSettings legacy = await store.LoadAsync();
        Assert.AreEqual(17d, legacy.UiFontSize);
        Assert.AreEqual(17d, legacy.FontSize);

        await store.SaveAsync(legacy with { TextFontSize = 13, FontSize = 19 });
        ApplicationSettings updated = await store.LoadAsync();
        Assert.AreEqual(13d, updated.UiFontSize);
        Assert.AreEqual(19d, updated.FontSize);

        await store.SaveAsync(updated with { FontSize = 22 });
        ApplicationSettings codeChanged = await store.LoadAsync();
        Assert.AreEqual(13d, codeChanged.UiFontSize);
        Assert.AreEqual(22d, codeChanged.FontSize);
        Assert.DoesNotContain("uiFontSize", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

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
            ActiveFile = Path.Combine(workspace, "one.txt"),
            ToolWindows = new()
            {
                ProjectPanelWidth = 336,
                BottomPanelHeight = 288,
            },
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
        Assert.AreEqual(expected.ActiveFile, actual.ActiveFile);
        Assert.AreEqual(336d, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(288d, actual.ToolWindows.BottomPanelHeight);
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
    public async Task 设置读取兼容旧版首字母大写属性名()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            "{\"Theme\":\"Dark\",\"LastWorkspace\":\"C:\\\\workspace\"}");

        ApplicationSettings actual = await new SettingsStore(settingsPath).LoadAsync();

        Assert.AreEqual("Dark", actual.Theme);
        Assert.AreEqual(@"C:\workspace", actual.LastWorkspace);
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
