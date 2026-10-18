using Augit.Infrastructure.Settings;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task 布局工作区归属保存时规范化且不更改会话工作区()
    {
        using TemporaryDirectory temporary = new();
        SettingsStore store = new(temporary.GetPath("settings.json"));
        string sessionRoot = temporary.GetPath("session");
        string layoutRoot = temporary.GetPath("layout/../current");
        await store.SaveAsync(new()
        {
            LastWorkspace = sessionRoot,
            ToolWindows = new() { WorkspaceRoot = layoutRoot, ProjectPanelWidth = 360 },
        });
        ApplicationSettings saved = await store.LoadAsync();
        Assert.AreEqual(Path.GetFullPath(layoutRoot), saved.ToolWindows.WorkspaceRoot);
        Assert.AreEqual(sessionRoot, saved.LastWorkspace);
        Assert.AreEqual(360d, saved.ToolWindows.ProjectPanelWidth);
    }

    [TestMethod]
    public async Task 旧外观迁移到测试基准且不丢失工作区会话()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        const string original = """{"theme":"Dark","textFontFamily":"Segoe UI","textFontSize":14,"monospaceFontFamily":"Consolas","fontSize":17,"lastWorkspace":"D:\\repo","openFiles":["a.cs"],"activeFile":"a.cs","toolWindows":{"projectPanelWidth":327},"gitExecutablePath":"custom-git.exe"}""";
        await File.WriteAllTextAsync(path, original);
        SettingsStore store = new(path);
        ApplicationSettings actual = await store.LoadAsync();
        Assert.AreEqual("Light", actual.Theme);
        Assert.AreEqual("Microsoft YaHei UI", actual.TextFontFamily);
        Assert.AreEqual(12d, actual.UiFontSize);
        Assert.AreEqual("JetBrains Mono", actual.MonospaceFontFamily);
        Assert.AreEqual(13d, actual.FontSize);
        Assert.AreEqual(1, actual.AppearanceVersion);
        Assert.AreEqual(@"D:\repo", actual.LastWorkspace);
        Assert.AreEqual("a.cs", actual.ActiveFile);
        Assert.HasCount(1, actual.OpenFiles);
        Assert.AreEqual("a.cs", actual.OpenFiles[0]);
        Assert.AreEqual(327d, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual("custom-git.exe", actual.GitExecutablePath);
        Assert.AreEqual(original, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task 迁移后主题与界面调整可保留而正文固定()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(path, """{"fontSize":17}""");
        SettingsStore store = new(path);
        ApplicationSettings migrated = await store.LoadAsync();
        await store.SaveAsync(migrated with { Theme = "Dark", TextFontSize = 15, FontSize = 22, MonospaceFontFamily = "Consolas" });
        ApplicationSettings saved = await store.LoadAsync();
        Assert.AreEqual("Dark", saved.Theme);
        Assert.AreEqual(15d, saved.UiFontSize);
        Assert.AreEqual("JetBrains Mono", saved.MonospaceFontFamily);
        Assert.AreEqual(13d, saved.FontSize);
        Assert.AreEqual(1, saved.AppearanceVersion);
    }

    [TestMethod]
    public async Task 只有旧主题或旧共用字号也会迁移且新配置保持系统主题()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        SettingsStore store = new(path);
        Assert.AreEqual("System", (await store.LoadAsync()).Theme);
        foreach (string json in new[] { """{"theme":"Dark"}""", """{"fontSize":17}""" })
        {
            await File.WriteAllTextAsync(path, json);
            ApplicationSettings settings = await store.LoadAsync();
            Assert.AreEqual("Light", settings.Theme);
            Assert.AreEqual(12d, settings.UiFontSize);
            Assert.AreEqual(13d, settings.FontSize);
        }
    }

    [TestMethod]
    public async Task 设置文件顶层不是对象时不抛异常且按默认配置回退()
    {
        // 探测 textFontSize 键时曾直接枚举根元素：顶层为 null 会抛 InvalidOperationException，
        // 而它不在 LoadAsync 的捕获范围内，启动与 Git 发现路径都会直接失败。
        // 非对象内容没有可用的用户配置，按默认配置回退（与损坏 JSON 同一口径）：界面 12、等宽 13。
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(path, "null");

        ApplicationSettings actual = await new SettingsStore(path).LoadAsync();

        Assert.AreEqual("System", actual.Theme);
        Assert.AreEqual(12d, actual.UiFontSize);
        Assert.AreEqual(13d, actual.FontSize);
    }

    [TestMethod]
    public async Task 分支面板按目录分组默认开启且可持久化()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        SettingsStore store = new(path);

        // 权威 `DvcsBranchSettings.groupingKeyIds` 的默认值就是 `GROUPING_BY_DIRECTORY`
        // （`platform/dvcs-impl/shared/src/com/intellij/dvcs/branch/DvcsBranchSettings.kt:22-23,26-28`）
        // ⇒ 新配置按目录分组是**开启**的。
        ApplicationSettings fresh = await store.LoadAsync();
        Assert.IsTrue(fresh.GroupBranchesByDirectory);

        // 关掉后要写进设置文件并能读回来。
        await store.SaveAsync(fresh with { GroupBranchesByDirectory = false });
        ApplicationSettings updated = await store.LoadAsync();
        Assert.IsFalse(updated.GroupBranchesByDirectory);

        // 旧配置里没有这个键 ⇒ 回到权威默认（开启），不会因为缺键被当成关闭。
        await File.WriteAllTextAsync(path, "{\"theme\":\"Dark\"}");
        ApplicationSettings legacy = await new SettingsStore(path).LoadAsync();
        Assert.IsTrue(legacy.GroupBranchesByDirectory);
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

        Assert.AreEqual("Light", actual.Theme);
        Assert.AreEqual(1, actual.AppearanceVersion);
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

    [TestMethod]
    public async Task 会话恢复数据去重限量且当前文件必须落在列表内()
    {
        // 启动恢复（规格 §6.7）依赖这里的归一化：列表要去重、限量，
        // 当前文件必须落在列表之内——否则恢复会去激活一个不在标签集合里的文件。
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);
        string[] many = Enumerable.Range(0, 52).Select(index => temporary.GetPath($"file-{index}.cs")).ToArray();

        await store.SaveAsync(new()
        {
            OpenFiles = [many[0], many[0], "   ", many[1], .. many],
            ActiveFile = temporary.GetPath("not-open.cs"),
        });
        ApplicationSettings actual = await store.LoadAsync();

        Assert.HasCount(50, actual.OpenFiles);
        Assert.AreEqual(many[0], actual.OpenFiles[0]);
        Assert.IsNull(actual.ActiveFile);
    }

    [TestMethod]
    public async Task 会话恢复的当前文件按列表项对齐()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);
        string path = temporary.GetPath("Docs/Product-Spec.md");

        await store.SaveAsync(new()
        {
            OpenFiles = [path],
            ActiveFile = path.ToUpperInvariant(),
        });
        ApplicationSettings actual = await store.LoadAsync();

        Assert.AreEqual(path, actual.ActiveFile);
    }
}
