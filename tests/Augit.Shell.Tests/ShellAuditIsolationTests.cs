using System.Text.Json;
using Augit.Infrastructure.Settings;
using Augit.Shell.Tests.Helpers;

namespace Augit.Shell.Tests;

/// <summary>
/// 两个显式参数分别隔离设置与网页数据；同时传入才能隔离两种用户状态。
/// 测试读写均使用临时目录，默认目录只比较路径字符串。
/// </summary>
[TestClass]
public sealed class ShellAuditIsolationTests
{
    [TestMethod]
    public void 显式的设置文件与用户数据目录解析为完全绝对路径()
    {
        using TemporaryDirectory temporary = new();
        string settings = Path.GetFullPath(temporary.GetPath("audit/settings.json"));
        string data = Path.GetFullPath(temporary.GetPath("audit/webview"));

        ShellOptions options = ShellOptions.Parse(["--settings-file", settings, "--user-data-folder", data]);

        Assert.AreEqual(settings, options.SettingsFile);
        Assert.AreEqual(data, options.UserDataFolder);
        // 隔离运行不参与单实例复用，也不登记，避免覆盖用户实例的同仓登记。
        Assert.IsFalse(options.ShouldReuseWorkspaceWindow);
        Assert.IsTrue(options.HasIsolatedState);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("relative/settings.json")]
    [DataRow(@"\root-relative\settings.json")]
    [DataRow(@"C:drive-relative\settings.json")]
    public void 设置文件拒绝空与相对路径(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ShellOptions.Parse(["--settings-file", value]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("relative/webview")]
    [DataRow(@"\root-relative\webview")]
    [DataRow(@"C:drive-relative\webview")]
    public void 用户数据目录拒绝空与相对路径(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ShellOptions.Parse(["--user-data-folder", value]));
    }

    [TestMethod]
    public void 默认启动仍用默认设置与默认网页数据目录()
    {
        ShellOptions options = ShellOptions.Parse([]);

        Assert.IsNull(options.SettingsFile);
        Assert.IsNull(options.UserDataFolder);
        Assert.IsTrue(options.ShouldReuseWorkspaceWindow);
        Assert.IsFalse(options.HasIsolatedState);

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.AreEqual(
            Path.Combine(local, "Augit", "WebView2", "Shell", "Session"),
            ShellOptions.ResolveWebViewDataFolder(null));
        // 默认启动仍按原规则清理历史会话目录。
        Assert.IsTrue(ShellOptions.ShouldCleanDefaultWebViewSessions(null));
    }

    [TestMethod]
    public void 显式用户数据目录时不清默认会话目录()
    {
        using TemporaryDirectory temporary = new();
        string data = Path.GetFullPath(temporary.GetPath("audit/webview"));

        ShellOptions options = ShellOptions.Parse(["--user-data-folder", data]);

        Assert.AreEqual(data, ShellOptions.ResolveWebViewDataFolder(options.UserDataFolder));
        // 显式目录的父目录可能同时存放用户数据，不能推断临时文件归属，绝不清理。
        Assert.IsFalse(ShellOptions.ShouldCleanDefaultWebViewSessions(options.UserDataFolder));
        Assert.IsTrue(options.HasIsolatedState);
        Assert.IsFalse(options.ShouldReuseWorkspaceWindow);
    }

    [TestMethod]
    [DataRow("--settings-file")]
    [DataRow("--user-data-folder")]
    public void 隔离参数缺少取值时拒绝启动(string argument)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ShellOptions.Parse([argument]));
    }

    [TestMethod]
    public void 场景启动仍按既有规则跳过复用且只隔离参数跳过登记()
    {
        ShellOptions scene = ShellOptions.Parse(["--scene", "main-project"]);
        Assert.IsFalse(scene.ShouldReuseWorkspaceWindow);
        Assert.IsFalse(scene.HasIsolatedState);

        using TemporaryDirectory temporary = new();
        ShellOptions isolated = ShellOptions.Parse(["--settings-file", temporary.GetPath("settings.json")]);
        Assert.IsTrue(isolated.HasIsolatedState);
        Assert.IsFalse(isolated.ShouldReuseWorkspaceWindow);
    }

    [TestMethod]
    public async Task 程序按显式设置文件读取设置()
    {
        using TemporaryDirectory temporary = new();
        string settingsPath = temporary.GetPath("audit/settings.json");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            Theme = "Dark",
            LastWorkspace = temporary.FullPath,
        });

        ApplicationSettings loaded = Program.LoadSettings(settingsPath);

        Assert.AreEqual("Dark", loaded.Theme);
        Assert.AreEqual(temporary.FullPath, loaded.LastWorkspace);
    }

    [TestMethod]
    public async Task 桥接的设置读写只落在注入的隔离文件()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string file = Path.Combine(workspace, "Program.cs");
        await File.WriteAllTextAsync(file, "// 审计样本文档");
        string settingsPath = temporary.GetPath("audit/settings.json");
        string otherPath = temporary.GetPath("other/settings.json");

        SettingsStore isolated = new(settingsPath);
        await isolated.SaveAsync(new() { Theme = "Dark", LastWorkspace = workspace });
        await new SettingsStore(otherPath).SaveAsync(new() { Theme = "System", LastWorkspace = workspace });
        byte[] otherBefore = await File.ReadAllBytesAsync(otherPath);

        using ShellBridge bridge = new(workspace, null, isolated);

        // settings/read 走注入的 Store，而不是用户默认设置文件。
        JsonElement read = await InvokeAsync(bridge, "settings/read", new { });
        Assert.AreEqual("Dark", read.GetProperty("theme").GetString());

        _ = await InvokeAsync(bridge, "settings/write", new { theme = "Light", projectPanelWidth = 420, bottomPanelHeight = 280 });
        _ = await InvokeAsync(bridge, "session/write", new Dictionary<string, object?>
        {
            ["openFiles"] = new[] { file },
            ["activeFile"] = file,
        });

        // 重新构造 Store 读盘：确认真写进了注入的设置文件。
        ApplicationSettings reopened = await new SettingsStore(settingsPath).LoadAsync();
        Assert.AreEqual("Light", reopened.Theme);
        Assert.AreEqual(workspace, reopened.LastWorkspace);
        Assert.HasCount(1, reopened.OpenFiles);
        Assert.AreEqual(file, reopened.OpenFiles[0]);
        Assert.AreEqual(file, reopened.ActiveFile);
        Assert.AreEqual(420d, reopened.ToolWindows.ProjectPanelWidth);
        Assert.AreEqual(280d, reopened.ToolWindows.BottomPanelHeight);
        JsonElement reread = await InvokeAsync(bridge, "settings/read", new { });
        Assert.AreEqual("Light", reread.GetProperty("theme").GetString());
        Assert.AreEqual(420d, reread.GetProperty("projectPanelWidth").GetDouble());
        Assert.AreEqual(280d, reread.GetProperty("bottomPanelHeight").GetDouble());

        // 另一个隔离 Store 完全未被触碰。
        ApplicationSettings untouched = await new SettingsStore(otherPath).LoadAsync();
        Assert.AreEqual("System", untouched.Theme);
        Assert.AreEqual(workspace, untouched.LastWorkspace);
        Assert.HasCount(0, untouched.OpenFiles);
        Assert.IsNull(untouched.ActiveFile);
        byte[] otherAfter = await File.ReadAllBytesAsync(otherPath);
        Assert.IsTrue(otherBefore.AsSpan().SequenceEqual(otherAfter));
    }

    private static async Task<JsonElement> InvokeAsync(ShellBridge bridge, string method, object? parameters)
    {
        string request = JsonSerializer.Serialize(new { id = 1, method, @params = parameters });
        string payload = await bridge.HandleAsync(request, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement response = document.RootElement.Clone();
        Assert.IsTrue(response.TryGetProperty("result", out JsonElement result), response.GetRawText());
        return result.Clone();
    }
}
