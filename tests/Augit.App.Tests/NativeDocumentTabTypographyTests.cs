namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeDocumentTabTypographyTests
{
    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 标题栏随真实字宽收紧且长名称不挤占当前文件和窗口动作(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        try
        {
            NativeTheme.ConfigureUiTypography("Segoe UI", 13);
            var narrow = MainWindow.MeasureTitleBarLayout(deviceContext, NativeTheme.Scale(1024), "iiiiii", "iiiiii");
            var wide = MainWindow.MeasureTitleBarLayout(deviceContext, NativeTheme.Scale(1024), "WWWWWW", "WWWWWW");
            Assert.IsGreaterThan(narrow.WorkspaceWidth, wide.WorkspaceWidth);
            Assert.IsGreaterThan(narrow.BranchWidth, wide.BranchWidth);
            Assert.AreEqual(narrow.ContextWidth, wide.ContextWidth);
            Assert.IsLessThan(NativeTheme.Scale(112), narrow.ContextWidth, "默认当前文件入口不应保留旧的固定空白。");

            NativeTheme.ConfigureUiTypography("Segoe UI", 19);
            var enlarged = MainWindow.MeasureTitleBarLayout(deviceContext, NativeTheme.Scale(1024), "iiiiii", "iiiiii");
            Assert.IsGreaterThan(narrow.ContextWidth, enlarged.ContextWidth);
            Assert.IsGreaterThan(narrow.WorkspaceWidth, enlarged.WorkspaceWidth);

            foreach (int size in new[] { 9, 13, 19, 40 })
            {
                NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
                var longNames = MainWindow.MeasureTitleBarLayout(
                    deviceContext, NativeTheme.Scale(1024), new string('W', 100), new string('W', 100));
                Assert.IsLessThanOrEqualTo(NativeTheme.Scale(180), longNames.WorkspaceWidth);
                Assert.IsLessThanOrEqualTo(NativeTheme.Scale(180), longNames.BranchWidth);
                Assert.IsGreaterThan(longNames.BranchLeft + longNames.BranchWidth, longNames.ContextLeft);
                int searchLeft = NativeTheme.Scale(1024) - NativeTheme.Scale(33) * 3 - NativeTheme.Scale(68);
                Assert.IsLessThan(searchLeft, longNames.ContextLeft + longNames.ContextWidth);
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 标签宽度取实际字宽并响应独立的界面字号(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        try
        {
            NativeTheme.ConfigureUiTypography("Segoe UI", 13);
            int narrow = MainWindow.MeasureDocumentTabWidth(deviceContext, "iiiiiiiiii.txt", 76, 420);
            int wide = MainWindow.MeasureDocumentTabWidth(deviceContext, "WWWWWWWWWW.txt", 76, 420);
            Assert.IsGreaterThan(narrow, wide);
            NativeTheme.ConfigureUiTypography("Segoe UI", 19);
            Assert.IsGreaterThan(wide, MainWindow.MeasureDocumentTabWidth(deviceContext, "WWWWWWWWWW.txt", 76, 420));

            NativeTheme.ConfigureUiTypography("Consolas", 13);
            Assert.AreEqual(
                MainWindow.MeasureDocumentTabWidth(deviceContext, "iiiiiiiiii.txt", 76, 420),
                MainWindow.MeasureDocumentTabWidth(deviceContext, "WWWWWWWWWW.txt", 76, 420));
            Assert.AreEqual(NativeTheme.Scale(76), MainWindow.MeasureDocumentTabWidth(deviceContext, "", 76, 420));
            Assert.AreEqual(NativeTheme.Scale(420), MainWindow.MeasureDocumentTabWidth(deviceContext, new string('W', 300), 76, 420));
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    [TestMethod]
    public void 临时标签使用独立斜体字体且正式标签保持常规字体()
    {
        Assert.AreNotEqual(NativeTheme.UiFont, NativeTheme.UiPreviewFont);
        Assert.AreEqual(NativeTheme.UiPreviewFont, NativeTheme.UiPreviewFont, "临时标签字体句柄应被缓存复用。");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 长比较标题给文件与两侧引用保留独立空间(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        nint dc = NativeMethods.GetDeviceContext(0);
        try
        {
            foreach (int fontSize in new[] { 13, 19, 40 })
            {
                NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", fontSize);
                foreach (int logicalWidth in new[] { 210, 362 })
                {
                    int available = NativeTheme.Scale(logicalWidth);
                    var layout = MainWindow.MeasureComparisonTabCaption(dc, available,
                        new("比较: 一个非常长的中文文件名以及后续说明.cs", "feature/非常长的来源分支名称", "release/非常长的目标分支名称"));
                    Assert.IsGreaterThan(0, layout.File);
                    Assert.IsGreaterThan(0, layout.Base);
                    Assert.IsGreaterThan(0, layout.Target);
                    Assert.IsGreaterThan(0, layout.Separator);
                    Assert.IsGreaterThan(0, layout.Arrow);
                    Assert.IsLessThanOrEqualTo(available, layout.File + layout.Separator + layout.Base + layout.Arrow + layout.Target);
                }
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
            _ = NativeMethods.ReleaseDeviceContext(0, dc);
        }
    }
}
