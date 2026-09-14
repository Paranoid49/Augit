using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 操作窗口的局部按钮绘制和鼠标跟踪，不改变原生按钮的点击语义。
/// </summary>
internal sealed class NativeDialogActionButtons : IDisposable
{
    internal enum Style { Secondary, Primary, Danger, Close, Icon }

    private const nuint SubclassId = 73;
    private readonly nint _owner;
    private readonly nint[] _buttons;
    private readonly NativeMethods.SubclassProcedure _procedure;
    private bool _tracking, _disposed;
    internal nint Hovered { get; private set; }

    internal NativeDialogActionButtons(nint owner, params nint[] buttons)
    {
        _owner = owner;
        _buttons = buttons;
        _procedure = HandleMessage;
        try
        {
            foreach (nint window in buttons.Prepend(owner))
                if (!NativeMethods.SetWindowSubclass(window, _procedure, SubclassId, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { Dispose(); throw; }
    }

    private nint HandleMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            if (window == _owner) Dispose();
            else
            {
                Clear(window);
                _ = NativeMethods.RemoveWindowSubclass(window, _procedure, id);
            }
        }
        if (message is NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable && word == 0)
            Clear(window == _owner ? Hovered : window);
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        if (_disposed || window == _owner) return result;
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            int x = unchecked((short)(long)parameter), y = unchecked((short)((long)parameter >> 16));
            if (NativeMethods.IsWindowEnabled(_owner) && NativeMethods.IsWindowEnabled(window)
                && NativeMethods.IsWindowVisible(window) && NativeMethods.GetClientRectangle(window, out var bounds)
                && x >= 0 && y >= 0 && x < bounds.Right && y < bounds.Bottom)
            {
                if (Hovered != window)
                {
                    Clear(Hovered);
                    Hovered = window;
                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                }
                if (!_tracking)
                {
                    NativeMethods.TrackMouseEvent tracking = new()
                    {
                        Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                        Window = window,
                        Flags = NativeMethods.TrackMouseEventLeave,
                    };
                    _tracking = NativeMethods.TrackMouse(ref tracking);
                }
            }
            else Clear(window);
        }
        else if (message == NativeMethods.WindowMessageMouseLeave) Clear(window);
        else if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            _ = NativeMethods.InvalidateRectangle(window, 0, false);
        return result;
    }

    private void Clear(nint window)
    {
        if (window == 0 || window != Hovered) return;
        if (_tracking)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Window = window,
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        Hovered = 0;
        _tracking = false;
        _ = NativeMethods.InvalidateRectangle(window, 0, false);
    }

    internal bool Draw(NativeMethods.DrawItem item, bool dark, Style style)
    {
        NativeThemePalette palette = NativeTheme.Palette(dark);
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0
            || !NativeMethods.IsWindowEnabled(item.Control) || !NativeMethods.IsWindowEnabled(_owner);
        bool hot = !disabled && (Hovered == item.Control || (item.ItemState & NativeMethods.OwnerDrawSelected) != 0);
        bool focused = !disabled && NativeMethods.GetFocus() == item.Control;
        bool filled = style is Style.Primary or Style.Danger;
        uint background = disabled ? style is Style.Close or Style.Icon ? palette.Panel : palette.PanelMuted
            : style == Style.Primary ? palette.Accent : style == Style.Danger ? palette.Danger
            : hot ? palette.Hover : palette.Panel;
        NativeMethods.Rectangle inner = item.ItemRectangle;
        Fill(item.DeviceContext, inner, palette.Panel);
        int radius = NativeTheme.Scale(5), border = Math.Max(1, NativeTheme.Scale(1));
        if (style == Style.Secondary)
        {
            NativeTheme.FillRounded(item.DeviceContext, inner, palette.BorderStrong, radius * 2);
            inner.Left += border; inner.Top += border; inner.Right -= border; inner.Bottom -= border;
            radius = Math.Max(0, radius - border);
        }
        NativeTheme.FillRounded(item.DeviceContext, inner, background, radius * 2);
        if (style == Style.Close)
            _ = NativeTheme.DrawTabCloseIcon(item.DeviceContext,
                (inner.Left + inner.Right) / 2, (inner.Top + inner.Bottom) / 2,
                disabled ? palette.Faint : hot || focused ? palette.Text : palette.Muted);
        else if (style != Style.Icon)
        {
            string text = NativeMethods.GetWindowTextValue(item.Control);
            nint previous = NativeMethods.SelectObject(item.DeviceContext, NativeTheme.UiFont);
            _ = NativeMethods.SetBackgroundMode(item.DeviceContext, NativeMethods.BackgroundModeTransparent);
            _ = NativeMethods.SetTextColor(item.DeviceContext, disabled ? palette.Faint : filled ? 0xFFFFFFu : palette.Text);
            var textBounds = item.ItemRectangle;
            _ = NativeMethods.DrawText(item.DeviceContext, text, text.Length, ref textBounds,
                NativeMethods.DrawTextCenter | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            _ = NativeMethods.SelectObject(item.DeviceContext, previous);
        }
        if (focused)
        {
            item.ItemState |= NativeMethods.OwnerDrawFocus;
            NativeTheme.DrawToolbarFocus(item, palette);
            if (style == Style.Primary)
            {
                // 蓝底上的蓝色焦点框另加中性外环，保持按钮尺寸不变。
                var edge = item.ItemRectangle;
                edge.Left += NativeTheme.Scale(1); edge.Top += NativeTheme.Scale(1);
                edge.Right -= NativeTheme.Scale(2); edge.Bottom -= NativeTheme.Scale(2);
                int stroke = Math.Max(1, NativeTheme.Scale(2) - NativeTheme.Scale(1));
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Right, Bottom = edge.Top + stroke }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Bottom - stroke, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Left + stroke, Bottom = edge.Bottom }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Right - stroke, Top = edge.Top, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
            }
        }
        return true;
    }

    private static void Fill(nint dc, NativeMethods.Rectangle bounds, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush == 0) return;
        _ = NativeMethods.FillRectangle(dc, ref bounds, brush);
        _ = NativeMethods.DeleteObject(brush);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear(Hovered);
        foreach (nint window in _buttons.Prepend(_owner))
            if (NativeMethods.IsWindow(window)) _ = NativeMethods.RemoveWindowSubclass(window, _procedure, SubclassId);
    }
}
