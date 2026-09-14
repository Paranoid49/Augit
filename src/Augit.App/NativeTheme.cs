using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Augit.App;

internal static partial class NativeTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int RoundedWindowCorner = 2;
    private const int DefaultDpi = 96;
    private const int UiFontLogicalSize = 13;
    private const string DefaultUiFontFamily = NativeFontResolver.DefaultInterfaceFamily;
    private static int _activeDpi = DefaultDpi;
    // 视觉审计在独立进程内模拟 DPI，避免测试修改用户的 Windows 缩放设置。
    private static int _visualAuditDpiOverride;
    private static readonly object FontGate = new();
    private static nint _uiFont;
    private static nint _uiPreviewFont;
    private static nint _uiMediumFont;
    private static nint _uiHeadingFont;
    private static nint _uiSmallFont;
    private static nint _brandFont;
    private static bool _brandFontOwned;
    private static nint _smallBrandFont;
    private static bool _smallBrandFontOwned;
    private static int _uiLineHeight;
    private static bool _uiFontOwned;
    private static bool _uiPreviewFontOwned;
    private static bool _uiMediumFontOwned;
    private static bool _uiHeadingFontOwned;
    private static bool _uiSmallFontOwned;
    private static string _uiFontFamily = DefaultUiFontFamily;
    private static double _uiFontSize = UiFontLogicalSize;

    internal static nint UiFont => GetFont(ref _uiFont, ref _uiFontOwned, CreateUiFont);

    internal static nint UiPreviewFont => GetFont(
        ref _uiPreviewFont,
        ref _uiPreviewFontOwned,
        CreateUiPreviewFont);

    internal static nint UiMediumFont => GetFont(ref _uiMediumFont, ref _uiMediumFontOwned, CreateUiMediumFont);

    internal static nint UiHeadingFont => GetFont(ref _uiHeadingFont, ref _uiHeadingFontOwned, CreateUiHeadingFont);

    internal static nint UiSmallFont => GetFont(ref _uiSmallFont, ref _uiSmallFontOwned, CreateUiSmallFont);

    internal static nint BrandFont => GetFont(ref _brandFont, ref _brandFontOwned, CreateBrandFont);
    internal static nint SmallBrandFont => GetFont(ref _smallBrandFont, ref _smallBrandFontOwned, CreateSmallBrandFont);

    // 实际字高随字体、字号和 DPI 一起缓存，布局和绘制不重复申请设备上下文。
    internal static int UiLineHeight
    {
        get
        {
            lock (FontGate)
            {
                if (_uiLineHeight > 0) return _uiLineHeight;
                nint deviceContext = NativeMethods.GetDeviceContext(0);
                if (deviceContext == 0) return Scale((int)Math.Ceiling(_uiFontSize));
                try
                {
                    foreach (nint font in new[] { UiFont, UiMediumFont, UiPreviewFont })
                    {
                        nint previous = NativeMethods.SelectObject(deviceContext, font);
                        try
                        {
                            NativeMethods.Rectangle bounds = new();
                            _ = NativeMethods.DrawText(deviceContext, "国Ag", 3, ref bounds,
                                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
                            _uiLineHeight = Math.Max(_uiLineHeight, bounds.Bottom - bounds.Top);
                        }
                        finally { _ = NativeMethods.SelectObject(deviceContext, previous); }
                    }
                }
                finally { _ = NativeMethods.ReleaseDeviceContext(0, deviceContext); }
                return _uiLineHeight;
            }
        }
    }

    internal static int ContentHeight(int defaultLogicalHeight, int verticalPadding) =>
        Math.Max(Scale(defaultLogicalHeight), UiLineHeight + Scale(verticalPadding));


    internal static int UiFontLogicalSizeForTest => UiFontLogicalSize;

    internal static string UiFontFamilyForTest => _uiFontFamily;

    internal static double UiFontSizeForTest => _uiFontSize;

    internal static (int Body, int Heading, int Secondary, int BodyWeight, int HeadingWeight)
        UiFontRolesForTest => ((int)Math.Round(_uiFontSize), (int)Math.Round(_uiFontSize), (int)Math.Round(_uiFontSize), 400, 600);

    internal static void ConfigureUiTypography(string? family, double size)
    {
        string nextFamily = NativeFontResolver.ResolveInterface(family);
        double nextSize = double.IsFinite(size) ? Math.Clamp(size, 9, 40) : UiFontLogicalSize;
        lock (FontGate)
        {
            if (string.Equals(_uiFontFamily, nextFamily, StringComparison.Ordinal)
                && Math.Abs(_uiFontSize - nextSize) < 0.001)
            {
                return;
            }

            _uiFontFamily = nextFamily;
            _uiFontSize = nextSize;
            DisposeFonts();
        }
    }

    internal static nint CreateOwnedUiFont(string? family, double size, int weight)
    {
        string fontFamily = NativeFontResolver.ResolveInterface(family);
        double logicalSize = Math.Clamp(double.IsFinite(size) ? size : UiFontLogicalSize, 9, 40);
        return NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)logicalSize)), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, fontFamily);
    }

    internal static int ActiveDpiForTest => Volatile.Read(ref _activeDpi);

    internal static bool VisualAuditDpiOverrideActiveForTest =>
        Volatile.Read(ref _visualAuditDpiOverride) > 0;

    internal static double EmbeddedContentRasterizationScaleForTest =>
        Math.Max(DefaultDpi, Volatile.Read(ref _activeDpi)) / (double)DefaultDpi;

    internal static NativeThemePalette Palette(bool dark)
    {
        return dark
            ? new(
                Chrome: Rgb(43, 45, 48),
                Panel: Rgb(30, 31, 34),
                PanelMuted: Rgb(37, 38, 42),
                Border: Rgb(57, 59, 64),
                BorderStrong: Rgb(75, 77, 83),
                Text: Rgb(223, 225, 229),
                Muted: Rgb(157, 161, 170),
                Faint: Rgb(111, 115, 123),
                Accent: Rgb(84, 138, 247),
                AccentSoft: Rgb(47, 70, 111),
                SelectionInactive: Rgb(67, 69, 74),
                HistorySelectionInactive: Rgb(67, 69, 74),
                Hover: Rgb(45, 47, 51),
                Success: Rgb(106, 171, 115),
                Danger: Rgb(227, 122, 122),
                Warning: Rgb(235, 161, 27))
            : new(
                Chrome: Rgb(233, 234, 238),
                Panel: Rgb(255, 255, 255),
                PanelMuted: Rgb(245, 248, 254),
                Border: Rgb(227, 227, 227),
                BorderStrong: Rgb(209, 211, 217),
                Text: Rgb(32, 33, 36),
                Muted: Rgb(100, 104, 112),
                Faint: Rgb(160, 164, 170),
                Accent: Rgb(56, 113, 225),
                AccentSoft: Rgb(208, 223, 254),
                SelectionInactive: Rgb(233, 234, 238),
                HistorySelectionInactive: Rgb(233, 234, 236),
                Hover: Rgb(241, 242, 244),
                Success: Rgb(56, 133, 88),
                Danger: Rgb(199, 68, 64),
                Warning: Rgb(233, 161, 27));
    }

    internal static uint SelectionColor(NativeThemePalette palette, bool hasFocus)
    {
        return hasFocus ? palette.AccentSoft : palette.SelectionInactive;
    }

    internal static int Scale(int logicalPixels)
    {
        return (int)Math.Round(logicalPixels * Math.Max(DefaultDpi, Volatile.Read(ref _activeDpi)) / (double)DefaultDpi);
    }

    internal static float Scale(float logicalPixels)
    {
        return (float)(logicalPixels * Math.Max(DefaultDpi, Volatile.Read(ref _activeDpi)) / (double)DefaultDpi);
    }

    internal static double Unscale(int physicalPixels)
    {
        return physicalPixels * DefaultDpi / (double)Math.Max(DefaultDpi, Volatile.Read(ref _activeDpi));
    }

    internal static double GetEmbeddedContentDpiAdjustment(nint window)
    {
        if (Volatile.Read(ref _visualAuditDpiOverride) <= 0)
        {
            return 1d;
        }

        uint actualDpi = window == 0 ? 0 : NativeMethods.GetDpiForWindow(window);
        if (actualDpi == 0)
        {
            actualDpi = NativeMethods.GetDpiForSystem();
        }

        return CalculateEmbeddedContentDpiAdjustmentForTest((int)actualDpi);
    }

    internal static double CalculateEmbeddedContentDpiAdjustmentForTest(int actualDpi)
    {
        int targetDpi = Math.Max(DefaultDpi, Volatile.Read(ref _activeDpi));
        return targetDpi / (double)Math.Max(DefaultDpi, actualDpi);
    }

    internal static bool UpdateDpiForWindow(nint window)
    {
        int auditDpi = Volatile.Read(ref _visualAuditDpiOverride);
        if (auditDpi > 0)
        {
            return UpdateActiveDpi(auditDpi);
        }

        uint dpi = window == 0 ? 0 : NativeMethods.GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = NativeMethods.GetDpiForSystem();
        }

        return UpdateActiveDpi((int)Math.Max(DefaultDpi, dpi));
    }

    internal static IDisposable PushVisualAuditDpiOverride(int dpi)
    {
        if (dpi is not 96 and not 120 and not 144)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dpi),
                dpi,
                "视觉审计仅支持 96、120 或 144 DPI。");
        }

        int previous = Interlocked.Exchange(ref _visualAuditDpiOverride, dpi);
        _ = UpdateActiveDpi(dpi);
        return new VisualAuditDpiOverrideScope(previous);
    }

    private static bool UpdateActiveDpi(int next)
    {
        if (Interlocked.Exchange(ref _activeDpi, next) == next)
        {
            return false;
        }

        lock (FontGate)
        {
            DisposeFonts();
        }

        return true;
    }

    private sealed class VisualAuditDpiOverrideScope(int previous) : IDisposable
    {
        private int _previous = previous;

        public void Dispose()
        {
            int restore = Interlocked.Exchange(ref _previous, int.MinValue);
            if (restore == int.MinValue)
            {
                return;
            }

            Volatile.Write(ref _visualAuditDpiOverride, restore);
            _ = UpdateDpiForWindow(0);
        }
    }

    private static nint GetFont(ref nint font, ref bool owned, Func<nint> factory)
    {
        lock (FontGate)
        {
            if (font != 0)
            {
                return font;
            }

            nint created = factory();
            owned = created != 0 && !IsStockFont(created) && !IsKnownFont(created);
            font = created;
            return created;
        }
    }

    private static bool IsKnownFont(nint font)
    {
        return font == _uiFont
            || font == _uiPreviewFont
            || font == _uiMediumFont
            || font == _uiHeadingFont
            || font == _uiSmallFont
            || font == _brandFont
            || font == _smallBrandFont;
    }

    private static bool IsStockFont(nint font)
    {
        return font == NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
    }

    private static void DisposeFonts()
    {
        _uiLineHeight = 0;
        DisposeFont(ref _brandFont, ref _brandFontOwned);
        DisposeFont(ref _smallBrandFont, ref _smallBrandFontOwned);
        DisposeFont(ref _uiFont, ref _uiFontOwned);
        DisposeFont(ref _uiPreviewFont, ref _uiPreviewFontOwned);
        DisposeFont(ref _uiMediumFont, ref _uiMediumFontOwned);
        DisposeFont(ref _uiHeadingFont, ref _uiHeadingFontOwned);
        DisposeFont(ref _uiSmallFont, ref _uiSmallFontOwned);
    }

    private static void DisposeFont(ref nint font, ref bool owned)
    {
        if (owned && font != 0)
        {
            _ = NativeMethods.DeleteObject(font);
        }

        font = 0;
        owned = false;
    }

    internal static bool IsDark(string theme)
    {
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static (uint Border, uint Fill, uint Text) SelectedToolTabColors(bool dark)
    {
        NativeThemePalette palette = Palette(dark);
        return (palette.BorderStrong, palette.PanelMuted, palette.Text);
    }

    internal static (uint Border, uint Fill) SelectedDocumentTabColors(bool dark)
    {
        NativeThemePalette palette = Palette(dark);
        return dark
            ? (palette.BorderStrong, palette.PanelMuted)
            : (Rgb(213, 217, 224), Rgb(240, 242, 245));
    }

    internal static void ApplyToWindow(nint window, bool dark)
    {
        if (window == 0)
        {
            return;
        }

        int enabled = dark ? 1 : 0;
        _ = NativeMethods.SetDwmWindowAttribute(window, UseImmersiveDarkMode, ref enabled, sizeof(int));
        int roundedCorner = RoundedWindowCorner;
        _ = NativeMethods.SetDwmWindowAttribute(window, WindowCornerPreference, ref roundedCorner, sizeof(int));
    }

    internal static void ApplyToControl(nint control, bool dark)
    {
        if (control != 0)
        {
            _ = NativeMethods.SetWindowTheme(control, dark ? "DarkMode_Explorer" : "Explorer", null);
            _ = NativeMethods.SendMessage(
                control,
                NativeMethods.WindowMessageSetFont,
                unchecked((nuint)UiFont),
                1);
        }
    }

    internal static bool DrawFlatButton(
        nint parameter,
        bool dark,
        bool emphasized = false,
        bool selected = false,
        bool outlined = false,
        bool danger = false)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        return DrawFlatButton(
            item.DeviceContext,
            item.ItemRectangle,
            NativeMethods.GetWindowTextValue(item.Control),
            dark,
            emphasized,
            selected,
            outlined,
            danger,
            disabled: (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0,
            pressed: (item.ItemState & NativeMethods.OwnerDrawSelected) != 0,
            hot: (item.ItemState & NativeMethods.OwnerDrawHotLight) != 0);
    }

    internal static bool DrawFlatButton(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        string label,
        bool dark,
        bool emphasized = false,
        bool selected = false,
        bool outlined = false,
        bool danger = false,
        bool disabled = false,
        bool pressed = false,
        bool hot = false)
    {
        if (deviceContext == 0)
        {
            return false;
        }

        NativeThemePalette palette = Palette(dark);
        uint background = palette.Panel;
        uint pressedBackground = palette.Hover;
        uint text = palette.Text;
        uint muted = palette.Faint;
        uint accent = palette.Accent;
        uint dangerAccent = dark ? Rgb(240, 95, 95) : Rgb(216, 77, 77);
        uint border = palette.BorderStrong;
        bool active = (emphasized || selected || danger) && !disabled;
        Fill(deviceContext, rectangle, background);
        if (emphasized && disabled)
        {
            FillRounded(deviceContext, rectangle, palette.PanelMuted);
        }
        else if (outlined && !active)
        {
            FillRounded(deviceContext, rectangle, border);
            NativeMethods.Rectangle inner = rectangle;
            int inset = Math.Max(1, Scale(1));
            inner.Left += inset;
            inner.Top += inset;
            inner.Right -= inset;
            inner.Bottom -= inset;
            FillRounded(deviceContext, inner, pressed || hot ? pressedBackground : background);
        }
        else if (active || pressed || hot)
        {
            FillRounded(deviceContext, rectangle, active ? danger ? dangerAccent : accent : pressedBackground);
        }
        nint previousFont = NativeMethods.SelectObject(deviceContext, UiFont);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(
            deviceContext,
            disabled
                ? muted
                : active
                    ? Rgb(255, 255, 255)
                    : text);
        uint format = NativeMethods.DrawTextCenter
            | NativeMethods.DrawTextVerticalCenter
            | NativeMethods.DrawTextSingleLine
            | NativeMethods.DrawTextNoPrefix
            | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, label, label.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }

        return true;
    }

    internal static bool DrawManagementToolbarButton(
        nint parameter,
        bool dark,
        NativeManagementToolbarIcon icon)
    {
        if (!DrawFlatButton(parameter, dark))
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        NativeThemePalette palette = Palette(dark);
        uint color = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            ? palette.Faint
            : palette.Muted;
        int centerX = (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2;
        int centerY = (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2;
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, Scale(1)), color);
        if (pen == 0)
        {
            return true;
        }

        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(item.DeviceContext, NativeMethods.GetStockObject(NativeMethods.NullBrush));
        switch (icon)
        {
            case NativeManagementToolbarIcon.Add:
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX - Scale(5), centerY, 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(5), centerY);
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX, centerY - Scale(5), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX, centerY + Scale(5));
                break;
            case NativeManagementToolbarIcon.Delete:
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX - Scale(6), centerY - Scale(5), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(6), centerY - Scale(5));
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX - Scale(2), centerY - Scale(7), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(2), centerY - Scale(7));
                _ = NativeMethods.DrawRectangle(
                    item.DeviceContext,
                    centerX - Scale(5),
                    centerY - Scale(3),
                    centerX + Scale(5),
                    centerY + Scale(7));
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX - Scale(2), centerY - Scale(1), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX - Scale(2), centerY + Scale(5));
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX + Scale(2), centerY - Scale(1), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(2), centerY + Scale(5));
                break;
            case NativeManagementToolbarIcon.Refresh:
                _ = NativeMethods.DrawEllipse(
                    item.DeviceContext,
                    centerX - Scale(6),
                    centerY - Scale(6),
                    centerX + Scale(6),
                    centerY + Scale(6));
                _ = NativeMethods.MoveTo(item.DeviceContext, centerX + Scale(2), centerY - Scale(7), 0);
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(7), centerY - Scale(7));
                _ = NativeMethods.LineTo(item.DeviceContext, centerX + Scale(7), centerY - Scale(2));
                break;
        }

        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(item.DeviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
        return true;
    }

    private static nint CreateUiFont()
    {
        nint font = NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)_uiFontSize)),
            0,
            0,
            0,
            400,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            _uiFontFamily);
        return font != 0 ? font : NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
    }

    private static nint CreateBrandFont()
    {
        nint font = CreateOwnedUiFont(DefaultUiFontFamily, UiFontLogicalSize, 400);
        return font != 0 ? font : NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
    }

    private static nint CreateSmallBrandFont()
    {
        nint font = CreateOwnedUiFont(DefaultUiFontFamily, 9, 400);
        return font != 0 ? font : NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
    }

    private static nint CreateUiPreviewFont()
    {
        nint font = NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)_uiFontSize)),
            0,
            0,
            0,
            400,
            1,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            _uiFontFamily);
        return font != 0 ? font : UiFont;
    }

    private static nint CreateUiMediumFont()
    {
        nint font = NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)_uiFontSize)),
            0,
            0,
            0,
            600,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            _uiFontFamily);
        return font != 0 ? font : UiFont;
    }

    private static nint CreateUiHeadingFont()
    {
        nint font = NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)_uiFontSize)),
            0,
            0,
            0,
            600,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            _uiFontFamily);
        return font != 0 ? font : UiMediumFont;
    }

    private static nint CreateUiSmallFont()
    {
        nint font = NativeMethods.CreateFont(
            -(int)Math.Round(Scale((float)_uiFontSize)),
            0,
            0,
            0,
            400,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            _uiFontFamily);
        return font != 0 ? font : UiFont;
    }

    internal static bool DrawNavigationIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeNavigationIcon icon,
        uint color)
    {
        float centerX = (rectangle.Left + rectangle.Right) / 2f;
        float centerY = (rectangle.Top + rectangle.Bottom) / 2f;
        float unit = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines;
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses = [];
        if (icon == NativeNavigationIcon.Search)
        {
            lines = [new(centerX + 3 * unit, centerY + 3 * unit, centerX + 6 * unit, centerY + 6 * unit)];
            ellipses = [new(centerX - 6 * unit, centerY - 6 * unit, 10 * unit, 10 * unit)];
        }
        else if (icon is NativeNavigationIcon.Left or NativeNavigationIcon.Right)
        {
            float direction = icon == NativeNavigationIcon.Left ? -1 : 1;
            lines =
            [
                new(centerX - direction * 2 * unit, centerY - 4 * unit, centerX + direction * 2 * unit, centerY),
                new(centerX + direction * 2 * unit, centerY, centerX - direction * 2 * unit, centerY + 4 * unit),
            ];
        }
        else
        {
            float direction = icon == NativeNavigationIcon.Up ? -1 : 1;
            float tipY = centerY + direction * 6 * unit;
            lines =
            [
                new(centerX, centerY - direction * 6 * unit, centerX, tipY),
                new(centerX - 4 * unit, centerY + direction * 2 * unit, centerX, tipY),
                new(centerX + 4 * unit, centerY + direction * 2 * unit, centerX, tipY),
            ];
        }

        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f), lines, ellipses, []);
    }

    internal static uint GitGraphColor(int index, bool dark)
    {
        return (index % 4, dark) switch
        {
            (0, false) => Rgb(71, 161, 179),
            (0, true) => Rgb(91, 181, 202),
            (1, false) => Rgb(151, 103, 177),
            (1, true) => Rgb(184, 138, 209),
            (2, false) => Rgb(96, 147, 73),
            (2, true) => Rgb(135, 183, 114),
            (_, false) => Rgb(194, 102, 90),
            _ => Rgb(222, 143, 129),
        };
    }

    internal static void DrawCommitGraphNode(nint dc, int x, int y, uint color, uint background, bool head)
    {
        void Circle(int radius, uint fill)
        {
            int size = Scale(radius);
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(dc,
                new() { Left = x - size, Top = y - size, Right = x + size, Bottom = y + size }, fill, size * 2);
        }
        if (head)
        {
            Circle(6, background);
            float radius = Scale(5.5f);
            _ = NativeGdiPlusDrawing.StrokeShapes(dc, color, Scale(2f), [], [new(x - radius, y - radius, radius * 2, radius * 2)], []);
            Circle(2, color);
        }
        else Circle(4, color);
    }

    internal static bool DrawChevronIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        bool expanded,
        uint color)
    {
        float x = (rectangle.Left + rectangle.Right) / 2f;
        float y = (rectangle.Top + rectangle.Bottom) / 2f;
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines = expanded
            ? [new(x - 4 * u, y - 2 * u, x, y + 2 * u), new(x, y + 2 * u, x + 4 * u, y - 2 * u)]
            : [new(x - 2 * u, y - 4 * u, x + 2 * u, y), new(x + 2 * u, y, x - 2 * u, y + 4 * u)];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f), lines, [], []);
    }

    internal static bool DrawSettingsIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color)
    {
        float centerX = (rectangle.Left + rectangle.Right) / 2f;
        float centerY = (rectangle.Top + rectangle.Bottom) / 2f;
        float innerRadius = Scale(2.3f);
        List<NativeGdiPlusDrawing.StrokeLine> lines = [];
        // 六个齿组成连续外轮廓，齿根与齿顶之间不再留出辐条式断口。
        ReadOnlySpan<(float Angle, float Radius)> tooth = [(-30, 5.4f), (-18, 5.4f), (-12, 7), (12, 7), (18, 5.4f), (30, 5.4f)];
        for (int index = 0; index < 36; index++)
        {
            (float angle, float radius) = tooth[index % tooth.Length];
            int next = (index + 1) % 36;
            (float nextAngle, float nextRadius) = tooth[next % tooth.Length];
            double start = (angle + index / tooth.Length * 60 - 90) * Math.PI / 180;
            double end = (nextAngle + next / tooth.Length * 60 - 90) * Math.PI / 180;
            lines.Add(new(
                centerX + (float)Math.Cos(start) * Scale(radius),
                centerY + (float)Math.Sin(start) * Scale(radius),
                centerX + (float)Math.Cos(end) * Scale(nextRadius),
                centerY + (float)Math.Sin(end) * Scale(nextRadius)));
        }

        NativeGdiPlusDrawing.StrokeEllipse[] ellipses =
        [
            new(centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2),
        ];
        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, Scale(1.5f)),
            CollectionsMarshal.AsSpan(lines),
            ellipses,
            []);
    }

    internal static bool DrawToolWindowIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        NativeToolWindowIcon icon,
        uint color)
    {
        float x = (rectangle.Left + rectangle.Right) / 2f;
        float y = (rectangle.Top + rectangle.Bottom) / 2f;
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeLine[] lines = [];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses = [];
        NativeGdiPlusDrawing.StrokeRectangle[] rectangles = [];
        NativeGdiPlusDrawing.StrokeArc[] arcs = [];
        switch (icon)
        {
            case NativeToolWindowIcon.Project:
                lines =
                [
                    new(x - 7 * u, y - 6 * u, x - 2 * u, y - 6 * u),
                    new(x - 2 * u, y - 6 * u, x, y - 3 * u),
                    new(x, y - 3 * u, x + 7 * u, y - 3 * u),
                    new(x + 7 * u, y - 3 * u, x + 7 * u, y + 6 * u),
                    new(x + 7 * u, y + 6 * u, x - 7 * u, y + 6 * u),
                    new(x - 7 * u, y + 6 * u, x - 7 * u, y - 6 * u),
                ];
                break;
            case NativeToolWindowIcon.Commit:
                lines = [new(x - 7 * u, y, x - 2.5f * u, y), new(x + 2.5f * u, y, x + 7 * u, y)];
                ellipses = [new(x - 2.5f * u, y - 2.5f * u, 5 * u, 5 * u)];
                break;
            case NativeToolWindowIcon.Terminal:
                rectangles = [new(x - 7 * u, y - 6 * u, 14 * u, 12 * u)];
                lines =
                [
                    new(x - 4 * u, y - 3 * u, x - u, y),
                    new(x - u, y, x - 4 * u, y + 3 * u),
                    new(x + u, y + 3 * u, x + 4 * u, y + 3 * u),
                ];
                break;
            case NativeToolWindowIcon.GitHistory:
                ellipses = [new(x - 6 * u, y - 7 * u, 4 * u, 4 * u), new(x + 2 * u, y - 5 * u, 4 * u, 4 * u)];
                lines = [new(x - 4 * u, y - 3 * u, x - 4 * u, y + 7 * u), new(x + 4 * u, y - u, x + 4 * u, y), new(x, y + 4 * u, x - 4 * u, y + 4 * u)];
                arcs = [new(x - 4 * u, y - 4 * u, 8 * u, 8 * u, 0, 90)];
                break;
        }
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f), lines, ellipses, rectangles, arcs);
    }

    internal static bool DrawMoreIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        NativeGdiPlusDrawing.StrokeEllipse[] dots =
        [
            new(centerX - 0.2f * u, centerY - 4.2f * u, 0.4f * u, 0.4f * u),
            new(centerX - 0.2f * u, centerY - 0.2f * u, 0.4f * u, 0.4f * u),
            new(centerX - 0.2f * u, centerY + 3.8f * u, 0.4f * u, 0.4f * u),
        ];
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.2f), [], dots, []);
    }

    internal static bool DrawHideIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - Scale(5f), centerY, centerX + Scale(5f), centerY)], [], []);
    }

    internal static bool DrawTabCloseIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float radius = Scale(3.5f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.2f),
            [new(centerX - radius, centerY - radius, centerX + radius, centerY + radius),
             new(centerX + radius, centerY - radius, centerX - radius, centerY + radius)], [], []);
    }

    internal static bool DrawHistoryIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        float radius = Scale(6f);
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(centerX, centerY, centerX, centerY - Scale(3.5f)),
            new(centerX, centerY, centerX + Scale(3.5f), centerY + Scale(2f)),
            new(centerX - Scale(7f), centerY, centerX - Scale(4f), centerY),
            new(centerX - Scale(7f), centerY, centerX - Scale(5f), centerY - Scale(2f)),
        ];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses =
        [new(centerX - radius, centerY - radius, radius * 2, radius * 2)];
        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, Scale(1.5f)),
            lines,
            ellipses,
            []);
    }

    internal static bool DrawRemoteIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color)
    {
        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        float node = Scale(2.2f);
        NativeGdiPlusDrawing.StrokeLine[] lines =
        [
            new(centerX - Scale(5f), centerY, centerX, centerY - Scale(4f)),
            new(centerX, centerY - Scale(4f), centerX + Scale(5f), centerY),
            new(centerX - Scale(5f), centerY, centerX, centerY + Scale(4f)),
            new(centerX, centerY + Scale(4f), centerX + Scale(5f), centerY),
        ];
        NativeGdiPlusDrawing.StrokeEllipse[] ellipses =
        [
            new(centerX - Scale(5f) - node, centerY - node, node * 2, node * 2),
            new(centerX - node, centerY - Scale(4f) - node, node * 2, node * 2),
            new(centerX + Scale(5f) - node, centerY - node, node * 2, node * 2),
            new(centerX - node, centerY + Scale(4f) - node, node * 2, node * 2),
        ];
        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, Scale(1.5f)),
            lines,
            ellipses,
            []);
    }

    internal static uint FileTypeIconColor(string? fileName, bool dark)
    {
        // 文件类型色不随 Git 状态或选中行变化，状态由文件名和既有状态说明表达。
        return FileTypeIconKind(fileName) switch
        {
            "markdown" or "image" => dark ? Rgb(106, 159, 255) : Rgb(53, 116, 240),
            "csharp" or "markup" => dark ? Rgb(106, 171, 115) : Rgb(54, 154, 92),
            "structured" => dark ? Rgb(235, 192, 81) : Rgb(184, 142, 43),
            _ => Palette(dark).Muted,
        };
    }

    internal static bool DrawFileTypeIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        string? fileName,
        bool dark)
    {
        return DrawFileTypeIcon(deviceContext, rectangle, fileName, FileTypeIconColor(fileName, dark));
    }

    internal static bool DrawFileTypeIcon(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        string? fileName,
        uint color)
    {
        if (deviceContext == 0)
        {
            return false;
        }

        int centerX = (rectangle.Left + rectangle.Right) / 2;
        int centerY = (rectangle.Top + rectangle.Bottom) / 2;
        float left = centerX - Scale(6f);
        float top = centerY - Scale(7f);
        float right = centerX + Scale(6f);
        float bottom = centerY + Scale(7f);
        float fold = Scale(3.5f);
        List<NativeGdiPlusDrawing.StrokeLine> lines =
        [
            new(left, top, right - fold, top),
            new(right - fold, top, right, top + fold),
            new(right, top + fold, right, bottom),
            new(right, bottom, left, bottom),
            new(left, bottom, left, top),
            new(right - fold, top, right - fold, top + fold),
            new(right - fold, top + fold, right, top + fold),
        ];
        List<NativeGdiPlusDrawing.StrokeEllipse> ellipses = [];
        List<NativeGdiPlusDrawing.StrokeRectangle> rectangles = [];
        List<NativeGdiPlusDrawing.StrokeArc> arcs = [];

        string iconKind = FileTypeIconKind(fileName);
        if (iconKind == "markdown")
        {
            // 参考图中的 M 是实心短字形；不能用高而细的折线或界面字体代替。
            float u = Scale(1f);
            float x = centerX - 8 * u;
            float y = centerY - 8 * u;
            if (!NativeGdiPlusDrawing.FillPolygon(deviceContext, color,
                [new(x + u, y + 4 * u), new(x + 3 * u, y + 4 * u),
                 new(x + 5 * u, y + 8 * u), new(x + 7 * u, y + 4 * u),
                 new(x + 9 * u, y + 4 * u), new(x + 9 * u, y + 12 * u),
                 new(x + 7 * u, y + 12 * u), new(x + 7 * u, y + 7.5f * u),
                 new(x + 5 * u, y + 11 * u), new(x + 3 * u, y + 7.5f * u),
                 new(x + 3 * u, y + 12 * u), new(x + u, y + 12 * u)]))
            {
                return false;
            }
            lines.Clear();
            lines.Add(new(x + 13 * u, y + 4 * u, x + 13 * u, y + 12 * u));
            lines.Add(new(x + 11 * u, y + 10 * u, x + 13 * u, y + 12 * u));
            lines.Add(new(x + 15 * u, y + 10 * u, x + 13 * u, y + 12 * u));
        }
        else if (iconKind == "csharp")
        {
            // C# 使用圆角 C 和紧凑井号，不使用字体字符，避免 DPI 下基线漂移。
            lines.Clear();
            arcs.Add(new(centerX - Scale(6.5f), centerY - Scale(4f), Scale(7f), Scale(8f), 45, 270));
            lines.Add(new(centerX + Scale(3.5f), centerY - Scale(3.5f), centerX + Scale(2.5f), centerY + Scale(3.5f)));
            lines.Add(new(centerX + Scale(6.5f), centerY - Scale(3.5f), centerX + Scale(5.5f), centerY + Scale(3.5f)));
            lines.Add(new(centerX + Scale(1.5f), centerY - Scale(1.5f), centerX + Scale(7f), centerY - Scale(1.5f)));
            lines.Add(new(centerX + Scale(1f), centerY + Scale(1.5f), centerX + Scale(6.5f), centerY + Scale(1.5f)));
        }
        else if (iconKind == "markup")
        {
            // 标记文件使用成对尖括号。
            lines.Clear();
            lines.Add(new(left + Scale(6f), centerY - Scale(4f), left + Scale(3f), centerY));
            lines.Add(new(left + Scale(3f), centerY, left + Scale(6f), centerY + Scale(4f)));
            lines.Add(new(left + Scale(8f), centerY - Scale(4f), left + Scale(11f), centerY));
            lines.Add(new(left + Scale(11f), centerY, left + Scale(8f), centerY + Scale(4f)));
        }
        else if (iconKind == "structured")
        {
            // 结构化文件使用轻量花括号轮廓。
            lines.Clear();
            lines.Add(new(left + Scale(5f), centerY - Scale(4f), left + Scale(3f), centerY - Scale(2f)));
            lines.Add(new(left + Scale(3f), centerY - Scale(2f), left + Scale(3f), centerY + Scale(2f)));
            lines.Add(new(left + Scale(3f), centerY + Scale(2f), left + Scale(5f), centerY + Scale(4f)));
            lines.Add(new(left + Scale(9f), centerY - Scale(4f), left + Scale(11f), centerY - Scale(2f)));
            lines.Add(new(left + Scale(11f), centerY - Scale(2f), left + Scale(11f), centerY + Scale(2f)));
            lines.Add(new(left + Scale(11f), centerY + Scale(2f), left + Scale(9f), centerY + Scale(4f)));
        }
        else if (iconKind == "text")
        {
            lines.Clear();
            lines.Add(new(centerX - Scale(6f), centerY - Scale(4f), centerX + Scale(5f), centerY - Scale(4f)));
            lines.Add(new(centerX - Scale(6f), centerY, centerX + Scale(5f), centerY));
            lines.Add(new(centerX - Scale(6f), centerY + Scale(4f), centerX + Scale(5f), centerY + Scale(4f)));
        }
        else if (iconKind == "ignored")
        {
            lines.Clear();
            ellipses.Add(new(centerX - Scale(6f), centerY - Scale(6f), Scale(12f), Scale(12f)));
            lines.Add(new(centerX - Scale(4f), centerY + Scale(4f), centerX + Scale(4f), centerY - Scale(4f)));
        }
        else if (iconKind == "image")
        {
            // 图片保留独立矩形外框，山峰和太阳限定在框内。
            lines.Clear();
            rectangles.Add(new(left, centerY - Scale(6f), Scale(12f), Scale(12f)));
            ellipses.Add(new(left + Scale(7.5f), centerY - Scale(3.5f), Scale(2f), Scale(2f)));
            lines.Add(new(left, centerY + Scale(3f), left + Scale(4f), centerY - Scale(1f)));
            lines.Add(new(left + Scale(4f), centerY - Scale(1f), right, centerY + Scale(6f)));
        }
        else
        {
            // 普通文件使用水平文字线，不能复用图片的山形内容。
            lines.Add(new(left + Scale(2f), centerY - Scale(2f), right - Scale(2f), centerY - Scale(2f)));
            lines.Add(new(left + Scale(2f), centerY + Scale(1f), right - Scale(2f), centerY + Scale(1f)));
            lines.Add(new(left + Scale(2f), centerY + Scale(4f), right - Scale(4f), centerY + Scale(4f)));
        }

        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext,
            color,
            Math.Max(1f, Scale(1.5f)),
            CollectionsMarshal.AsSpan(lines),
            CollectionsMarshal.AsSpan(ellipses),
            CollectionsMarshal.AsSpan(rectangles),
            CollectionsMarshal.AsSpan(arcs));
    }

    internal static string FileTypeIconKindForTest(string? fileName)
    {
        return FileTypeIconKind(fileName);
    }

    internal static bool FileTypeIconUsesPageFrameForTest(string? fileName)
    {
        return FileTypeIconKind(fileName) == "file";
    }

    private static string FileTypeIconKind(string? fileName)
    {
        string name = Path.GetFileName(fileName ?? string.Empty);
        if (name.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) return "ignored";
        string extension = Path.GetExtension(fileName ?? string.Empty);
        if (name.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase))
        {
            return "text";
        }
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return "markdown";
        }

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return "csharp";
        }

        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "markup";
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return "structured";
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            return "image";
        }

        return "file";
    }

    internal static bool DrawRefreshIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        // 对齐参考图的两段圆弧：左下、右上各有一个箭头，圆弧之间保留断口。
        // 使用浮点圆弧保持各 DPI 下曲率一致，不能用折线绕过曲线绘制。
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - 7 * u, centerY, centerX - 5.168f * u, centerY + 1.881f * u),
             new(centerX - 5.168f * u, centerY + 1.881f * u, centerX - 3.3f * u, centerY),
             new(centerX + 7 * u, centerY, centerX + 5.168f * u, centerY - 1.881f * u),
             new(centerX + 5.168f * u, centerY - 1.881f * u, centerX + 3.3f * u, centerY)], [], [],
            [new(centerX - 5.5f * u, centerY - 5.5f * u, 11 * u, 11 * u, -60, -140),
             new(centerX - 5.5f * u, centerY - 5.5f * u, 11 * u, 11 * u, 120, -140)]);
    }

    internal static bool DrawCompareIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        // 参考图为错开的双向箭头：上半向左，下半向右，两个箭头不相连。
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - u, centerY - 4 * u, centerX + 7 * u, centerY - 4 * u),
             new(centerX + 2 * u, centerY - 7 * u, centerX - u, centerY - 4 * u),
             new(centerX - u, centerY - 4 * u, centerX + 2 * u, centerY - u),
             new(centerX - 7 * u, centerY + 4 * u, centerX + u, centerY + 4 * u),
             new(centerX - 2 * u, centerY + u, centerX + u, centerY + 4 * u),
             new(centerX + u, centerY + 4 * u, centerX - 2 * u, centerY + 7 * u)], [], []);
    }

    internal static bool DrawRollbackIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - 6 * u, centerY - 3 * u, centerX + 2 * u, centerY - 3 * u),
             new(centerX - 3 * u, centerY - 6 * u, centerX - 6 * u, centerY - 3 * u),
             new(centerX - 6 * u, centerY - 3 * u, centerX - 3 * u, centerY),
             new(centerX + 2 * u, centerY + 5 * u, centerX - 3 * u, centerY + 5 * u)], [], [],
            [new(centerX - 2 * u, centerY - 3 * u, 8 * u, 8 * u, -90, 180)]);
    }

    internal static bool DrawTrayArrowIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - 7 * u, centerY, centerX - 4 * u, centerY),
             new(centerX - 4 * u, centerY, centerX - 3 * u, centerY + 3 * u),
             new(centerX - 3 * u, centerY + 3 * u, centerX + 3 * u, centerY + 3 * u),
             new(centerX + 3 * u, centerY + 3 * u, centerX + 4 * u, centerY),
             new(centerX + 4 * u, centerY, centerX + 7 * u, centerY),
             new(centerX + 7 * u, centerY, centerX + 7 * u, centerY + 6 * u),
             new(centerX + 7 * u, centerY + 6 * u, centerX - 7 * u, centerY + 6 * u),
             new(centerX - 7 * u, centerY + 6 * u, centerX - 7 * u, centerY),
             new(centerX, centerY - 7 * u, centerX, centerY),
             new(centerX - 3 * u, centerY - 3 * u, centerX, centerY),
             new(centerX, centerY, centerX + 3 * u, centerY - 3 * u)], [], []);
    }

    internal static bool DrawPreviewIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        // 上下眼睑采用连续曲线，外形为杏仁形；瞳孔单独描边，不使用菱形或椭圆代替。
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f), [],
            [new(centerX - 2.5f * u, centerY - 2.5f * u, 5 * u, 5 * u)], [], [],
            [new(centerX - 7 * u, centerY, centerX - 3.5f * u, centerY - 6.667f * u,
                 centerX + 3.5f * u, centerY - 6.667f * u, centerX + 7 * u, centerY),
             new(centerX + 7 * u, centerY, centerX + 3.5f * u, centerY + 6.667f * u,
                 centerX - 3.5f * u, centerY + 6.667f * u, centerX - 7 * u, centerY)]);
    }

    internal static bool DrawLocateIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - 7 * u, centerY, centerX - 3 * u, centerY),
             new(centerX + 3 * u, centerY, centerX + 7 * u, centerY),
             new(centerX, centerY - 7 * u, centerX, centerY - 3 * u),
             new(centerX, centerY + 3 * u, centerX, centerY + 7 * u)],
            [new(centerX - 6 * u, centerY - 6 * u, 12 * u, 12 * u)], []);
    }

    internal static bool DrawCollapseIcon(nint deviceContext, int centerX, int centerY, uint color)
    {
        float u = Scale(1f);
        return NativeGdiPlusDrawing.StrokeShapes(deviceContext, color, Scale(1.5f),
            [new(centerX - 3.5f * u, centerY - 6 * u, centerX, centerY - 2.5f * u),
             new(centerX, centerY - 2.5f * u, centerX + 3.5f * u, centerY - 6 * u),
             new(centerX - 3.5f * u, centerY + 6 * u, centerX, centerY + 2.5f * u),
             new(centerX, centerY + 2.5f * u, centerX + 3.5f * u, centerY + 6 * u)], [], []);
    }

    internal static bool DrawCheckbox(
        nint deviceContext,
        int left,
        int centerY,
        NativeCheckboxState state,
        bool dark,
        bool enabled = true)
    {
        if (deviceContext == 0) return false;
        NativeThemePalette palette = Palette(dark);
        int size = Scale(15);
        NativeMethods.Rectangle box = new()
        {
            Left = left,
            Top = centerY - size / 2,
            Right = left + size,
            Bottom = centerY - size / 2 + size,
        };
        bool filled = state != NativeCheckboxState.Unchecked;
        // 浅色勾选蓝取自参考窗口；空框、禁用框与标记不沿用文件的 Git 状态色。
        uint accent = dark ? palette.Accent : Rgb(53, 116, 240);
        FillRounded(deviceContext, box, filled && enabled ? accent : palette.Faint);
        if (!filled || !enabled)
        {
            int inset = Scale(1);
            NativeMethods.Rectangle inner = new()
            {
                Left = box.Left + inset,
                Top = box.Top + inset,
                Right = box.Right - inset,
                Bottom = box.Bottom - inset,
            };
            FillRounded(deviceContext, inner, enabled ? palette.Panel : palette.PanelMuted, Scale(4));
        }
        if (!filled) return true;
        float u = Scale(1f);
        float markCenterY = (box.Top + box.Bottom) / 2f;
        NativeGdiPlusDrawing.StrokeLine[] mark = state == NativeCheckboxState.Mixed
            ?
            [new(left + 4 * u, markCenterY, left + 11 * u, markCenterY)]
            :
            [new(left + 3 * u, markCenterY, left + 6 * u, markCenterY + 3 * u),
             new(left + 6 * u, markCenterY + 3 * u, left + 12 * u, markCenterY - 4 * u)];
        return NativeGdiPlusDrawing.StrokeShapes(
            deviceContext, enabled ? Rgb(255, 255, 255) : palette.Faint, Scale(1.5f), mark, [], []);
    }

    internal static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    internal static void FillRounded(nint deviceContext, NativeMethods.Rectangle rectangle, uint color, int? cornerDiameter = null)
    {
        int diameter = cornerDiameter ?? Scale(6);
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, diameter))
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(color);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right + 1,
            rectangle.Bottom + 1,
            diameter,
            diameter);
        if (brush != 0 && region != 0)
        {
            _ = NativeMethods.FillRegion(deviceContext, region, brush);
        }

        if (region != 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }

        if (brush != 0)
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}

internal enum NativeNavigationIcon
{
    Up,
    Down,
    Left,
    Right,
    Search,
}

internal enum NativeCheckboxState
{
    Unchecked,
    Checked,
    Mixed,
}

internal enum NativeToolWindowIcon
{
    Project,
    Commit,
    Terminal,
    GitHistory,
}

internal enum NativeManagementToolbarIcon
{
    Add,
    Delete,
    Refresh,
}

internal readonly record struct NativeThemePalette(
    uint Chrome,
    uint Panel,
    uint PanelMuted,
    uint Border,
    uint BorderStrong,
    uint Text,
    uint Muted,
    uint Faint,
    uint Accent,
    uint AccentSoft,
    uint SelectionInactive,
    uint HistorySelectionInactive,
    uint Hover,
    uint Success,
    uint Danger,
    uint Warning);
