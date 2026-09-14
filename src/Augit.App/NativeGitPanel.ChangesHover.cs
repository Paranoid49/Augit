using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeGitPanel
{
    private int _hoveredChangeIndex = -1;
    private NativeMethods.Point _changesHoverPoint;
    private bool _trackingChangesMouseLeave;

    internal int HoveredChangeIndexForTest => _hoveredChangeIndex;
    internal bool ChangesHoverTrackingForTest => _trackingChangesMouseLeave;

    private void HandleChangesHoverMessage(uint message, nuint wordParameter, nint longParameter)
    {
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            if (_disposed || !NativeMethods.IsWindowVisible(_changesList) || !NativeMethods.IsWindowEnabled(_changesList))
                return;
            _changesHoverPoint = new()
            {
                X = unchecked((short)(long)longParameter),
                Y = unchecked((short)((long)longParameter >> 16)),
            };
            if (!_trackingChangesMouseLeave)
            {
                NativeMethods.TrackMouseEvent tracking = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                    Flags = NativeMethods.TrackMouseEventLeave,
                    Window = _changesList,
                };
                _trackingChangesMouseLeave = NativeMethods.TrackMouse(ref tracking);
            }
            UpdateChangesHover();
        }
        else if (message is NativeMethods.WindowMessageMouseLeave or NativeMethods.WindowMessageNonClientDestroy
            || (message is NativeMethods.WindowMessageShowWindow or NativeMethods.WindowMessageEnable && wordParameter == 0))
        {
            ClearChangesHover();
        }
        else if (_trackingChangesMouseLeave && !_updatingChangesList && message is
            NativeMethods.WindowMessageMouseWheel or NativeMethods.WindowMessageVerticalScroll
            or NativeMethods.WindowMessageHorizontalScroll or NativeMethods.WindowMessageKeyDown
            or NativeMethods.WindowMessageSize or NativeMethods.ListBoxSetTopIndex
            or NativeMethods.ListBoxSetCurrentSelection or NativeMethods.ListBoxSetItemHeight
            or NativeMethods.ListBoxAddString or NativeMethods.ListBoxInsertString
            or NativeMethods.ListBoxDeleteString or NativeMethods.ListBoxResetContent)
        {
            // 视口和列表改变后按指针位置重新命中，不让悬停跟随旧行索引漂移。
            UpdateChangesHover();
        }
    }

    private void UpdateChangesHover()
    {
        if (!_trackingChangesMouseLeave) return;
        int index = -1;
        if (!_disposed && NativeMethods.IsWindowVisible(_changesList) && NativeMethods.IsWindowEnabled(_changesList)
            && NativeMethods.GetClientRectangle(_changesList, out NativeMethods.Rectangle client)
            && _changesHoverPoint.X >= 0 && _changesHoverPoint.X < client.Right
            && _changesHoverPoint.Y >= 0 && _changesHoverPoint.Y < client.Bottom
            && TryGetChangeEntryHit(PackPoint(_changesHoverPoint.X, _changesHoverPoint.Y), out int hit,
                out NativeMethods.Rectangle row, out _, out _)
            && _changesHoverPoint.Y >= row.Top && _changesHoverPoint.Y < row.Bottom)
        {
            index = hit;
        }
        SetChangesHoverIndex(index);
    }

    private void SetChangesHoverIndex(int index)
    {
        if (_hoveredChangeIndex == index) return;
        int previous = _hoveredChangeIndex;
        _hoveredChangeIndex = index;
        InvalidateChangeEntry(previous);
        InvalidateChangeEntry(index);
    }

    private void ClearChangesHover()
    {
        if (_trackingChangesMouseLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
                Window = _changesList,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingChangesMouseLeave = false;
        SetChangesHoverIndex(-1);
    }
}
