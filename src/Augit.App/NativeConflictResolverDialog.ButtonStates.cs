using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog
{
    private const nuint ActionButtonSubclassIdentifier = 1;
    private static readonly NativeMethods.SubclassProcedure ActionButtonProcedure = HandleActionButtonMessage;
    private nint _hoveredActionButton;
    private bool _trackingActionMouseLeave;

    internal nint HoveredActionButtonForTest => _hoveredActionButton;
    internal bool ActionHoverTrackingForTest => _trackingActionMouseLeave;

    private static nint HandleActionButtonMessage(nint window, uint message, nuint wordParameter,
        nint longParameter, nuint subclassIdentifier, nuint referenceData)
    {
        NativeConflictResolverDialog? instance;
        lock (InstancesGate) Instances.TryGetValue((nint)referenceData, out instance);
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            instance?.ClearActionHover(window);
            _ = NativeMethods.RemoveWindowSubclass(window, ActionButtonProcedure, subclassIdentifier);
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is null) return result;
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            int x = unchecked((short)(long)longParameter), y = unchecked((short)((long)longParameter >> 16));
            if (!instance._closed && !instance._disposed && NativeMethods.IsWindowVisible(window)
                && NativeMethods.IsWindowEnabled(window) && NativeMethods.IsWindowEnabled(instance._handle)
                && NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client)
                && x >= 0 && x < client.Right && y >= 0 && y < client.Bottom)
            {
                if (instance._hoveredActionButton != window)
                {
                    instance.ClearActionHover(instance._hoveredActionButton);
                    instance._hoveredActionButton = window;
                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                }
                if (!instance._trackingActionMouseLeave)
                {
                    NativeMethods.TrackMouseEvent tracking = new()
                    {
                        Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                        Flags = NativeMethods.TrackMouseEventLeave,
                        Window = window,
                    };
                    instance._trackingActionMouseLeave = NativeMethods.TrackMouse(ref tracking);
                }
            }
            else instance.ClearActionHover(window);
        }
        else if (message == NativeMethods.WindowMessageMouseLeave
            || (message is NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable && wordParameter == 0))
            instance.ClearActionHover(window);
        else if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            _ = NativeMethods.InvalidateRectangle(window, 0, false);
        return result;
    }

    private void ClearActionHover(nint window)
    {
        if (window == 0 || window != _hoveredActionButton) return;
        if (_trackingActionMouseLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
                Window = window,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingActionMouseLeave = false;
        _hoveredActionButton = 0;
        _ = NativeMethods.InvalidateRectangle(window, 0, false);
    }

    private bool DrawActionButton(NativeMethods.DrawItem item)
    {
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool pressed = !disabled && (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        bool hot = !disabled && item.Control == _hoveredActionButton;
        bool primary = item.ControlIdentifier == CommandSave;
        bool close = item.ControlIdentifier == CommandHeaderClose;
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        uint background = disabled ? close ? palette.Panel : palette.PanelMuted
            : primary ? palette.Accent : hot || pressed ? palette.Hover : palette.Panel;
        Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        NativeMethods.Rectangle inner = item.ItemRectangle;
        int radius = S(5);
        if (!close && !primary)
        {
            NativeTheme.FillRounded(item.DeviceContext, inner, palette.BorderStrong, radius * 2);
            int border = Math.Max(1, S(1));
            inner.Left += border;
            inner.Top += border;
            inner.Right -= border;
            inner.Bottom -= border;
            radius = Math.Max(0, radius - border);
        }
        NativeTheme.FillRounded(item.DeviceContext, inner, background, radius * 2);
        DrawText(item.DeviceContext, NativeMethods.GetWindowTextValue(item.Control), item.ItemRectangle,
            disabled ? palette.Faint : primary ? 0xFFFFFFu : palette.Text,
            NativeTheme.UiFont, centered: true);
        if (close)
            _ = NativeTheme.DrawTabCloseIcon(item.DeviceContext,
                (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2,
                (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2,
                disabled ? palette.Faint : hot || pressed || NativeMethods.GetFocus() == item.Control ? palette.Text : palette.Muted);
        if (!disabled && NativeMethods.GetFocus() == item.Control)
        {
            item.ItemState |= NativeMethods.OwnerDrawFocus;
            NativeTheme.DrawToolbarFocus(item, palette);
            if (primary)
            {
                // 主要动作蓝底与焦点同色，外加一像素中性环，焦点不改变按钮尺寸。
                NativeMethods.Rectangle edge = item.ItemRectangle;
                edge.Left += S(1);
                edge.Top += S(1);
                edge.Right -= S(2);
                edge.Bottom -= S(2);
                int inset = Math.Max(1, S(2) - S(1));
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Right, Bottom = edge.Top + inset }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Bottom - inset, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Left, Top = edge.Top, Right = edge.Left + inset, Bottom = edge.Bottom }, palette.Panel);
                Fill(item.DeviceContext, new() { Left = edge.Right - inset, Top = edge.Top, Right = edge.Right, Bottom = edge.Bottom }, palette.Panel);
            }
        }
        return true;
    }
}
