using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitComparisonInteractionTests
{
    private static readonly bool[] TypographyModes = [true, false];
    private static readonly int[] TypographySizes = [40, 19, 13];
    private static readonly int[] TypographyIconIndexes = [0, 1, 2, 5, 6];

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public Task 比较界面字号原位改变后来源计数完整且不重查正文(int dpi, string theme) => RunAsync(async (window, view, reload) =>
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest;
        double originalSize = NativeTheme.UiFontSizeForTest;
        ApplicationSettings settings = new() { Theme = theme, FontSize = 13, TextFontSize = 13 };
        try
        {
            NativeTheme.ConfigureUiTypography(family, 13);
            view.ApplyAppearance(settings);
            view.SetBounds(0, NativeTheme.Scale(100), NativeTheme.Scale(700), NativeTheme.Scale(500));
            view.SetResult(Ready("中文引用.txt", 200));
            await WaitUntilAsync(() => !view.IsBusy);
            foreach (bool split in TypographyModes)
            {
                view.ClickModeForTest(split);
                await WaitUntilAsync(() => !view.IsBusy);
                nint body = view.TextHandlesForTest[split ? 3 : 0];
                _ = NativeMethods.SendMessage(body, 2613, 50, 0);
                _ = NativeMethods.SendMessage(body, 2160, 600, 610);
                _ = NativeMethods.SetFocus(view.ToolbarButtonForTest(2));
                nint top = NativeMethods.SendMessage(body, 2152, 0, 0);
                nint lineHeight = NativeMethods.SendMessage(body, 2279, 0, 0);
                int renders = view.RenderCountForTest;
                string text = view.BodyTextForTest;
                foreach (int size in TypographySizes)
                {
                    NativeTheme.ConfigureUiTypography(family, size);
                    view.ApplyAppearance(settings with { TextFontSize = size });
                    NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(view.FileBarHandleForTest, body, split, topPadding: 8);
                    AssertHeaderRowsFit(view.FileBarHandleForTest, split);
                    Assert.IsTrue(NativeMethods.GetWindowRectangle(view.ChangeSummaryHandleForTest, out var summary));
                    Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, summary.Bottom - summary.Top);
                    foreach (int index in TypographyIconIndexes)
                    {
                        Assert.IsTrue(NativeMethods.GetWindowRectangle(view.ToolbarButtonForTest(index), out var icon));
                        Assert.AreEqual(NativeTheme.Scale(27), icon.Bottom - icon.Top);
                        Assert.AreEqual((summary.Top + summary.Bottom) / 2, (icon.Top + icon.Bottom) / 2, 1);
                    }
                    NativeDiffToolbarAssertions.AssertGroup(view.Handle, view.ModeGroupBoundsForTest, view.ToolbarButtonForTest(4), view.ToolbarButtonForTest(3));
                    Assert.AreEqual(view.ToolbarButtonForTest(2), NativeMethods.GetFocus());
                    Assert.AreEqual(lineHeight, NativeMethods.SendMessage(body, 2279, 0, 0));
                    Assert.AreEqual(top, NativeMethods.SendMessage(body, 2152, 0, 0));
                    Assert.AreEqual((nint)600, NativeMethods.SendMessage(body, 2009, 0, 0));
                    Assert.AreEqual((nint)610, NativeMethods.SendMessage(body, 2008, 0, 0));
                    Assert.AreEqual(text, view.BodyTextForTest);
                    Assert.AreEqual(renders, view.RenderCountForTest);
                    Assert.IsEmpty(reload.Calls);
                }
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(family, originalSize);
            view.ApplyAppearance();
        }
    });

    internal static void AssertHeaderRowsFit(nint header, bool split)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(header, out var bounds));
        NativeDiffFileHeaderLayout layout = NativeDiffFileHeader.Calculate(bounds, split, NativeTheme.Scale(70));
        foreach (var text in new[] { layout.Source, layout.Target, layout.Path })
        {
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, text.Bottom - text.Top, "引用行应容纳实际字体的完整高度。");
            Assert.IsTrue(text.Top >= bounds.Top && text.Bottom <= bounds.Bottom);
        }
        Assert.IsTrue(split || layout.Source.Bottom <= layout.Target.Top);
        Assert.AreEqual(NativeTheme.Scale(12), layout.SourceIcon.Bottom - layout.SourceIcon.Top);
    }
}
