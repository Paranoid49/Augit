using Augit.Core.Documents;

namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    internal bool HandleJsonErrorShortcut(NativeMethods.Message message)
    {
        if (message.MessageId != NativeMethods.WindowMessageKeyDown
            || message.WordParameter != NativeMethods.VirtualKeyEnter
            || message.Window != _jsonErrorButton || NativeMethods.GetFocus() != _jsonErrorButton
            || !IsFocusable(_jsonErrorButton)) return false;
        _ = NativeMethods.SendMessage(_jsonErrorButton, 0x00F5, 0, 0);
        return true;
    }

    private void UpdateJsonErrorButton(JsonDisplayResult formatted)
    {
        _ = NativeMethods.EnableWindow(_alternativeButton, formatted.IsValid);
        _modeToolTip?.Update(_alternativeButton, formatted.IsValid ? UiText.Formatted : UiText.JsonFormatUnavailable);
        if (!formatted.IsValid && _jsonErrorButton == 0)
        {
            _jsonErrorButton = CreateButton(string.Empty, CommandJsonError, false);
            NativeTheme.ApplyToControl(_jsonErrorButton, NativeTheme.IsDark(_settings.Theme));
        }
        if (_jsonErrorButton == 0) return;

        // 错误已修复时不把焦点留给即将隐藏的按钮；其他输入上下文保持。
        if (formatted.IsValid && NativeMethods.GetFocus() == _jsonErrorButton)
            _ = NativeMethods.SetFocus(_originalEditor!.Handle);
        _ = NativeMethods.SetWindowText(_jsonErrorButton, formatted.IsValid ? string.Empty
            : UiText.JsonError(formatted.ErrorLine, formatted.ErrorColumn) + UiText.JsonLocateError);
        _ = NativeMethods.ShowWindow(_jsonErrorButton, formatted.IsValid ? NativeMethods.ShowHide : NativeMethods.ShowWithoutActivate);
        _ = NativeMethods.InvalidateRectangle(_jsonErrorButton, 0, false);
        Layout();
    }

    private int MeasureJsonErrorHeight(int width)
        => MeasureDocumentNotice(_jsonErrorButton, width, 32);

    private void DrawJsonError(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        bool focused = NativeMethods.GetFocus() == _jsonErrorButton;
        FillJsonRectangle(item, item.ItemRectangle, focused ? palette.Accent : palette.Border);
        NativeMethods.Rectangle inside = item.ItemRectangle;
        if (focused) { inside.Left++; inside.Top++; inside.Right--; }
        inside.Bottom--;
        ClampRectangle(ref inside);
        FillJsonRectangle(item, inside, (item.ItemState & NativeMethods.OwnerDrawSelected) != 0 ? palette.Hover : palette.Panel);
        NativeMethods.Rectangle textRectangle = item.ItemRectangle;
        int padding = NativeTheme.Scale(8);
        textRectangle.Left += padding;
        textRectangle.Right = Math.Max(textRectangle.Left, textRectangle.Right - padding);
        textRectangle.Top += padding;
        textRectangle.Bottom = Math.Max(textRectangle.Top, textRectangle.Bottom - padding);
        nint previous = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
        _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(item.DeviceContext, palette.Danger);
        string text = NativeMethods.GetWindowTextValue(_jsonErrorButton);
        _ = NativeMethods.DrawText(item.DeviceContext, text, text.Length, ref textRectangle,
            NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
        if (previous != 0) _ = NativeMethods.SelectObject(item.DeviceContext, previous);
    }

    private static void FillJsonRectangle(NativeMethods.DrawItem item, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush == 0) return;
        _ = NativeMethods.FillRectangle(item.DeviceContext, ref rectangle, brush);
        _ = NativeMethods.DeleteObject(brush);
    }

    private static void ClampRectangle(ref NativeMethods.Rectangle rectangle)
    {
        rectangle.Right = Math.Max(rectangle.Left, rectangle.Right);
        rectangle.Bottom = Math.Max(rectangle.Top, rectangle.Bottom);
    }
}
