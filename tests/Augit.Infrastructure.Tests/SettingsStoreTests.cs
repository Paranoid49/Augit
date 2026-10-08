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
        // 全新配置（文件不存在）采用本次 PyCharm 对照基线：界面 12px、等宽 13px。
        // 12px 只是新配置的初值，不是覆盖已有用户的强制值（见下面的旧配置用例）。
        Assert.AreEqual(12d, fresh.UiFontSize);
        Assert.AreEqual(13d, fresh.FontSize);
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
        // 旧配置只保存一个共用字号：两类文字先沿用旧值 17，不被新配置的 12px 对照基线改写
        // （design-system.md §5.3 末）。
        Assert.IsNull(legacy.TextFontSize);
        Assert.AreEqual(17d, legacy.UiFontSize);
        Assert.AreEqual(17d, legacy.FontSize);

        // 旧配置整体回写后 textFontSize 会以 null 落盘；重新读取仍要沿用共用字号，
        // 不能在一次保存之后就跳回默认值。
        await store.SaveAsync(legacy);
        ApplicationSettings roundTripped = await store.LoadAsync();
        Assert.IsNull(roundTripped.TextFontSize);
        Assert.AreEqual(17d, roundTripped.UiFontSize);

        await store.SaveAsync(roundTripped with { TextFontSize = 13, FontSize = 19 });
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
    public async Task 旧配置缺少共用字号时沿用默认字号而不套用新界面基线()
    {
        // 已经存在 settings.json 的用户即使没写过 fontSize，升级前也是按 FontSize 默认值 13 渲染；
        // 迁移不得把这份既有配置改成新配置才有的 12px 对照基线，否则等于替用户改了设置。
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(path, "{\"theme\":\"Dark\"}");
        SettingsStore store = new(path);

        ApplicationSettings legacy = await store.LoadAsync();

        Assert.AreEqual("Dark", legacy.Theme);
        Assert.IsNull(legacy.TextFontSize);
        Assert.AreEqual(13d, legacy.UiFontSize);
        Assert.AreEqual(13d, legacy.FontSize);
    }

    [TestMethod]
    public async Task 新配置保存后界面字号与等宽字号各自独立()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("settings.json");
        SettingsStore store = new(path);
        ApplicationSettings fresh = await store.LoadAsync();

        // 新配置首次保存就把两个字号分开落盘，互不覆盖。
        await store.SaveAsync(fresh with { TextFontSize = 15, FontSize = 18 });
        ApplicationSettings saved = await store.LoadAsync();

        Assert.AreEqual(15d, saved.UiFontSize);
        Assert.AreEqual(18d, saved.FontSize);
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
