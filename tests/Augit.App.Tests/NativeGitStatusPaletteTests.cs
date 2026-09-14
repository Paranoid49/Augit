using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeGitStatusPaletteTests
{
    [TestMethod]
    public void 共享主题令牌与全新视觉稿颜色一致()
    {
        NativeThemePalette light = NativeTheme.Palette(dark: false);
        NativeThemePalette dark = NativeTheme.Palette(dark: true);

        Assert.AreEqual(Rgb(233, 234, 238), light.Chrome);
        Assert.AreEqual(Rgb(255, 255, 255), light.Panel);
        Assert.AreEqual(Rgb(245, 248, 254), light.PanelMuted);
        Assert.AreEqual(Rgb(227, 227, 227), light.Border);
        Assert.AreEqual(Rgb(209, 211, 217), light.BorderStrong);
        Assert.AreEqual(Rgb(32, 33, 36), light.Text);
        Assert.AreEqual(Rgb(100, 104, 112), light.Muted);
        Assert.AreEqual(Rgb(160, 164, 170), light.Faint);
        Assert.AreEqual(Rgb(56, 113, 225), light.Accent);
        Assert.AreEqual(Rgb(208, 223, 254), light.AccentSoft);
        Assert.AreEqual(Rgb(241, 242, 244), light.Hover);
        Assert.AreEqual(Rgb(43, 45, 48), dark.Chrome);
        Assert.AreEqual(Rgb(30, 31, 34), dark.Panel);
        Assert.AreEqual(Rgb(37, 38, 42), dark.PanelMuted);
        Assert.AreEqual(Rgb(84, 138, 247), dark.Accent);
    }

    [TestMethod]
    public void 底部工具页签选中态使用视觉稿的中性描边和弱底色()
    {
        (uint lightBorder, uint lightFill, uint lightText) = NativeTheme.SelectedToolTabColors(dark: false);
        (uint darkBorder, uint darkFill, uint darkText) = NativeTheme.SelectedToolTabColors(dark: true);

        Assert.AreEqual(Rgb(209, 211, 217), lightBorder);
        Assert.AreEqual(Rgb(245, 248, 254), lightFill);
        Assert.AreEqual(Rgb(75, 77, 83), darkBorder);
        Assert.AreEqual(Rgb(37, 38, 42), darkFill);
        Assert.AreEqual(Rgb(32, 33, 36), lightText);
        Assert.AreEqual(Rgb(223, 225, 229), darkText);
    }

    [TestMethod]
    public void 文档活动标签使用视觉稿边框和填充()
    {
        (uint lightBorder, uint lightFill) = NativeTheme.SelectedDocumentTabColors(dark: false);
        (uint darkBorder, uint darkFill) = NativeTheme.SelectedDocumentTabColors(dark: true);

        Assert.AreEqual(Rgb(213, 217, 224), lightBorder);
        Assert.AreEqual(Rgb(240, 242, 245), lightFill);
        Assert.AreEqual(NativeTheme.Palette(dark: true).BorderStrong, darkBorder);
        Assert.AreEqual(NativeTheme.Palette(dark: true).PanelMuted, darkFill);
    }

    [TestMethod]
    public void 浅色主题为不同Git状态提供可区分颜色()
    {
        uint modified = NativeGitStatusPalette.Resolve(GitChangeKind.Modified, dark: false);
        uint added = NativeGitStatusPalette.Resolve(GitChangeKind.Added, dark: false);
        uint untracked = NativeGitStatusPalette.Resolve(GitChangeKind.Untracked, dark: false);
        uint renamed = NativeGitStatusPalette.Resolve(GitChangeKind.Renamed, dark: false);

        Assert.AreNotEqual(modified, added);
        Assert.AreEqual(modified, untracked);
        Assert.AreEqual(Rgb(56, 113, 225), untracked);
        Assert.AreNotEqual(modified, renamed);
        Assert.AreNotEqual(untracked, NativeGitStatusPalette.Resolve(GitChangeKind.Unmerged, dark: false));
    }

    [TestMethod]
    public void 深色主题保留相同状态语义()
    {
        Assert.AreEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Untracked, dark: true),
            NativeGitStatusPalette.Resolve(GitChangeKind.Modified, dark: true));
        Assert.AreNotEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Untracked, dark: true),
            NativeGitStatusPalette.Resolve(GitChangeKind.Unmerged, dark: true));
        Assert.AreEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Renamed, dark: true),
            NativeGitStatusPalette.Resolve(GitChangeKind.Copied, dark: true));
        Assert.AreNotEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Modified, dark: true),
            NativeGitStatusPalette.Resolve(GitChangeKind.Added, dark: true));
    }

    [TestMethod]
    public void 浅色顶栏使用参考界面的轻微冷蓝渐变()
    {
        uint start = MainWindow.ToolbarColorForTest(0);
        uint peak = MainWindow.ToolbarColorForTest(NativeTheme.Scale(200));
        uint middle = MainWindow.ToolbarColorForTest(NativeTheme.Scale(360));
        uint end = MainWindow.ToolbarColorForTest(NativeTheme.Scale(720));

        Assert.AreEqual(Rgb(233, 234, 238), start);
        Assert.AreNotEqual(start, peak);
        Assert.AreNotEqual(peak, middle);
        Assert.AreNotEqual(middle, end);
    }

    [TestMethod]
    public void 主工具窗口圆角与视觉稿一致()
    {
        Assert.AreEqual(NativeTheme.Scale(9), MainWindow.CardRadiusForTest);
    }

    [TestMethod]
    public void 主面板宽度与间距匹配参考界面的紧凑比例()
    {
        Assert.AreEqual(NativeTheme.Scale(4), MainWindow.CardGapForTest);
        Assert.AreEqual(NativeTheme.Scale(360), MainWindow.TreePanelMaximumWidthForTest);
    }

    [TestMethod]
    public void 项目树根节点名称不会被过窄栏位截断()
    {
        Assert.IsGreaterThan(0, MainWindow.CalculateRootNameWidthForTest("Augit"));
        Assert.IsLessThanOrEqualTo(
            NativeTheme.Scale(180),
            MainWindow.CalculateRootNameWidthForTest("这是一个很长的工作区名称"));
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 根路径随字体实际字宽排列而不是按字符数或面板右边界排列(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            NativeTheme.ConfigureUiTypography("Segoe UI", 13);
            int narrow = MainWindow.CalculateRootNameWidthForTest("iiii");
            int wide = MainWindow.CalculateRootNameWidthForTest("WWWW");
            Assert.IsGreaterThan(narrow * 2, wide, "同字符数但不同字宽的项目名不能分配相同宽度。");
            NativeTheme.ConfigureUiTypography("Segoe UI", 26);
            Assert.IsGreaterThan(wide, MainWindow.CalculateRootNameWidthForTest("WWWW"));
            Assert.AreEqual(NativeTheme.Scale(180), MainWindow.CalculateRootNameWidthForTest(new string('W', 80)));
        }
        finally { NativeTheme.ConfigureUiTypography(previousFamily, previousSize); }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 主框架尺寸与UxSpec一致(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);

        Assert.AreEqual(NativeTheme.Scale(44), MainWindow.ToolbarHeightForTest);
        Assert.AreEqual(NativeTheme.Scale(42), MainWindow.ActivityBarWidthForTest);
        Assert.AreEqual(NativeTheme.Scale(6), MainWindow.ActivityBarLeftForTest);
        Assert.AreEqual(NativeTheme.Scale(42), MainWindow.TabHeightForTest);
        Assert.AreEqual(NativeTheme.Scale(22), MainWindow.StatusHeightForTest);
        Assert.AreEqual(
            MainWindow.ActivityBarLeftForTest
                + MainWindow.ActivityBarWidthForTest
                + MainWindow.CardGapForTest,
            MainWindow.MainPanelLeftForTest,
            "左侧工具窗必须位于全局工具栏和 4px 表面间隙之后。");
        Assert.AreEqual(0, MainWindow.FrameAmbientFadeHeightForTest);
        Assert.AreEqual(
            MainWindow.ToolbarColorForTest(0),
            MainWindow.FrameAmbientColorForTest(NativeTheme.Scale(360), NativeTheme.Scale(400)));
    }

    [TestMethod]
    public void 底部工具窗口默认高度随视口缩放并遵守视觉稿边界()
    {
        Assert.AreEqual(
            NativeTheme.Scale(223),
            MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(654)));
        Assert.AreEqual(
            NativeTheme.Scale(305),
            MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(928)));
        Assert.AreEqual(
            NativeTheme.Scale(305),
            MainWindow.CalculateDefaultBottomPanelHeightForTest(NativeTheme.Scale(1400)));
    }

    [TestMethod]
    public void 小视口项目窗默认宽度与视觉稿一致()
    {
        Assert.AreEqual(300, MainWindow.GetDefaultTreePanelWidthForTest(1180, 1100));
        Assert.AreEqual(318, MainWindow.GetDefaultTreePanelWidthForTest(1281, 1100));
    }

    [TestMethod]
    public void 项目工具窗标题包含下拉入口()
    {
        Assert.AreEqual("项目", MainWindow.FormatProjectHeaderTextForTest());
    }

    [TestMethod]
    public void 主卡片内容为抗锯齿边缘保留内缩空间()
    {
        Assert.AreEqual(1, MainWindow.SurfaceContentInsetForTest);
    }

    [TestMethod]
    public void 文件树获得焦点使用蓝色失焦使用中性灰色()
    {
        Assert.AreEqual(
            NativeTheme.Palette(dark: false).AccentSoft,
            MainWindow.TreeSelectionColorForTest(dark: false, hasFocus: true));
        Assert.AreEqual(
            NativeTheme.Palette(dark: true).AccentSoft,
            MainWindow.TreeSelectionColorForTest(dark: true, hasFocus: true));
        Assert.AreEqual(
            NativeTheme.Palette(dark: false).SelectionInactive,
            MainWindow.TreeSelectionColorForTest(dark: false, hasFocus: false));
        Assert.AreEqual(
            NativeTheme.Palette(dark: true).SelectionInactive,
            MainWindow.TreeSelectionColorForTest(dark: true, hasFocus: false));
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}
