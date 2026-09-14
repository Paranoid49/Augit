using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeAppearanceDialogTests
{
    private static readonly string[] ExpectedAppearanceSections =
        [UiText.ThemeSection, UiText.InterfaceFont, UiText.WindowSection];

    private static readonly int[] ExpectedAppearanceSectionTops = [123, 245, 453];

    [TestMethod]
    public void 用户字体设置会更新统一界面字体令牌()
    {
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            NativeTheme.ConfigureUiTypography("Consolas", 17);
            Assert.AreEqual("Consolas", NativeTheme.UiFontFamilyForTest);
            Assert.AreEqual(17d, NativeTheme.UiFontSizeForTest);
            Assert.AreEqual((17, 17, 17, 400, 600), NativeTheme.UiFontRolesForTest);
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    public void 设置窗口尺寸和分区严格匹配视觉稿基线()
    {
        (int width, int height, int sidebarWidth, int headerHeight, int footerHeight) =
            NativeAppearanceDialog.LogicalLayoutForTest;

        Assert.AreEqual(1040, width);
        Assert.AreEqual(779, height);
        Assert.AreEqual(262, sidebarWidth);
        Assert.AreEqual(45, headerHeight);
        Assert.AreEqual(53, footerHeight);
        CollectionAssert.AreEqual(
            ExpectedAppearanceSections,
            NativeAppearanceDialog.AppearanceSectionLabelsForTest.ToArray());
        CollectionAssert.AreEqual(
            ExpectedAppearanceSectionTops,
            NativeAppearanceDialog.AppearanceSectionTopForTest.ToArray());
    }

    [TestMethod]
    public void 设置窗口在最小主窗口内保留视觉稿边距()
    {
        (int width, int height) = NativeAppearanceDialog.GetResponsiveLogicalSizeForTest(1024, 640);

        Assert.AreEqual(944, width);
        Assert.AreEqual(534, height);
        Assert.IsLessThanOrEqualTo(1024 - width, 80);
        Assert.IsLessThanOrEqualTo(640 - height, 106);
    }

    [TestMethod]
    public void 大字号设置窗口按实际字高扩展标题行输入行和底部动作()
    {
        (int header, int footer, int row, int button, int height) =
            NativeAppearanceDialog.CalculateAdaptiveMetricsForTest(40);

        Assert.IsGreaterThan(45, header);
        Assert.IsGreaterThan(53, footer);
        Assert.IsGreaterThan(30, row);
        Assert.IsGreaterThan(30, button);
        Assert.IsGreaterThan(779, height);
    }

    [TestMethod]
    public void 大字号正文控件越过固定标题栏或操作栏时隐藏()
    {
        Assert.IsTrue(NativeAppearanceDialog.IsContentFullyVisibleForTest(100, 40, 45, 700));
        Assert.IsFalse(NativeAppearanceDialog.IsContentFullyVisibleForTest(40, 40, 45, 700));
        Assert.IsFalse(NativeAppearanceDialog.IsContentFullyVisibleForTest(680, 40, 45, 700));
    }

    [TestMethod]
    public void 大字号正文滚动范围不会小于零()
    {
        Assert.AreEqual(0, NativeAppearanceDialog.CalculateContentScrollMaximumForTest(600, 540));
        Assert.AreEqual(120, NativeAppearanceDialog.CalculateContentScrollMaximumForTest(600, 720));
    }

    [TestMethod]
    public void 设置分类搜索忽略大小写且空查询显示全部分类()
    {
        Assert.IsTrue(NativeAppearanceDialog.CategoryMatchesForTest(UiText.GitSection, string.Empty));
        Assert.IsTrue(NativeAppearanceDialog.CategoryMatchesForTest(UiText.GitSection, "git"));
        Assert.IsFalse(NativeAppearanceDialog.CategoryMatchesForTest(UiText.TerminalSection, "Git"));
    }

    [TestMethod]
    public void 主题和终端选项映射保持双向稳定()
    {
        foreach (string theme in new[] { "System", "Light", "Dark" })
        {
            Assert.AreEqual(theme, NativeAppearanceDialog.ThemeIdForIndexForTest(
                NativeAppearanceDialog.ThemeIndexForTest(theme)));
        }

        foreach (string shell in new[]
        {
            TerminalShellIds.WindowsPowerShell,
            TerminalShellIds.PowerShell7,
            TerminalShellIds.CommandPrompt,
            TerminalShellIds.GitBash,
            TerminalShellIds.Wsl,
            TerminalShellIds.Custom,
        })
        {
            Assert.AreEqual(shell, NativeAppearanceDialog.ShellIdForIndexForTest(
                NativeAppearanceDialog.ShellIndexForTest(shell)));
        }
    }

    [TestMethod]
    public void 默认正文和等宽字体符合视觉规格()
    {
        ApplicationSettings settings = new();

        Assert.AreEqual("Microsoft YaHei UI", settings.TextFontFamily);
        Assert.AreEqual("Cascadia Mono", settings.MonospaceFontFamily);
        Assert.AreEqual(13, settings.FontSize);
        Assert.AreEqual(13, settings.UiFontSize);
    }

    [TestMethod]
    public void 设置下拉框使用固定行高自绘样式以适配深色主题()
    {
        uint style = NativeAppearanceDialog.ComboControlStyleForTest;

        Assert.AreEqual(
            NativeMethods.ComboBoxDropDownList
            | NativeMethods.ComboBoxOwnerDrawFixed
            | NativeMethods.ComboBoxHasStrings,
            style);
        CollectionAssert.AreEqual(
            new[] { UiText.FollowWindows, UiText.Light, UiText.Dark },
            NativeAppearanceDialog.ThemeLabelsForTest.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                UiText.WindowsPowerShell,
                UiText.PowerShell7,
                UiText.CommandPrompt,
                UiText.GitBash,
                UiText.Wsl,
                UiText.CustomTerminal,
            },
            NativeAppearanceDialog.TerminalShellLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 设置输入框不依赖系统粗边框而使用统一输入框外壳()
    {
        Assert.AreEqual(
            NativeMethods.EditAutoHorizontalScroll,
            NativeAppearanceDialog.InputControlStyleForTest);
        Assert.AreEqual(
            0u,
            NativeAppearanceDialog.InputControlStyleForTest & NativeMethods.WindowStyleBorder);
    }
}
