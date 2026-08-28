using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Augit.App;

internal sealed class ScintillaControl : IDisposable
{
    private const uint SetCodePage = 2037;
    private const uint SetText = 2181;
    private const uint GetText = 2182;
    private const uint GetTextLength = 2183;
    private const uint SetReadOnly = 2171;
    private const uint GetReadOnly = 2140;
    private const uint GetModify = 2159;
    private const uint SetSavePoint = 2014;
    private const uint EmptyUndoBuffer = 2175;
    private const uint SetMarginType = 2240;
    private const uint SetMarginWidth = 2242;
    private const uint SetWrapMode = 2268;
    private const uint SetViewWhitespace = 2021;
    private const uint GoToLineMessage = 2024;
    private const uint GoToPositionMessage = 2025;
    private const uint SetSelection = 2160;
    private const uint GetSelectionStart = 2143;
    private const uint GetSelectionEnd = 2145;
    private const uint ScrollCaret = 2169;
    private const uint GetFirstVisibleLineMessage = 2152;
    private const uint SetFirstVisibleLineMessage = 2613;
    private const uint StyleClearAll = 2050;
    private const uint StyleSetForeground = 2051;
    private const uint StyleSetBackground = 2052;
    private const uint StyleSetFont = 2056;
    private const uint StyleSetSizeFractional = 2061;
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
    private const int Utf8CodePage = 65001;
    private const int MarginNumber = 1;
    private const int StyleDefault = 32;
    private const int IndicatorStraightBox = 8;
    private static readonly object LibraryGate = new();
    private static nint _library;
    private bool _disposed;
    private bool _showLineNumbers = true;

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

        _ = NativeMethods.SendMessage(Handle, SetCodePage, Utf8CodePage, 0);
        _ = NativeMethods.SendMessage(Handle, SetMarginType, 0, MarginNumber);
        _ = NativeMethods.SendMessage(Handle, SetMarginWidth, 0, 44);
        _ = NativeMethods.SendMessage(Handle, UsePopup, 1, 0);
        _ = NativeMethods.SendMessage(Handle, SetReadOnly, 1, 0);
    }

    internal nint Handle { get; private set; }

    internal int FirstVisibleLine => Handle == 0
        ? 0
        : checked((int)NativeMethods.SendMessage(Handle, GetFirstVisibleLineMessage, 0, 0));

    internal bool IsReadOnly => Handle != 0 && NativeMethods.SendMessage(Handle, GetReadOnly, 0, 0) != 0;

    internal bool IsModified => Handle != 0 && NativeMethods.SendMessage(Handle, GetModify, 0, 0) != 0;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        int firstVisibleLine = FirstVisibleLine;
        int selectionStart = checked((int)NativeMethods.SendMessage(Handle, GetSelectionStart, 0, 0));
        int selectionEnd = checked((int)NativeMethods.SendMessage(Handle, GetSelectionEnd, 0, 0));
        nint utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            _ = NativeMethods.SendMessage(Handle, SetReadOnly, 0, 0);
            _ = NativeMethods.SendMessage(Handle, SetText, 0, utf8);
            _ = NativeMethods.SendMessage(Handle, EmptyUndoBuffer, 0, 0);
            _ = NativeMethods.SendMessage(Handle, SetReadOnly, 1, 0);
            int lineCount = 1 + text.Count(character => character == '\n');
            int marginWidth = 16 + (Math.Max(3, lineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) * 8);
            _ = NativeMethods.SendMessage(Handle, SetMarginWidth, 0, _showLineNumbers ? marginWidth : 0);
            _ = NativeMethods.SendMessage(Handle, SetFirstVisibleLineMessage, unchecked((nuint)Math.Max(0, firstVisibleLine)), 0);
            int byteLength = Encoding.UTF8.GetByteCount(text);
            int safeStart = Math.Clamp(selectionStart, 0, byteLength);
            int safeEnd = Math.Clamp(selectionEnd, safeStart, byteLength);
            _ = NativeMethods.SendMessage(Handle, SetSelection, unchecked((nuint)safeStart), safeEnd);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
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

    internal void ApplyAppearance(string fontFamily, double fontSize, bool darkTheme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        nint font = Marshal.StringToCoTaskMemUTF8(fontFamily);
        try
        {
            int foreground = darkTheme ? ToColor(0xE8, 0xEA, 0xED) : ToColor(0x20, 0x21, 0x24);
            int background = darkTheme ? ToColor(0x29, 0x2A, 0x2D) : ToColor(0xFF, 0xFF, 0xFF);
            int marginBackground = darkTheme ? ToColor(0x22, 0x23, 0x26) : ToColor(0xF3, 0xF4, 0xF6);
            int marginForeground = darkTheme ? ToColor(0x9A, 0x9D, 0xA3) : ToColor(0x6B, 0x70, 0x78);
            _ = NativeMethods.SendMessage(Handle, StyleSetFont, StyleDefault, font);
            _ = NativeMethods.SendMessage(
                Handle,
                StyleSetSizeFractional,
                unchecked((nuint)StyleDefault),
                checked((nint)Math.Round(Math.Clamp(fontSize, 9, 40) * 100)));
            _ = NativeMethods.SendMessage(Handle, StyleSetForeground, unchecked((nuint)StyleDefault), foreground);
            _ = NativeMethods.SendMessage(Handle, StyleSetBackground, unchecked((nuint)StyleDefault), background);
            _ = NativeMethods.SendMessage(Handle, StyleClearAll, 0, 0);
            _ = NativeMethods.SendMessage(Handle, SetCaretForeground, unchecked((nuint)foreground), 0);
            _ = NativeMethods.SendMessage(Handle, SetSelectionBackground, 1, ToColor(0x66, 0x8F, 0xC7));
            _ = NativeMethods.SendMessage(Handle, SetMarginBackground, 0, marginBackground);
            _ = NativeMethods.SendMessage(Handle, SetMarginForeground, 0, marginForeground);
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
        _ = NativeMethods.SendMessage(Handle, SetMarginWidth, 0, visible ? 44 : 0);
    }

    internal void SetHighlights(
        int indicator,
        string source,
        IReadOnlyList<(int Start, int Length)> ranges,
        int red,
        int green,
        int blue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        int byteLength = Encoding.UTF8.GetByteCount(source);
        _ = NativeMethods.SendMessage(Handle, SetIndicatorCurrent, unchecked((nuint)indicator), 0);
        _ = NativeMethods.SendMessage(Handle, IndicatorClearRange, 0, byteLength);
        _ = NativeMethods.SendMessage(Handle, IndicatorSetStyle, unchecked((nuint)indicator), IndicatorStraightBox);
        _ = NativeMethods.SendMessage(
            Handle,
            IndicatorSetForeground,
            unchecked((nuint)indicator),
            ToColor(red, green, blue));
        _ = NativeMethods.SendMessage(Handle, IndicatorSetAlpha, unchecked((nuint)indicator), 70);
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
            if (rangeBytes > 0)
            {
                _ = NativeMethods.SendMessage(
                    Handle,
                    IndicatorFillRange,
                    unchecked((nuint)startByte),
                    rangeBytes);
            }
        }
    }

    internal void GoToLine(int oneBasedLine)
    {
        int line = Math.Max(0, oneBasedLine - 1);
        _ = NativeMethods.SendMessage(Handle, GoToLineMessage, unchecked((nuint)line), 0);
        _ = NativeMethods.SendMessage(Handle, ScrollCaret, 0, 0);
        _ = NativeMethods.SetFocus(Handle);
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

    internal void SelectUtf8Range(int startCharacter, int endCharacter, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int safeStart = Math.Clamp(startCharacter, 0, source.Length);
        int safeEnd = Math.Clamp(endCharacter, safeStart, source.Length);
        int startByte = Encoding.UTF8.GetByteCount(source.AsSpan(0, safeStart));
        int endByte = startByte + Encoding.UTF8.GetByteCount(source.AsSpan(safeStart, safeEnd - safeStart));
        _ = NativeMethods.SendMessage(Handle, SetSelection, unchecked((nuint)startByte), endByte);
        _ = NativeMethods.SendMessage(Handle, ScrollCaret, 0, 0);
        _ = NativeMethods.SetFocus(Handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != 0 && NativeMethods.IsWindow(Handle))
        {
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

    private static int ToColor(int red, int green, int blue)
    {
        return red | (green << 8) | (blue << 16);
    }
}
