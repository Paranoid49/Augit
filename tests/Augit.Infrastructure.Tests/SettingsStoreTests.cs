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

    [TestMethod]
    public async Task 保存时归一化窗口摆放()
    {
        // 窗口摆放此前在模型里有字段却没有任何读写与归一化，是 §6.6「已恢复窗口尺寸
        // 不得被默认值覆盖」的缺口；这里钉住"损坏或越界的摆放不会进入设置文件"。
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);

        await store.SaveAsync(new()
        {
            Window = new()
            {
                Left = double.NaN,
                Top = 120,
                Width = 99999,
                Height = double.PositiveInfinity,
            },
        });
        ApplicationSettings actual = await store.LoadAsync();

        // 非有限值的位置被丢弃，外壳会回退到默认位置。
        Assert.IsNull(actual.Window.Left);
        Assert.AreEqual(120d, actual.Window.Top);
        // 过大的尺寸收敛到上限；非法尺寸回退到默认值而不是留下 NaN 或无穷。
        Assert.AreEqual(20000d, actual.Window.Width);
        Assert.AreEqual(760d, actual.Window.Height);
    }

    [TestMethod]
    public async Task 只更新窗口摆放时保留其它字段()
    {
        // 外壳在 WM_CLOSE 里用"读改写"只替换窗口摆放；若整体覆盖，
        // 用户的主题、Git 路径、最近工作区会在每次关窗时丢失。
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);
        await store.SaveAsync(new()
        {
            Theme = "Light",
            GitExecutablePath = @"C:\git\git.exe",
            ToolWindows = new() { ProjectPanelWidth = 320 },
        });

        ApplicationSettings current = await store.LoadAsync();
        await store.SaveAsync(current with
        {
            Window = new() { Left = 40, Top = 40, Width = 1300, Height = 820, IsMaximized = true },
        });
        ApplicationSettings actual = await store.LoadAsync();

        Assert.AreEqual("Light", actual.Theme);
        Assert.AreEqual(@"C:\git\git.exe", actual.GitExecutablePath);
        Assert.AreEqual(320d, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(1300d, actual.Window.Width);
        Assert.AreEqual(820d, actual.Window.Height);
        Assert.AreEqual(40d, actual.Window.Left);
        Assert.IsTrue(actual.Window.IsMaximized);
    }
}
