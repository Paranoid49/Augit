namespace Augit.Shell.Tests;

[TestClass]
public sealed class IndependentSettingsTests
{
    [TestMethod]
    [DataRow(true, "settings-surface.html")]
    [DataRow(false, "index.html")]
    public void 独立设置选择精简入口且主窗口入口不变(bool settingsSurface, string expected)
        => Assert.AreEqual(expected, ShellWindow.EntryDocument(settingsSurface));

    [TestMethod]
    [DataRow("Dark", false, "Dark")]
    [DataRow(" light ", true, "Light")]
    [DataRow("System", true, "Dark")]
    [DataRow("System", false, "Light")]
    public void 设置表面使用当前模式而不是启动主题(string mode, bool systemIsDark, string expected)
        => Assert.AreEqual(expected, ShellTheme.ResolveMode(mode, systemIsDark));

    [TestMethod]
    [DataRow("settings/read")]
    [DataRow("settings/write")]
    [DataRow("settings/fonts")]
    [DataRow("git/detect")]
    public void SettingsSurfaceAllowsOnlyRequiredServices(string method)
        => Assert.IsTrue(ShellWindow.IsSettingsPageMethod(method));

    [TestMethod]
    [DataRow("workspace/info")]
    [DataRow("workspace/changes")]
    [DataRow("terminal/start")]
    [DataRow("git/reset")]
    [DataRow("session/write")]
    [DataRow("window/modal-cover")]
    [DataRow("window/modal-refresh")]
    public void SettingsSurfaceRejectsWorkspaceAndSnapshotServices(string method)
        => Assert.IsFalse(ShellWindow.IsSettingsPageMethod(method));
}
