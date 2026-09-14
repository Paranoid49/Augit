namespace Augit.App;

internal sealed partial class NativeGitPanel
{
    private const int DiffBoundaryHintIdentifier = 70;
    private const uint ScintillaPositionFromLine = 2167;
    private const uint ScintillaPointYFromPosition = 2165;
    private const uint ScintillaTextHeight = 2279;
    private const uint ButtonPerformClick = 0x00F5;
    private nint _diffBoundaryHint;
    private int _diffBoundaryDirection;
    private string? _diffBoundaryTargetPath;

    internal nint DiffBoundaryHintHandleForTest => _diffBoundaryHint;

    internal nint DiffChangeButtonForTest(int direction) => direction < 0 ? _previousChangeButton : _nextChangeButton;

    internal bool DiffBoundaryHintVisibleForTest => _diffBoundaryHint != 0
        && NativeMethods.IsWindowVisible(_diffBoundaryHint);

    internal string DiffBoundaryHintTextForTest => NativeMethods.GetWindowTextValue(_diffBoundaryHint);

    internal string DiffFileSummaryForTest => NativeMethods.GetWindowTextValue(_diffFileSummary);

    internal bool HandleDiffToolbarInput(NativeMethods.Message message)
    {
        if (message.MessageId == NativeMethods.WindowMessageKeyDown
            && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyEnter
            && NativeMethods.GetFocus() == message.Window
            && NativeMethods.IsWindowEnabled(message.Window)
            && NativeMethods.IsWindowVisible(message.Window)
            && NativeMethods.GetKeyState(NativeMethods.VirtualKeyControl) >= 0
            && NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) >= 0
            && NativeMethods.GetKeyState(0x12) >= 0
            && (message.Window == _previousChangeButton || message.Window == _nextChangeButton
                || message.Window == _previousFileButton || message.Window == _nextFileButton
                || message.Window == _diffSearchButton || message.Window == _ignoreWhitespaceButton
                || message.Window == _sideBySideButton || message.Window == _unifiedButton
                || message.Window == _diffSettingsButton))
        {
            _ = NativeMethods.SendMessage(message.Window, ButtonPerformClick, 0, 0);
            return true;
        }
        if (_diffBoundaryDirection == 0)
        {
            return false;
        }
        bool arrow = message.Window == _previousChangeButton || message.Window == _nextChangeButton;
        if (message.MessageId == NativeMethods.WindowMessageKeyDown)
        {
            int key = unchecked((int)message.WordParameter);
            if (key == NativeMethods.VirtualKeyEscape)
            {
                return DismissDiffBoundaryHint();
            }
            if (!arrow || key is not (NativeMethods.VirtualKeyEnter or NativeMethods.VirtualKeySpace))
            {
                DismissDiffBoundaryHint();
            }
        }
        else if ((message.MessageId == NativeMethods.WindowMessageLeftButtonDown && !arrow)
            || message.MessageId is NativeMethods.WindowMessageMouseWheel or NativeMethods.WindowMessageContextMenu)
        {
            DismissDiffBoundaryHint();
        }
        return false;
    }

    internal bool DismissDiffBoundaryHint()
    {
        bool wasVisible = _diffBoundaryDirection != 0;
        _diffBoundaryDirection = 0;
        _diffBoundaryTargetPath = null;
        if (_diffBoundaryHint != 0)
        {
            _ = NativeMethods.ShowWindow(_diffBoundaryHint, NativeMethods.ShowHide);
        }
        return wasVisible;
    }

    private void ShowDiffBoundaryHint(int direction, string? targetPath)
    {
        if (_diffBoundaryHint == 0 || !NativeMethods.IsWindowVisible(_diffHandle))
        {
            return;
        }
        _diffBoundaryDirection = direction;
        _diffBoundaryTargetPath = targetPath;
        string text = targetPath is null
            ? direction < 0 ? UiText.DiffFirstFileBoundary : UiText.DiffLastFileBoundary
            : direction < 0 ? UiText.DiffPreviousFileBoundary : UiText.DiffNextFileBoundary;
        _ = NativeMethods.SetWindowText(_diffBoundaryHint, text);
        _ = NativeAccessibility.SetName(_diffBoundaryHint, text);

        nint editor = (_sideBySide ? _newDiff : _unifiedDiff)?.Handle ?? 0;
        if (editor == 0 || !NativeMethods.GetClientRectangle(_diffHandle, out NativeMethods.Rectangle surface)
            || !NativeMethods.GetWindowRectangle(editor, out NativeMethods.Rectangle editorBounds))
        {
            return;
        }
        NativeMethods.Point origin = new() { X = editorBounds.Left, Y = editorBounds.Top };
        _ = NativeMethods.ScreenToClient(_diffHandle, ref origin);
        nint position = NativeMethods.SendMessage(editor, ScintillaPositionFromLine,
            unchecked((nuint)Math.Max(0, _changeLines[_changeNavigationIndex] - 1)), 0);
        int lineY = checked((int)NativeMethods.SendMessage(editor, ScintillaPointYFromPosition, 0, position));
        int lineHeight = checked((int)NativeMethods.SendMessage(editor, ScintillaTextHeight, 0, 0));
        int inset = NativeTheme.Scale(8);
        int height = NativeTheme.Scale(32);
        int width;
        nint dc = NativeMethods.GetDeviceContext(_diffHandle);
        try
        {
            width = MeasureTextWidth(dc, text, NativeTheme.UiFont) + NativeTheme.Scale(24);
            nint previousFont = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
            NativeMethods.Rectangle textBounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref textBounds,
                NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextCalculateRectangle);
            height = Math.Max(height, textBounds.Bottom - textBounds.Top + NativeTheme.Scale(12));
            _ = NativeMethods.SelectObject(dc, previousFont);
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(_diffHandle, dc);
        }
        width = Math.Min(width, Math.Max(0, surface.Right - inset * 2));
        height = Math.Min(height, Math.Max(0, surface.Bottom - DiffContentTop - inset * 2));
        int left = Math.Clamp(origin.X + inset, inset, Math.Max(inset, surface.Right - width - inset));
        int top = Math.Clamp(origin.Y + lineY + lineHeight + NativeTheme.Scale(4),
            DiffContentTop + inset, Math.Max(DiffContentTop + inset, surface.Bottom - height - inset));
        // 提示是正文内的轻量子控件；不接管焦点、不开消息框，也不改变父窗口几何。
        _ = NativeMethods.SetWindowPosition(_diffBoundaryHint, 0, left, top, width, height,
            NativeMethods.SetWindowPositionNoActivate);
        ApplyRoundedRegion(_diffBoundaryHint, width, height, NativeTheme.Scale(6));
        _ = NativeMethods.ShowWindow(_diffBoundaryHint, NativeMethods.ShowNormal);
        _ = NativeMethods.InvalidateRectangle(_diffBoundaryHint, 0, true);
    }

    private static bool DrawDiffBoundaryHint(NativeMethods.DrawItem item, bool dark)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        PaintRoundedInput(item.DeviceContext, item.ItemRectangle, palette.BorderStrong, palette.Panel);
        NativeMethods.Rectangle text = item.ItemRectangle;
        text.Left += NativeTheme.Scale(12);
        text.Right -= NativeTheme.Scale(12);
        DrawText(item.DeviceContext, NativeMethods.GetWindowTextValue(item.Control), text,
            palette.Text, centered: false, fontWeight: NativeTheme.UiFont);
        return true;
    }
}
