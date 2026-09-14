using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;

namespace Augit.App;

internal sealed partial class ScintillaControl : IDisposable
{
    private const uint SetCodePage = 2037;
    private const uint SetTechnology = 2630;
    private const uint SetFontLocale = 2760;
    private const uint SetText = 2181;
    private const uint GetText = 2182;
    private const uint GetTextLength = 2183;
    private const uint GetDirectFunction = 2184;
    private const uint GetDirectPointer = 2185;
    private const uint GetLineCount = 2154;
    private const uint TextWidth = 2276;
    private const uint SetReadOnly = 2171;
    private const uint GetReadOnly = 2140;
    private const uint GetModify = 2159;
    private const uint SetSavePoint = 2014;
    private const uint EmptyUndoBuffer = 2175;
    private const uint SetMarginType = 2240;
    private const uint SetMarginWidth = 2242;
    private const uint SetMarginSensitive = 2246;
    private const uint SetMargins = 2252;
    private const uint GetMargins = 2253;
    private const uint SetMarginLeft = 2155;
    private const uint SetMarginRight = 2157;
    private const uint GetMarginLeft = 2156;
    private const uint GetMarginRight = 2158;
    private const uint MarginSetText = 2530;
    private const uint MarginSetStyle = 2532;
    private const uint MarginTextClearAll = 2536;
    private const uint SetWrapMode = 2268;
    private const uint SetViewWhitespace = 2021;
    private const uint GoToLineMessage = 2024;
    private const uint GoToPositionMessage = 2025;
    private const uint SetSelection = 2160;
    private const uint GetCurrentPosition = 2008;
    private const uint GetAnchor = 2009;
    private const uint PositionBefore = 2417;
    private const uint ScrollCaret = 2169;
    private const uint GetFirstVisibleLineMessage = 2152;
    private const uint SetFirstVisibleLineMessage = 2613;
    private const uint SetXOffset = 2397;
    private const uint GetXOffset = 2398;
    private const uint SetHorizontalScrollBarMessage = 2130;
    private const uint SetVerticalScrollBarMessage = 2280;
    private const uint SetScrollWidth = 2274;
    private const uint SetScrollWidthTracking = 2516;
    private const uint TextHeight = 2279;
    private const uint SetExtraAscentMessage = 2525;
    private const uint SetExtraDescentMessage = 2527;
    private const uint StyleClearAll = 2050;
    private const uint StyleSetForeground = 2051;
    private const uint StyleSetBackground = 2052;
    private const uint StyleSetFont = 2056;
    private const uint StyleSetSizeFractional = 2061;
    private const uint StartStyling = 2032;
    private const uint SetStyling = 2033;
    private const uint SetCaretForeground = 2069;
    private const uint SetSelectionBackground = 2068;
    private const uint SetMarginBackground = 2250;
    private const uint SetMarginForeground = 2251;
    private const uint UsePopup = 2371;
    private const uint IndicatorSetStyle = 2080;
    private const uint IndicatorSetForeground = 2082;
    private const uint IndicatorSetAlpha = 2523;
    private const uint SetIndicatorCurrent = 2500;
    private const uint IndicatorFillRange = 2504;
    private const uint IndicatorClearRange = 2505;
    private const uint MarkerDefine = 2040;
    private const uint MarkerSetBackground = 2042;
    private const uint MarkerAdd = 2043;
    private const uint MarkerDeleteAll = 2045;
    private const uint MarkerSetAlpha = 2476;
    private const uint HideLines = 2227;
    private const uint ShowLines = 2226;
    private const uint GetLineVisible = 2228;
    private const int Utf8CodePage = 65001;
    private const int MarginNumber = 1;
    private const int MarginText = 4;
    private const int StyleDefault = 32;
    private const int StyleLineNumber = 33;
    private const int StyleBlame = 40;
    private const int StyleBlameSelected = 41;
    private const int StyleDiffLineNumber = 1;
    private const int StyleDiffRemovedMarker = 2;
    private const int StyleDiffAddedMarker = 3;
    private const int IndicatorStraightBox = 8;
    private const int MarkerBackground = 22;
    private const int NotificationMarginClick = 2010;
    private const int NotificationUpdateUi = 2007;
    private const int NotificationModified = 2008;
    private static readonly object LibraryGate = new();
    private static nint _library;
    private bool _disposed;
    private bool _showLineNumbers = true;
    private bool _blameVisible;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ScintillaDirectFunction? _directFunction;
    private nint _directPointer;
    private int _contentVersion;
    private readonly Dictionary<int, BlameRow> _blameRows = [];
    private int _selectedBlameLine = -1;

    internal ScintillaControl(nint parent, int controlIdentifier)
    {
        EnsureLibraryLoaded();
        Handle = NativeMethods.CreateWindow(
            0,
            "Scintilla",
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.WindowStyleClipSiblings,
            0,
            0,
            0,
            0,
            parent,
            controlIdentifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ReadOnlyTextControlCreateFailed);
        }

        nint directFunction = NativeMethods.SendMessage(Handle, GetDirectFunction, 0, 0);
        _directPointer = NativeMethods.SendMessage(Handle, GetDirectPointer, 0, 0);
        if (directFunction != 0 && _directPointer != 0)
        {
            _directFunction = Marshal.GetDelegateForFunctionPointer<ScintillaDirectFunction>(directFunction);
        }

        // 使用内置 DirectWrite，并按产品语言提供中文缺字回退的区域信息。
        _ = NativeMethods.SendMessage(Handle, SetTechnology, 1, 0);
        nint locale = Marshal.StringToCoTaskMemUTF8("zh-CN");
        try
        {
            _ = NativeMethods.SendMessage(Handle, SetFontLocale, 0, locale);
        }
        finally
        {
            Marshal.FreeCoTaskMem(locale);
        }
        _ = NativeMethods.SendMessage(Handle, SetCodePage, Utf8CodePage, 0);
        _ = NativeMethods.SendMessage(Handle, SetScrollWidthTracking, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetScrollWidth, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetMargins, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetMarginType, 0, MarginNumber);
        _ = NativeMethods.SendMessage(Handle, SetMarginWidth, 0, 44);
        _ = NativeMethods.SendMessage(Handle, UsePopup, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetReadOnly, 1, 0);
        RegisterViewport();
    }

    internal nint Handle { get; private set; }

    internal int FirstVisibleLine => Handle == 0
        ? 0
        : checked((int)NativeMethods.SendMessage(Handle, GetFirstVisibleLineMessage, 0, 0));

    internal bool IsReadOnly => Handle != 0 && NativeMethods.SendMessage(Handle, GetReadOnly, 0, 0) != 0;

    internal bool IsModified => Handle != 0 && NativeMethods.SendMessage(Handle, GetModify, 0, 0) != 0;

    internal int MarginCount => Handle == 0
        ? 0
        : checked((int)NativeMethods.SendMessage(Handle, GetMargins, 0, 0));

    internal bool BlameVisible => _blameVisible;

    internal int SelectedBlameLineForTest => _selectedBlameLine;


    internal static int ConvertPixelsToFontHundredthsForTest(double fontSize)
    {
        return ConvertPixelsToFontHundredths(fontSize);
    }

    internal static int ConvertPixelsToFontHundredthsForTest(double fontSize, double dpiAdjustment)
    {
        return ConvertPixelsToFontHundredths(fontSize, dpiAdjustment);
    }

    internal static List<int> CalculateCoveredLinesForTest(
        string source,
        IReadOnlyList<(int Start, int Length)> ranges)
    {
        return CalculateCoveredLines(source, ranges);
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    internal void SetVisible(bool visible)
    {
        if (Handle != 0)
        {
            _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
        }
    }

    internal void SetTextContent(string text)
    {
        ClearDisplayGaps();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        _contentVersion++;
        int firstVisibleLine = FirstVisibleLine;
        int anchor = checked((int)NativeMethods.SendMessage(Handle, GetAnchor, 0, 0));
        int caret = checked((int)NativeMethods.SendMessage(Handle, GetCurrentPosition, 0, 0));
        nint xOffset = NativeMethods.SendMessage(Handle, GetXOffset, 0, 0);
        nint utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            _ = NativeMethods.SendMessage(Handle, SetReadOnly, 0, 0);
            _ = NativeMethods.SendMessage(Handle, ShowLines, 0, unchecked((nint)(-1)));
            _ = NativeMethods.SendMessage(Handle, SetText, 0, utf8);
            // 旧正文的行标记可能在替换文本后合并到首行，不能带入新文档或空提示页。
            _ = NativeMethods.SendMessage(Handle, MarkerDeleteAll, unchecked((nuint)(-1)), 0);
            _ = NativeMethods.SendMessage(Handle, EmptyUndoBuffer, 0, 0);
            _ = NativeMethods.SendMessage(Handle, SetReadOnly, 1, 0);
            UpdateLineNumberMargin();
            // 新正文不沿用上一版本的最长行宽；原控件只度量绘制所需的可见行。
            _ = NativeMethods.SendMessage(Handle, SetScrollWidth, 1, 0);
            _viewportNeedsRefresh = true;
            int byteLength = Encoding.UTF8.GetByteCount(text);
            // 锚点与光标分别保留方向；截断到新正文时不能落在 UTF-8 多字节字符内部。
            int safeAnchor = ClampUtf8Position(anchor, byteLength);
            int safeCaret = ClampUtf8Position(caret, byteLength);
            _ = NativeMethods.SendMessage(Handle, SetSelection, unchecked((nuint)safeAnchor), safeCaret);
            _ = NativeMethods.SendMessage(Handle, SetFirstVisibleLineMessage, unchecked((nuint)Math.Max(0, firstVisibleLine)), 0);
            _ = NativeMethods.SendMessage(Handle, SetXOffset, unchecked((nuint)xOffset), 0);
        }
        finally
        {
            _ = NativeMethods.SendMessage(Handle, SetReadOnly, 1, 0);
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private int ClampUtf8Position(int position, int byteLength)
    {
        int bounded = Math.Clamp(position, 0, byteLength);
        return bounded == byteLength ? bounded
            : checked((int)NativeMethods.SendMessage(Handle, PositionBefore, unchecked((nuint)(bounded + 1)), 0));
    }

    internal string GetTextContent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int byteLength = checked((int)NativeMethods.SendMessage(Handle, GetTextLength, 0, 0));
        nint buffer = Marshal.AllocCoTaskMem(checked(byteLength + 1));
        try
        {
            _ = NativeMethods.SendMessage(
                Handle,
                GetText,
                unchecked((nuint)(byteLength + 1)),
                buffer);
            return Marshal.PtrToStringUTF8(buffer, byteLength) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    internal void SetEditable(bool editable)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = NativeMethods.SendMessage(Handle, SetReadOnly, editable ? 0U : 1U, 0);
    }

    internal void MarkSaved()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = NativeMethods.SendMessage(Handle, SetSavePoint, 0, 0);
    }

    internal void ApplyAppearance(string fontFamily, double fontSize, bool darkTheme, bool mutedText = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        _ = NativeMethods.SetWindowTheme(Handle, darkTheme ? "DarkMode_Explorer" : "Explorer", null);
        nint font = Marshal.StringToCoTaskMemUTF8(NativeFontResolver.ResolveMonospace(fontFamily));
        try
        {
            double dpiAdjustment = NativeTheme.GetEmbeddedContentDpiAdjustment(Handle);
            NativeThemePalette palette = NativeTheme.Palette(darkTheme);
            int background = unchecked((int)palette.Panel);
            int marginBackground = unchecked((int)palette.PanelMuted);
            int marginForeground = unchecked((int)palette.Muted);
            int foreground = mutedText
                ? marginForeground
                : unchecked((int)palette.Text);
            _ = NativeMethods.SendMessage(Handle, StyleSetFont, StyleDefault, font);
            _ = NativeMethods.SendMessage(
                Handle,
                StyleSetSizeFractional,
                unchecked((nuint)StyleDefault),
                ConvertPixelsToFontHundredths(fontSize, dpiAdjustment));
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, unchecked((nuint)StyleDefault), foreground);
            _ = NativeMethods.SendMessage(Handle, StyleSetBackground, unchecked((nuint)StyleDefault), background);
            _ = NativeMethods.SendMessage(Handle, StyleClearAll, 0, 0);
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, StyleLineNumber, marginForeground);
            _ = NativeMethods.SendMessage(Handle, StyleSetBackground, StyleLineNumber, marginBackground);
            _ = NativeMethods.SendMessage(Handle, StyleSetFont, StyleBlame, font);
            _ = NativeMethods.SendMessage(
                Handle,
                StyleSetSizeFractional,
                StyleBlame,
                ConvertPixelsToFontHundredths(fontSize, dpiAdjustment));
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, StyleBlame, marginForeground);
            _ = NativeMethods.SendMessage(Handle, StyleSetBackground, StyleBlame, marginBackground);
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, StyleBlameSelected, unchecked((int)palette.Text));
            _ = NativeMethods.SendMessage(Handle, StyleSetBackground, StyleBlameSelected, unchecked((int)palette.AccentSoft));
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, StyleDiffLineNumber, marginForeground);
            _ = NativeMethods.SendMessage(
                Handle,
                StyleSetForeground,
                StyleDiffRemovedMarker,
                unchecked((int)palette.Danger));
            _ = NativeMethods.SendMessage(
                Handle,
                StyleSetForeground,
                StyleDiffAddedMarker,
                unchecked((int)palette.Success));
            _ = NativeMethods.SendMessage(Handle, SetCaretForeground, unchecked((nuint)foreground), 0);
            _ = NativeMethods.SendMessage(Handle, SetSelectionBackground, 1, unchecked((nint)palette.AccentSoft));
            _ = NativeMethods.SendMessage(Handle, SetMarginBackground, 0, marginBackground);
            _ = NativeMethods.SendMessage(Handle, SetMarginForeground, 0, marginForeground);
            _ = NativeMethods.SendMessage(
                Handle,
                SetExtraAscentMessage,
                0,
                0);
            _ = NativeMethods.SendMessage(
                Handle,
                SetExtraDescentMessage,
                0,
                0);
            int naturalHeight = (int)NativeMethods.SendMessage(Handle, TextHeight, 0, 0);
            int targetHeight = (int)Math.Round(NativeTheme.Scale((float)(fontSize * 1.7)), MidpointRounding.AwayFromZero);
            int spacing = Math.Max(0, targetHeight - naturalHeight);
            _ = NativeMethods.SendMessage(Handle, SetExtraAscentMessage, (nuint)(spacing / 2), 0);
            _ = NativeMethods.SendMessage(Handle, SetExtraDescentMessage, (nuint)(spacing - spacing / 2), 0);
            if (_blameVisible)
            {
                _ = NativeMethods.SendMessage(Handle, SetMarginBackground, 1, marginBackground);
                _ = NativeMethods.SendMessage(Handle, SetMarginForeground, 1, marginForeground);
            }
            UpdateLineNumberMargin();
            _ = NativeMethods.SendMessage(Handle, SetScrollWidth, 1, 0);
            ConfigureBlameAppearance(fontFamily, fontSize, darkTheme);
            _viewportNeedsRefresh = true;
            NativeScrollBarCornerTheme.Apply(Handle, palette.Panel);
        }
        finally
        {
            Marshal.FreeCoTaskMem(font);
        }
    }

    internal void SetWordWrap(bool enabled)
    {
        _ = NativeMethods.SendMessage(Handle, SetWrapMode, enabled ? 1U : 0U, 0);
    }

    internal void SetWhitespaceVisible(bool visible)
    {
        _ = NativeMethods.SendMessage(Handle, SetViewWhitespace, visible ? 1U : 0U, 0);
    }

    internal void SetLineNumbersVisible(bool visible)
    {
        _showLineNumbers = visible;
        UpdateLineNumberMargin();
    }

    internal bool LineNumbersVisibleForTest => _showLineNumbers;

    internal void SetScrollBarsVisible(bool horizontal, bool vertical)
    {
        _ = NativeMethods.SendMessage(Handle, SetHorizontalScrollBarMessage, horizontal ? 1U : 0U, 0);
        _ = NativeMethods.SendMessage(Handle, SetVerticalScrollBarMessage, vertical ? 1U : 0U, 0);
    }

    internal void SetTextPadding(int left, int right)
    {
        if (Handle == 0)
        {
            return;
        }

        _ = NativeMethods.SendMessage(Handle, SetMarginLeft, 0, Math.Max(0, left));
        _ = NativeMethods.SendMessage(Handle, SetMarginRight, 0, Math.Max(0, right));
        _viewportNeedsRefresh = true;
    }

    internal (int Left, int Right) TextPaddingForTest => Handle == 0
        ? (0, 0)
        : ((int)NativeMethods.SendMessage(Handle, GetMarginLeft, 0, 0),
            (int)NativeMethods.SendMessage(Handle, GetMarginRight, 0, 0));

    internal int MeasureTextWidth(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint sample = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return checked((int)NativeMethods.SendMessage(Handle, TextWidth, StyleDefault, sample));
        }
        finally
        {
            Marshal.FreeCoTaskMem(sample);
        }
    }

    internal void SetFirstVisibleLine(int line)
    {
        if (Handle != 0 && FirstVisibleLine != line)
        {
            _ = NativeMethods.SendMessage(
                Handle,
                SetFirstVisibleLineMessage,
                unchecked((nuint)Math.Max(0, line)),
                0);
        }
    }

    internal bool IsUpdateUiNotification(nint notificationPointer)
    {
        if (Handle == 0 || notificationPointer == 0)
        {
            return false;
        }

        ScintillaNotification notification = Marshal.PtrToStructure<ScintillaNotification>(notificationPointer);
        return notification.Header.WindowFrom == Handle
            && notification.Header.Code == NotificationUpdateUi;
    }

    internal bool IsVerticalScrollNotification(nint notificationPointer)
    {
        if (Handle == 0 || notificationPointer == 0) return false;
        ScintillaNotification notification = Marshal.PtrToStructure<ScintillaNotification>(notificationPointer);
        return notification.Header.WindowFrom == Handle && notification.Header.Code == NotificationUpdateUi
            && (notification.Updated & 4) != 0; // SC_UPDATE_V_SCROLL；选区和水平滚动不驱动其他栏。
    }

    internal bool IsModifiedNotification(nint notificationPointer)
    {
        if (Handle == 0 || notificationPointer == 0)
        {
            return false;
        }

        ScintillaNotification notification = Marshal.PtrToStructure<ScintillaNotification>(notificationPointer);
        return notification.Header.WindowFrom == Handle
            && notification.Header.Code == NotificationModified
            // SC_MOD_INSERTTEXT / SC_MOD_DELETETEXT；样式和标记通知不代表正文变化。
            && (notification.ModificationType & 3) != 0;
    }

    internal void SetBlame(IReadOnlyList<GitBlameLine> lines)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(lines);
        _blameRows.Clear();
        _selectedBlameLine = -1;
        _ = NativeMethods.SendMessage(Handle, MarginTextClearAll, 0, 0);
        _ = NativeMethods.SendMessage(Handle, SetMargins, 2, 0);
        _ = NativeMethods.SendMessage(Handle, SetMarginType, 0, MarginText);
        _ = NativeMethods.SendMessage(Handle, SetMarginSensitive, 0, 1);
        _ = NativeMethods.SendMessage(Handle, SetMarginType, 1, MarginNumber);
        _blameVisible = true;
        UpdateLineNumberMargin();

        foreach (GitBlameLine line in lines)
        {
            int zeroBasedLine = Math.Max(0, line.LineNumber - 1);
            _blameRows[zeroBasedLine] = new(line.AuthorDate.ToString("yyyy/M/d", System.Globalization.CultureInfo.InvariantCulture),
                line.AuthorName, line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), line.CommitHash);
        }
        PrepareBlameFont();
    }

    internal void ClearBlame()
    {
        if (!_blameVisible) return;
        _blameVisible = false;
        _blameRows.Clear();
        ReleaseBlameDrawing();
        _selectedBlameLine = -1;
        _ = NativeMethods.SendMessage(Handle, MarginTextClearAll, 0, 0);
        _ = NativeMethods.SendMessage(Handle, SetMargins, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetMarginType, 0, MarginNumber);
        _ = NativeMethods.SendMessage(Handle, SetMarginSensitive, 0, 0);
        UpdateLineNumberMargin();
    }

    internal bool TryGetBlameCommit(nint notificationPointer, out string? commitHash)
    {
        commitHash = null;
        if (!_blameVisible || notificationPointer == 0)
        {
            return false;
        }

        ScintillaNotification notification = Marshal.PtrToStructure<ScintillaNotification>(notificationPointer);
        if (notification.Header.WindowFrom != Handle
            || notification.Header.Code != NotificationMarginClick
            || notification.Margin != 0)
        {
            return false;
        }

        // SCN_MARGINCLICK 提供 UTF-8 字节位置；line 字段只用于 SCN_MODIFIED。
        int line = checked((int)NativeMethods.SendMessage(Handle, 2166,
            unchecked((nuint)notification.Position), 0));
        if (!_blameRows.TryGetValue(line, out BlameRow? row)) return false;
        _selectedBlameLine = line;
        InvalidateBlame();
        commitHash = row.Commit;
        return true;
    }

    internal void SetHighlights(
        int indicator,
        string source,
        IReadOnlyList<(int Start, int Length)> ranges,
        int red,
        int green,
        int blue)
    {
        ApplyDecorations(HighlightCommands(indicator, source, ranges, red, green, blue));
    }

    internal void SetLineBackgrounds(
        int marker,
        string source,
        IReadOnlyList<(int Start, int Length)> ranges,
        int red,
        int green,
        int blue)
    {
        ApplyDecorations(BackgroundCommands(marker, source, ranges, red, green, blue));
    }

    internal void SetLineBackgroundColor(int marker, int red, int green, int blue)
    {
        EnsureDecorationTarget();
        ApplyDecoration(new(MarkerSetBackground, (nuint)marker, ToColor(red, green, blue)));
    }

    internal void ClearBackgroundLines(int marker)
    {
        EnsureDecorationTarget();
        ApplyDecoration(new(MarkerDeleteAll, (nuint)marker, 0));
        ApplyDecoration(new(MarkerDefine, (nuint)marker, MarkerBackground));
        ApplyDecoration(new(MarkerSetAlpha, (nuint)marker, 256));
    }

    internal void AddBackgroundLines(int marker, int firstLine, int count)
    {
        EnsureDecorationTarget();
        for (int line = firstLine; line < firstLine + count; line++)
            ApplyDecoration(new(MarkerAdd, (nuint)line, marker));
    }

    internal void SetHiddenLines(IReadOnlyList<int> lines)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = NativeMethods.SendMessage(Handle, ShowLines, 0, unchecked((nint)(-1)));
        foreach (int line in lines)
        {
            if (line >= 0)
                _ = NativeMethods.SendMessage(Handle, HideLines, unchecked((nuint)line), line);
        }
    }

    internal bool IsLineVisibleForTest(int line) =>
        NativeMethods.SendMessage(Handle, GetLineVisible, unchecked((nuint)Math.Max(0, line)), 0) != 0;

    internal void SetDiffStyles(
        string source,
        IReadOnlyList<(int Start, int Length)> lineNumberRanges,
        IReadOnlyList<(int Start, int Length)> removedMarkerRanges,
        IReadOnlyList<(int Start, int Length)> addedMarkerRanges)
    {
        ApplyDecorations(DiffStyleCommands(source, lineNumberRanges, removedMarkerRanges, addedMarkerRanges));
    }

    internal Task<bool> SetHighlightsAsync(int indicator, string source, IReadOnlyList<(int Start, int Length)> ranges,
        int red, int green, int blue, Func<bool> isCurrent, CancellationToken cancellationToken) =>
        ApplyDecorationsAsync(HighlightCommands(indicator, source, ranges, red, green, blue), isCurrent, cancellationToken);

    internal Task<bool> SetLineBackgroundsAsync(int marker, string source, IReadOnlyList<(int Start, int Length)> ranges,
        int red, int green, int blue, Func<bool> isCurrent, CancellationToken cancellationToken) =>
        ApplyDecorationsAsync(BackgroundCommands(marker, source, ranges, red, green, blue), isCurrent, cancellationToken);

    internal Task<bool> SetDiffStylesAsync(string source, IReadOnlyList<(int Start, int Length)> lineNumberRanges,
        IReadOnlyList<(int Start, int Length)> removedMarkerRanges, IReadOnlyList<(int Start, int Length)> addedMarkerRanges,
        Func<bool> isCurrent, CancellationToken cancellationToken) =>
        ApplyDecorationsAsync(DiffStyleCommands(source, lineNumberRanges, removedMarkerRanges, addedMarkerRanges),
            isCurrent, cancellationToken);

    internal void GoToLine(int oneBasedLine, bool focus = true)
    {
        int line = Math.Max(0, oneBasedLine - 1);
        _ = NativeMethods.SendMessage(Handle, GoToLineMessage, unchecked((nuint)line), 0);
        _ = NativeMethods.SendMessage(Handle, ScrollCaret, 0, 0);
        if (focus)
        {
            _ = NativeMethods.SetFocus(Handle);
        }
    }

    internal void GoToPosition(int utf16Position)
    {
        string text = GetTextContent();
        int bytePosition = Encoding.UTF8.GetByteCount(text.AsSpan(
            0,
            Math.Clamp(utf16Position, 0, text.Length)));
        _ = NativeMethods.SendMessage(
            Handle,
            GoToPositionMessage,
            unchecked((nuint)bytePosition),
            0);
    }

    internal void SelectUtf8Range(int startCharacter, int endCharacter, string source, bool focus = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        int safeStart = Math.Clamp(startCharacter, 0, source.Length);
        int safeEnd = Math.Clamp(endCharacter, safeStart, source.Length);
        int startByte = Encoding.UTF8.GetByteCount(source.AsSpan(0, safeStart));
        int endByte = startByte + Encoding.UTF8.GetByteCount(source.AsSpan(safeStart, safeEnd - safeStart));
        _ = NativeMethods.SendMessage(Handle, SetSelection, unchecked((nuint)startByte), endByte);
        _ = NativeMethods.SendMessage(Handle, ScrollCaret, 0, 0);
        if (focus) _ = NativeMethods.SetFocus(Handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseBlameDrawing();
        _blameRows.Clear();
        _directFunction = null;
        _directPointer = 0;
        if (Handle != 0 && NativeMethods.IsWindow(Handle))
        {
            NativeScrollBarCornerTheme.Unregister(Handle);
            _ = NativeMethods.DestroyWindow(Handle);
        }

        Handle = 0;
    }

    private static void EnsureLibraryLoaded()
    {
        lock (LibraryGate)
        {
            if (_library != 0)
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "native", "Scintilla.dll");
            _library = NativeMethods.LoadLibrary(path);
            if (_library == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ScintillaMissing);
            }
        }
    }

    private void UpdateLineNumberMargin()
    {
        int margin = _blameVisible ? 1 : 0;
        if (!_showLineNumbers)
        {
            _ = NativeMethods.SendMessage(Handle, SetMarginWidth, unchecked((nuint)margin), 0);
            return;
        }
        int lineCount = checked((int)NativeMethods.SendMessage(Handle, GetLineCount, 0, 0));
        int digits = Math.Max(
            3,
            Math.Max(1, lineCount).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        // 行号宽度由 Scintilla 的实际等宽字形度量决定，界面字号不能移动正文起点。
        // 只查询行数并度量最长行号，不为刷新边栏复制整个文件正文。
        nint sample = Marshal.StringToCoTaskMemUTF8(new string('9', digits));
        try
        {
            int textWidth = checked((int)NativeMethods.SendMessage(Handle, TextWidth, StyleLineNumber, sample));
            int width = NativeTheme.Scale(28) + textWidth;
            _ = NativeMethods.SendMessage(Handle, SetMarginWidth, unchecked((nuint)margin), width);
        }
        finally
        {
            Marshal.FreeCoTaskMem(sample);
        }
    }

    private static List<int> CalculateCoveredLines(
        string source,
        IReadOnlyList<(int Start, int Length)> ranges)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        List<int> lineStarts = [0];
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                lineStarts.Add(index + 1);
            }
        }

        SortedSet<int> lines = [];
        foreach ((int start, int length) in ranges)
        {
            int safeStart = Math.Clamp(start, 0, source.Length);
            int safeLength = Math.Clamp(length, 0, source.Length - safeStart);
            if (safeLength == 0)
            {
                continue;
            }

            int firstLine = FindLine(lineStarts, safeStart);
            int lastLine = FindLine(lineStarts, safeStart + safeLength - 1);
            for (int line = firstLine; line <= lastLine; line++)
            {
                lines.Add(line);
            }
        }

        return [.. lines];
    }

    private static int FindLine(List<int> lineStarts, int position)
    {
        int index = lineStarts.BinarySearch(position);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    private static int ConvertPixelsToFontHundredths(double fontSize)
    {
        return ConvertPixelsToFontHundredths(fontSize, 1d);
    }

    private static int ConvertPixelsToFontHundredths(double fontSize, double dpiAdjustment)
    {
        const double pointsPerPixel = 72d / 96d;
        return checked((int)Math.Round(
            Math.Clamp(fontSize, 9, 40)
            * pointsPerPixel
            * 100
            * Math.Clamp(dpiAdjustment, 0.25d, 4d)));
    }

    private static int ToColor(int red, int green, int blue)
    {
        return red | (green << 8) | (blue << 16);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScintillaNotification
    {
        internal NativeMethods.NotificationHeader Header;
        internal nint Position;
        internal int Character;
        internal int Modifiers;
        internal int ModificationType;
        internal nint Text;
        internal nint Length;
        internal nint LinesAdded;
        internal int Message;
        internal nuint WordParameter;
        internal nint LongParameter;
        internal nint Line;
        internal int FoldLevelNow;
        internal int FoldLevelPrevious;
        internal int Margin;
        internal int ListType;
        internal int X;
        internal int Y;
        internal int Token;
        internal nint AnnotationLinesAdded;
        internal int Updated;
    }

    private static IEnumerable<(int Start, int Length)> Utf8Ranges(
        string source,
        IReadOnlyList<(int Start, int Length)> ranges)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        int convertedCharacterCount = 0;
        int convertedByteCount = 0;
        foreach ((int start, int length) in ranges)
        {
            int safeStart = Math.Clamp(start, 0, source.Length);
            int safeLength = Math.Clamp(length, 0, source.Length - safeStart);
            int startByte;
            if (safeStart >= convertedCharacterCount)
            {
                convertedByteCount += Encoding.UTF8.GetByteCount(
                    source.AsSpan(convertedCharacterCount, safeStart - convertedCharacterCount));
                startByte = convertedByteCount;
            }
            else
            {
                startByte = Encoding.UTF8.GetByteCount(source.AsSpan(0, safeStart));
            }

            int rangeBytes = Encoding.UTF8.GetByteCount(source.AsSpan(safeStart, safeLength));
            convertedCharacterCount = safeStart + safeLength;
            convertedByteCount = startByte + rangeBytes;
            if (rangeBytes <= 0)
            {
                continue;
            }

            yield return (startByte, rangeBytes);
        }
    }

    private readonly record struct DecorationCommand(uint Message, nuint Word, nint Long,
        uint SetupMessage = 0, nuint SetupWord = 0);

    private static IEnumerable<DecorationCommand> DiffStyleCommands(string source,
        IReadOnlyList<(int Start, int Length)> lineNumbers,
        IReadOnlyList<(int Start, int Length)> removed,
        IReadOnlyList<(int Start, int Length)> added) =>
        StyleCommands(source, lineNumbers, StyleDiffLineNumber)
            .Concat(StyleCommands(source, removed, StyleDiffRemovedMarker))
            .Concat(StyleCommands(source, added, StyleDiffAddedMarker));

    private static IEnumerable<DecorationCommand> StyleCommands(string source,
        IReadOnlyList<(int Start, int Length)> ranges, int style)
    {
        foreach ((int start, int length) in Utf8Ranges(source, ranges))
            yield return new(SetStyling, (nuint)length, style, StartStyling, (nuint)start);
    }

    private static IEnumerable<DecorationCommand> HighlightCommands(int indicator, string source,
        IReadOnlyList<(int Start, int Length)> ranges, int red, int green, int blue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        yield return new(IndicatorClearRange, 0, Encoding.UTF8.GetByteCount(source), SetIndicatorCurrent, (nuint)indicator);
        yield return new(IndicatorSetStyle, (nuint)indicator, IndicatorStraightBox);
        yield return new(IndicatorSetForeground, (nuint)indicator, ToColor(red, green, blue));
        yield return new(IndicatorSetAlpha, (nuint)indicator, 70);
        foreach ((int start, int length) in Utf8Ranges(source, ranges))
            yield return new(IndicatorFillRange, (nuint)start, length, SetIndicatorCurrent, (nuint)indicator);
    }

    private static IEnumerable<DecorationCommand> BackgroundCommands(int marker, string source,
        IReadOnlyList<(int Start, int Length)> ranges, int red, int green, int blue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        yield return new(MarkerDeleteAll, (nuint)marker, 0);
        yield return new(MarkerDefine, (nuint)marker, MarkerBackground);
        yield return new(MarkerSetBackground, (nuint)marker, ToColor(red, green, blue));
        yield return new(MarkerSetAlpha, (nuint)marker, 256);
        foreach (int line in CalculateCoveredLines(source, ranges))
            yield return new(MarkerAdd, (nuint)line, marker);
    }

    private void ApplyDecorations(IEnumerable<DecorationCommand> commands)
    {
        EnsureDecorationTarget();
        foreach (DecorationCommand command in commands) { ApplyDecoration(command); }
    }

    private async Task<bool> ApplyDecorationsAsync(IEnumerable<DecorationCommand> commands,
        Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        int contentVersion = _contentVersion;
        bool CanApply() => !_disposed && contentVersion == _contentVersion
            && !cancellationToken.IsCancellationRequested && isCurrent() && NativeMethods.IsWindow(Handle);
        if (!CanApply()) { return false; }
        long started = Stopwatch.GetTimestamp();
        int count = 0;
        foreach (DecorationCommand command in commands)
        {
            ApplyDecoration(command);
            if (++count % 64 == 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 4)
            {
                // Task.Yield 会被主窗口当前的分发队列立即消费；短延迟才能真正回到 Win32 消息循环。
                await Task.Delay(1, cancellationToken);
                if (!CanApply()) { return false; }
                started = Stopwatch.GetTimestamp();
            }
        }
        return CanApply();
    }

    private void ApplyDecoration(DecorationCommand command)
    {
        // 样式起点和写入、高亮编号和填充必须在同一时间片内成对执行。
        if (command.SetupMessage != 0) { _ = SendDecorationMessage(command.SetupMessage, command.SetupWord, 0); }
        _ = SendDecorationMessage(command.Message, command.Word, command.Long);
    }

    private nint SendDecorationMessage(uint message, nuint wordParameter, nint longParameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // 官方直接调用接口仅能用于控件所属线程；跨线程调用仍由 Win32 同步。
        return Environment.CurrentManagedThreadId == _ownerThreadId && _directFunction is not null
            ? _directFunction(_directPointer, message, wordParameter, longParameter)
            : NativeMethods.SendMessage(Handle, message, wordParameter, longParameter);
    }

    private void EnsureDecorationTarget()
    {
        // 父窗口可能先销毁 HWND，此时不能再使用控件的原生上下文。
        ObjectDisposedException.ThrowIf(_disposed || !NativeMethods.IsWindow(Handle), this);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ScintillaDirectFunction(nint pointer, uint message, nuint wordParameter, nint longParameter);
}
