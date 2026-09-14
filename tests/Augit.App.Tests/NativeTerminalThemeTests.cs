using System.Text.Json;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeTerminalThemeTests
{
    [TestMethod]
    public void TerminalThemeUsesTheSharedLightPalette()
    {
        (string background, string foreground, string cursor, string selection, string thumb, string hover) =
            TerminalWebViewHost.TerminalColorsForTest(false);

        NativeThemePalette palette = NativeTheme.Palette(false);
        Assert.AreEqual(ToCss(palette.Panel), background);
        Assert.AreEqual(ToCss(palette.Text), foreground);
        Assert.AreEqual(foreground, cursor);
        Assert.AreEqual(ToCss(palette.AccentSoft), selection);
        Assert.AreEqual(ToCss(palette.Faint), thumb);
        Assert.AreEqual(ToCss(palette.Muted), hover);
    }

    [TestMethod]
    public void TerminalThemeUsesTheSharedDarkPalette()
    {
        (string background, string foreground, string cursor, string selection, string thumb, string hover) =
            TerminalWebViewHost.TerminalColorsForTest(true);

        NativeThemePalette palette = NativeTheme.Palette(true);
        Assert.AreEqual(ToCss(palette.Panel), background);
        Assert.AreEqual(ToCss(palette.Text), foreground);
        Assert.AreEqual(foreground, cursor);
        Assert.AreEqual(ToCss(palette.AccentSoft), selection);
        Assert.AreEqual(ToCss(palette.Faint), thumb);
        Assert.AreEqual(ToCss(palette.Muted), hover);
    }

    [TestMethod]
    public void TerminalConfigurationUsesSharedMonospaceLineHeight()
    {
        using JsonDocument configuration = JsonDocument.Parse(
            TerminalWebViewHost.CreateConfigurationMessage(new()));

        Assert.AreEqual(13, configuration.RootElement.GetProperty("fontSize").GetDouble());
        Assert.AreEqual(1.7, configuration.RootElement.GetProperty("lineHeight").GetDouble(), 0.0001);
    }

    private static string ToCss(uint color) =>
        $"#{color & 0xFF:X2}{(color >> 8) & 0xFF:X2}{(color >> 16) & 0xFF:X2}";
}
