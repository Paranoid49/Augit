using System.Text.Json;
using Augit.Infrastructure.Settings;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// `settings/write` 对工具窗口面板尺寸的宿主契约（规格 §4.2：拖动分隔条后持久化尺寸）。
///
/// 网页层 `live-data.js` 的 `persistPanelSize` 每次只发一个字段
/// （`projectPanelWidth` 或 `bottomPanelHeight`），宿主必须把合法的有限数字合并进
/// `toolWindows`（侧宽 240–1600、底高 180–1200，与 `SettingsStore` 的归一化范围一致），
/// 其余情况保留原尺寸，不能把拖动中间态或非法值落盘。
///
/// 测试固定用临时目录里的显式 <see cref="SettingsStore"/>，不读写用户默认设置文件。
/// </summary>
[TestClass]
public sealed class ShellBridgeSettingsTests
{
    private const double SeededPanelWidth = 300;
    private const double SeededPanelHeight = 260;

    [TestMethod]
    public async Task 会话切换保留旧布局归属而不会把尺寸带给新工作区()
    {
        using TemporaryDirectory temporary = new();
        string previous = temporary.GetPath("previous");
        string workspace = temporary.GetPath("current");
        Directory.CreateDirectory(previous);
        Directory.CreateDirectory(workspace);
        SettingsStore store = new(temporary.GetPath("settings.json"));
        await store.SaveAsync(new()
        {
            LastWorkspace = previous,
            ToolWindows = new() { ProjectPanelWidth = 550, BottomPanelHeight = 420 },
        });
        using ShellBridge bridge = new(workspace, settingsStore: store);
        _ = await bridge.HandleAsync(
            """{"id":1,"method":"session/write","params":{"openFiles":[],"expandedDirectories":[]}}""",
            CancellationToken.None);
        ApplicationSettings saved = await store.LoadAsync();
        Assert.AreEqual(workspace, saved.LastWorkspace);
        Assert.AreEqual(previous, saved.ToolWindows.WorkspaceRoot);
        using JsonDocument response = JsonDocument.Parse(await bridge.HandleAsync(
            """{"id":2,"method":"settings/read","params":{}}""", CancellationToken.None));
        Assert.IsFalse(response.RootElement.GetProperty("result").TryGetProperty("projectPanelWidth", out _));
    }

    [TestMethod]
    public async Task 新工作区仅拖布局也可恢复尺寸但不会接管旧标签会话()
    {
        using TemporaryDirectory temporary = new();
        string previous = temporary.GetPath("previous");
        string workspace = temporary.GetPath("current");
        Directory.CreateDirectory(previous);
        Directory.CreateDirectory(workspace);
        string previousFile = Path.Combine(previous, "old.txt");
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);
        await store.SaveAsync(new()
        {
            LastWorkspace = previous,
            OpenFiles = [previousFile],
            ActiveFile = previousFile,
            ExpandedDirectories = [previous],
            ToolWindows = new() { ProjectPanelWidth = 500, BottomPanelHeight = 420 },
        });
        using ShellBridge bridge = new(workspace, settingsStore: store);
        await WriteAsync(bridge, store, """{"projectPanelWidth":360}""");
        ApplicationSettings saved = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(workspace, saved.ToolWindows.WorkspaceRoot);
        Assert.AreEqual(360d, saved.ToolWindows.ProjectPanelWidth);
        Assert.IsNull(saved.ToolWindows.BottomPanelHeight);
        Assert.AreEqual(previous, saved.LastWorkspace);
        CollectionAssert.AreEqual(new[] { previousFile }, saved.OpenFiles);
        Assert.AreEqual(previousFile, saved.ActiveFile);

        using JsonDocument current = JsonDocument.Parse(await bridge.HandleAsync(
            """{"id":1,"method":"settings/read","params":{}}""", CancellationToken.None));
        JsonElement result = current.RootElement.GetProperty("result");
        Assert.AreEqual(360d, result.GetProperty("projectPanelWidth").GetDouble());
        Assert.AreEqual(0, result.GetProperty("openFiles").GetArrayLength());
        Assert.IsFalse(result.TryGetProperty("activeFile", out _));

        using ShellBridge previousBridge = new(previous, settingsStore: store);
        using JsonDocument other = JsonDocument.Parse(await previousBridge.HandleAsync(
            """{"id":2,"method":"settings/read","params":{}}""", CancellationToken.None));
        JsonElement previousResult = other.RootElement.GetProperty("result");
        Assert.IsFalse(previousResult.TryGetProperty("projectPanelWidth", out _));
        Assert.AreEqual(previousFile, previousResult.GetProperty("openFiles")[0].GetString());
    }

    [TestMethod]
    public async Task 同工作区旧配置布局仍可读取且普通设置写入不改尺寸归属()
    {
        using TemporaryDirectory temporary = new();
        SettingsStore store = new(temporary.GetPath("settings.json"));
        await store.SaveAsync(new()
        {
            LastWorkspace = temporary.FullPath,
            ToolWindows = new() { ProjectPanelWidth = 330, BottomPanelHeight = 240 },
        });
        using ShellBridge bridge = new(temporary.FullPath, settingsStore: store);
        using JsonDocument response = JsonDocument.Parse(await bridge.HandleAsync(
            """{"id":1,"method":"settings/read","params":{}}""", CancellationToken.None));
        Assert.AreEqual(330d, response.RootElement.GetProperty("result").GetProperty("projectPanelWidth").GetDouble());
        await WriteAsync(bridge, store, """{"theme":"Dark","projectPanelWidth":-1}""");
        ApplicationSettings saved = await store.LoadAsync();
        Assert.IsNull(saved.ToolWindows.WorkspaceRoot);
        Assert.AreEqual(330d, saved.ToolWindows.ProjectPanelWidth);
    }

    [TestMethod]
    public async Task 面板尺寸的两个值都会写入并能从设置文件重读()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("local/Augit/settings.json");
        using ShellBridge bridge = new(temporary.FullPath);
        SettingsStore store = new(settingsPath);

        await WriteAsync(bridge, store, """{"projectPanelWidth":360,"bottomPanelHeight":300}""");

        // 重新构造 Store 读盘，证明确实写进了设置文件，而不是只改了内存里的对象。
        ApplicationSettings saved = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(360d, saved.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(300d, saved.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    public async Task 面板尺寸按单个字段合并并保留未提交的一侧()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        using ShellBridge bridge = new(temporary.FullPath);
        SettingsStore store = new(settingsPath);
        await SeedPanelsAsync(store, SeededPanelWidth, SeededPanelHeight);

        await WriteAsync(bridge, store, """{"projectPanelWidth":420}""");
        ApplicationSettings widthOnly = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(420d, widthOnly.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(SeededPanelHeight, widthOnly.ToolWindows.BottomPanelHeight);

        await WriteAsync(bridge, store, """{"bottomPanelHeight":240}""");
        ApplicationSettings heightOnly = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(420d, heightOnly.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(240d, heightOnly.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    public async Task 一个尺寸非法时仍保存另一个有效尺寸()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        using ShellBridge bridge = new(temporary.FullPath);
        SettingsStore store = new(settingsPath);
        await SeedPanelsAsync(store, SeededPanelWidth, SeededPanelHeight);

        await WriteAsync(bridge, store, """{"projectPanelWidth":-1,"bottomPanelHeight":480}""");
        ApplicationSettings saved = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(SeededPanelWidth, saved.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(480d, saved.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    public async Task 空布局也可合并合法尺寸()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"toolWindows":null}""");
        using ShellBridge bridge = new(temporary.FullPath);

        await WriteAsync(bridge, new SettingsStore(settingsPath), """{"projectPanelWidth":400}""");
        ApplicationSettings saved = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(400d, saved.ToolWindows.ProjectPanelWidth);
        Assert.IsNull(saved.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    public async Task 只写布局时一并保存已迁移的测试外观基准()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"fontSize":16}""");
        using ShellBridge bridge = new(temporary.FullPath);

        await WriteAsync(bridge, new SettingsStore(settingsPath), """{"projectPanelWidth":400}""");
        ApplicationSettings saved = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(12d, saved.TextFontSize);
        Assert.AreEqual(12d, saved.UiFontSize);
        Assert.AreEqual(13d, saved.FontSize);
        Assert.AreEqual(400d, saved.ToolWindows.ProjectPanelWidth);
    }

    [TestMethod]
    [DataRow("""{"projectPanelWidth":240,"bottomPanelHeight":180}""", 240d, 180d)]
    [DataRow("""{"projectPanelWidth":1600,"bottomPanelHeight":1200}""", 1600d, 1200d)]
    [DataRow("""{"projectPanelWidth":601,"bottomPanelHeight":777}""", 601d, 777d)]
    public async Task 面板尺寸边界内的值会被采用(string payload, double expectedWidth, double expectedHeight)
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        using ShellBridge bridge = new(temporary.FullPath);
        SettingsStore store = new(settingsPath);
        await SeedPanelsAsync(store, SeededPanelWidth, SeededPanelHeight);

        await WriteAsync(bridge, store, payload);

        ApplicationSettings actual = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(expectedWidth, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(expectedHeight, actual.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{"projectPanelWidth":null,"bottomPanelHeight":null}""")]
    [DataRow("""{"projectPanelWidth":"360","bottomPanelHeight":true}""")]
    [DataRow("""{"projectPanelWidth":0,"bottomPanelHeight":0}""")]
    [DataRow("""{"projectPanelWidth":-1,"bottomPanelHeight":-20}""")]
    [DataRow("""{"projectPanelWidth":239,"bottomPanelHeight":179}""")]
    [DataRow("""{"projectPanelWidth":1601,"bottomPanelHeight":1201}""")]
    [DataRow("""{"projectPanelWidth":1e999,"bottomPanelHeight":-1e999}""")]
    [DataRow("""{"projectPanelWidth":[360],"bottomPanelHeight":{"width":300}}""")]
    public async Task 面板尺寸的非法类型与越界值保留原尺寸(string payload)
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("settings.json");
        using ShellBridge bridge = new(temporary.FullPath);
        SettingsStore store = new(settingsPath);
        await SeedPanelsAsync(store, SeededPanelWidth, SeededPanelHeight);

        await WriteAsync(bridge, store, payload);

        ApplicationSettings actual = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(SeededPanelWidth, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(SeededPanelHeight, actual.ToolWindows.BottomPanelHeight);
    }

    [TestMethod]
    public async Task 写入面板尺寸保留其它设置与窗口和会话数据()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string source = Path.Combine(workspace, "src");
        Directory.CreateDirectory(source);
        string file = Path.Combine(source, "Program.cs");
        string settingsPath = temporary.GetPath("settings.json");
        SettingsStore store = new(settingsPath);
        await store.SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = "Dark",
            TextFontFamily = "Segoe UI",
            MonospaceFontFamily = "Consolas",
            FontSize = 17,
            TextFontSize = 15,
            GitExecutablePath = @"C:\git\git.exe",
            TerminalShell = TerminalShellIds.Custom,
            TerminalCustomCommand = "custom-shell.exe",
            ShowGitBranchesTags = false,
            GroupBranchesByDirectory = false,
            HideLargeFileWarning = true,
            Window = new() { Left = 40, Top = 40, Width = 1300, Height = 820, IsMaximized = true },
            ToolWindows = new() { ProjectPanelWidth = SeededPanelWidth, BottomPanelHeight = SeededPanelHeight },
            OpenFiles = [file],
            ActiveFile = file,
            ExpandedDirectories = [source],
            RecentWorkspaces = [workspace],
        });

        using ShellBridge bridge = new(workspace);
        await WriteAsync(bridge, store, """{"projectPanelWidth":400}""");

        ApplicationSettings actual = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual(400d, actual.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(SeededPanelHeight, actual.ToolWindows.BottomPanelHeight);
        Assert.AreEqual("Dark", actual.Theme);
        Assert.AreEqual("Segoe UI", actual.TextFontFamily);
        Assert.AreEqual("JetBrains Mono", actual.MonospaceFontFamily);
        Assert.AreEqual(13d, actual.FontSize);
        Assert.AreEqual(15d, actual.UiFontSize);
        Assert.AreEqual(@"C:\git\git.exe", actual.GitExecutablePath);
        Assert.AreEqual(TerminalShellIds.Custom, actual.TerminalShell);
        Assert.AreEqual("custom-shell.exe", actual.TerminalCustomCommand);
        Assert.IsFalse(actual.ShowGitBranchesTags);
        Assert.IsFalse(actual.GroupBranchesByDirectory);
        Assert.IsTrue(actual.HideLargeFileWarning);
        Assert.AreEqual(40d, actual.Window.Left);
        Assert.AreEqual(40d, actual.Window.Top);
        Assert.AreEqual(1300d, actual.Window.Width);
        Assert.AreEqual(820d, actual.Window.Height);
        Assert.IsTrue(actual.Window.IsMaximized);
        Assert.AreEqual(workspace, actual.LastWorkspace);
        Assert.HasCount(1, actual.OpenFiles);
        Assert.AreEqual(file, actual.OpenFiles[0]);
        Assert.AreEqual(file, actual.ActiveFile);
        Assert.HasCount(1, actual.ExpandedDirectories);
        Assert.AreEqual(source, actual.ExpandedDirectories[0]);
        Assert.HasCount(1, actual.RecentWorkspaces);
        Assert.AreEqual(workspace, actual.RecentWorkspaces[0]);
    }

    private static async Task SeedPanelsAsync(SettingsStore store, double width, double height)
    {
        await store.SaveAsync(new()
        {
            ToolWindows = new() { ProjectPanelWidth = width, BottomPanelHeight = height },
        });
    }

    private static async Task WriteAsync(ShellBridge bridge, SettingsStore store, string parametersJson)
    {
        using JsonDocument document = JsonDocument.Parse(parametersJson);
        await bridge.WriteSettingsAsync(document.RootElement, store, CancellationToken.None);
    }
}
