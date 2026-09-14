using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    private const nuint ToolbarButtonSubclassIdentifier = 2;
    private static readonly NativeMethods.SubclassProcedure ToolbarButtonProcedure = HandleToolbarButtonMessage;
    private nint _hoveredToolbarButton;
    private bool _trackingToolbarMouseLeave;

    internal nint HoveredToolbarButtonForTest => _hoveredToolbarButton;
    internal bool ToolbarHoverTrackingForTest => _trackingToolbarMouseLeave;

    private static nint HandleToolbarButtonMessage(nint window, uint message, nuint wordParameter,
        nint longParameter, nuint subclassIdentifier, nuint referenceData)
    {
        NativeDocumentView? instance;
        lock (InstancesGate) Instances.TryGetValue((nint)referenceData, out instance);
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            instance?.ClearToolbarHover(window);
            _ = NativeMethods.RemoveWindowSubclass(window, ToolbarButtonProcedure, subclassIdentifier);
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (instance is null) return result;
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            int x = unchecked((short)(long)longParameter), y = unchecked((short)((long)longParameter >> 16));
            if (!instance._disposed && IsFocusable(window)
                && NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client)
                && x >= 0 && x < client.Right && y >= 0 && y < client.Bottom)
            {
                if (instance._hoveredToolbarButton != window)
                {
                    instance.ClearToolbarHover(instance._hoveredToolbarButton);
                    instance._hoveredToolbarButton = window;
                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                }
                if (!instance._trackingToolbarMouseLeave)
                {
                    NativeMethods.TrackMouseEvent tracking = new()
                    {
                        Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                        Flags = NativeMethods.TrackMouseEventLeave,
                        Window = window,
                    };
                    instance._trackingToolbarMouseLeave = NativeMethods.TrackMouse(ref tracking);
                }
            }
            else instance.ClearToolbarHover(window);
        }
        else if (message == NativeMethods.WindowMessageMouseLeave
            || (message is NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable && wordParameter == 0))
            instance.ClearToolbarHover(window);
        else if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            _ = NativeMethods.InvalidateRectangle(window, 0, false);
        return result;
    }

    private void ClearToolbarHover(nint window)
    {
        if (window == 0 || window != _hoveredToolbarButton) return;
        if (_trackingToolbarMouseLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
                Window = window,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingToolbarMouseLeave = false;
        _hoveredToolbarButton = 0;
        _ = NativeMethods.InvalidateRectangle(window, 0, false);
    }

    private void DrawDocumentActionBackground(NativeMethods.DrawItem item, bool selected = false)
    {
        NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark(_settings.Theme));
        bool disabled = (item.ItemState & NativeMethods.OwnerDrawDisabled) != 0;
        bool highlighted = !disabled && (item.ItemState & (NativeMethods.OwnerDrawSelected | NativeMethods.OwnerDrawHotLight)) != 0;
        nint brush = NativeMethods.CreateSolidBrush(palette.Panel);
        try { _ = NativeMethods.FillRectangle(item.DeviceContext, ref item.ItemRectangle, brush); }
        finally { if (brush != 0) _ = NativeMethods.DeleteObject(brush); }
        if (!disabled && (selected || highlighted))
            NativeTheme.FillRounded(item.DeviceContext, item.ItemRectangle,
                selected ? palette.AccentSoft : palette.Hover, NativeTheme.Scale(10));
        if (!disabled) NativeTheme.DrawToolbarFocus(item, palette);
    }
}
