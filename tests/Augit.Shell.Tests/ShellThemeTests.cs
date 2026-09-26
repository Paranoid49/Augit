using Augit.Shell;

namespace Augit.Shell.Tests;

/// <summary>
/// 主题取值规则的测试。
///
/// 背景：主题有三处取值需要区分——设置里的模式（System/Light/Dark）、解析后的生效主题、
/// 以及下发给网页层的 URL 查询值。网页层的契约是小写 `theme=dark`，而设置里的取值是
/// 首字母大写的 `Dark`。两者不统一时，用户在设置里选“深色”之后界面仍然是浅色，
/// 而且这个缺陷不会被审计发现：审计与视觉稿一律用命令行小写参数（`--theme dark`），
/// 走的是“显式主题原样返回”的分支，从未经过设置里的大写取值。
///
/// 另一条规则来自参考实现对系统深色检测失败的处理——检测不出来时**不切换**、
/// 按非深色取值，而不是假定系统处于深色。
/// </summary>
[TestClass]
public sealed class ShellThemeTests
{
    [TestMethod]
    public void 窗口表面色按主题取网页层body的底色()
    {
        // 取值就是 `web/src/mockup.css` 里 `body` 的 `--augit-chrome`
        //（`docs/design-system.md` §6.1 浅色 `#E9EAEE`／§6.2 深色 `#2B2D30`）。
        // 外壳必须在导航前把它交给 WebView2，否则深色下会先用默认白底画一帧（白闪）。
        Assert.AreEqual((0x2B, 0x2D, 0x30), ShellTheme.SurfaceColor("Dark"));
        Assert.AreEqual((0xE9, 0xEA, 0xEE), ShellTheme.SurfaceColor("Light"));
        Assert.AreEqual((0xE9, 0xEA, 0xEE), ShellTheme.SurfaceColor("system"),
            "未知/空主题按浅色处理（外壳在 Program 里已解析成具体主题，这里只是兜底）");
    }

    [TestMethod]
    public void 窗口表面色与样式表里的chrome令牌逐值一致()
    {
        // 反向核对：外壳里的常量必须等于网页层真正用的令牌，避免两边各写一份后漂移。
        string css = File.ReadAllText(FindRepositoryFile("web/src/mockup.css"));
        StringAssert.Contains(css, "--augit-chrome: #e9eaee;");
        StringAssert.Contains(css, "--augit-chrome: #2b2d30;");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件：{relativePath}");
    }

    [TestMethod]
    public void 系统使用深色时取深色()
    {
        Assert.AreEqual("Dark", ShellTheme.FromSystemDark(isDark: true));
    }

    [TestMethod]
    public void 系统未使用深色时取浅色()
    {
        Assert.AreEqual("Light", ShellTheme.FromSystemDark(isDark: false));
    }

    [TestMethod]
    public void 系统深色检测失败时按非深色处理()
    {
        // null 表示注册表不可读或取值不可用。参考实现遇到检测失败直接返回、不改主题，
        // 因此这里必须落到 Light，不能把“检测不出来”当作深色。
        Assert.AreEqual("Light", ShellTheme.FromSystemDark(isDark: null));
    }

    [TestMethod]
    public void 设置里的大写主题产出网页层认得的查询值()
    {
        // 回归：曾经直接下发生效主题，得到 theme=Dark，而网页层只认小写，深色永不生效。
        Assert.AreEqual("dark", ShellTheme.QueryValue("Dark"));
        Assert.AreEqual("light", ShellTheme.QueryValue("Light"));
    }

    [TestMethod]
    public void 查询值统一为小写并去除首尾空白()
    {
        Assert.AreEqual("dark", ShellTheme.QueryValue("  DARK "));
        Assert.AreEqual("system", ShellTheme.QueryValue("System"));
    }

    [TestMethod]
    public void 三种设置模式都能产出网页层可判别的查询值()
    {
        foreach (string mode in new[] { "System", "Light", "Dark" })
        {
            string value = ShellTheme.QueryValue(mode);
            Assert.AreEqual(value.ToLowerInvariant(), value, $"模式 {mode} 的查询值必须是小写");
            Assert.IsTrue(value is "system" or "light" or "dark", $"模式 {mode} 产出了未知查询值 {value}");
        }
    }

    [TestMethod]
    public void 区域名只认应用模式变化()
    {
        Assert.IsTrue(ShellTheme.IsImmersiveColorSet("ImmersiveColorSet"));
        // 同一条 WM_SETTINGCHANGE 也用于语言、无障碍等变化，必须过滤掉。
        Assert.IsFalse(ShellTheme.IsImmersiveColorSet("Environment"));
        Assert.IsFalse(ShellTheme.IsImmersiveColorSet(""));
        Assert.IsFalse(ShellTheme.IsImmersiveColorSet(null));
        Assert.IsFalse(ShellTheme.IsImmersiveColorSet("immersivecolorset"), "区域名比较区分大小写");
    }

    [TestMethod]
    public void 跟随系统时按新的系统取值切换主题()
    {
        // 当前浅色、系统切到深色 ⇒ 切深色。
        Assert.AreEqual("Dark", ShellTheme.NextOnSystemChange("System", systemIsDark: true, currentTheme: "Light"));
        // 当前深色、系统切回浅色 ⇒ 切浅色。
        Assert.AreEqual("Light", ShellTheme.NextOnSystemChange("System", systemIsDark: false, currentTheme: "Dark"));
    }

    [TestMethod]
    public void 显式选择浅色或深色时不跟随系统()
    {
        Assert.IsNull(ShellTheme.NextOnSystemChange("Light", systemIsDark: true, currentTheme: "Light"));
        Assert.IsNull(ShellTheme.NextOnSystemChange("Dark", systemIsDark: false, currentTheme: "Dark"));
    }

    [TestMethod]
    public void 主题没有实际变化时不推送()
    {
        Assert.IsNull(ShellTheme.NextOnSystemChange("System", systemIsDark: true, currentTheme: "Dark"));
        Assert.IsNull(ShellTheme.NextOnSystemChange("System", systemIsDark: false, currentTheme: "Light"));
    }

    [TestMethod]
    public void 系统深色检测失败时按非深色切换()
    {
        // 检测失败（null）时参考实现不切换、按非深色取值；当前若为深色则应切回浅色。
        Assert.AreEqual("Light", ShellTheme.NextOnSystemChange("System", systemIsDark: null, currentTheme: "Dark"));
        Assert.IsNull(ShellTheme.NextOnSystemChange("System", systemIsDark: null, currentTheme: "Light"));
    }

    [TestMethod]
    public void 主题模式大小写不敏感()
    {
        Assert.AreEqual("Dark", ShellTheme.NextOnSystemChange("system", systemIsDark: true, currentTheme: "Light"));
        Assert.IsNull(ShellTheme.NextOnSystemChange(" LIGHT ", systemIsDark: true, currentTheme: "Light"));
    }
}
