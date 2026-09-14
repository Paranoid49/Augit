using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class MainWindow
{
    private nint _hoveredTreeItem;
    private NativeMethods.Point _treeHoverPoint;
    private bool _trackingTreeMouseLeave;

    internal string? HoveredTreePathForTest => GetTreeNodeFromItem(_hoveredTreeItem)?.FullPath;
    internal bool TreeHoverTrackingForTest => _trackingTreeMouseLeave;

    private void HandleTreeHoverMessage(uint message, nuint wordParameter, nint longParameter)
    {
        if (message == NativeMethods.WindowMessageMouseMove)
        {
            if (!NativeMethods.IsWindowVisible(_fileTree) || !NativeMethods.IsWindowEnabled(_fileTree))
                return;
            _treeHoverPoint = new() { X = unchecked((short)(long)longParameter), Y = unchecked((short)((long)longParameter >> 16)) };
            if (!_trackingTreeMouseLeave)
            {
                NativeMethods.TrackMouseEvent tracking = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                    Flags = NativeMethods.TrackMouseEventLeave,
                    Window = _fileTree,
                };
                _trackingTreeMouseLeave = NativeMethods.TrackMouse(ref tracking);
            }
            UpdateTreeHover();
        }
        else if (message == NativeMethods.WindowMessageMouseLeave
            || (message == NativeMethods.WindowMessageShowWindow && wordParameter == 0))
        {
            ClearTreeHover();
        }
        else if (_trackingTreeMouseLeave && message is
            NativeMethods.WindowMessageMouseWheel or NativeMethods.WindowMessageVerticalScroll
            or NativeMethods.WindowMessageKeyDown or NativeMethods.WindowMessageSize
            or NativeMethods.TreeViewSelectItem or NativeMethods.TreeViewExpand
            or NativeMethods.TreeViewInsertItem or NativeMethods.TreeViewDeleteItem
            or NativeMethods.TreeViewSetItemHeight)
        {
            // 滚动和目录更新完成后，命中仍停在原位置的指针，不让悬停跟着旧节点移动。
            UpdateTreeHover();
        }
    }

    private void UpdateTreeHover()
    {
        nint item = 0;
        if (NativeMethods.GetClientRectangle(_fileTree, out NativeMethods.Rectangle client)
            && _treeHoverPoint.X >= 0 && _treeHoverPoint.X < client.Right
            && _treeHoverPoint.Y >= 0 && _treeHoverPoint.Y < client.Bottom)
        {
            NativeMethods.TreeViewHitTestInfo hit = new() { Point = _treeHoverPoint };
            _ = NativeMethods.SendMessage(_fileTree, NativeMethods.TreeViewHitTest, 0, ref hit);
            if (GetTreeNodeFromItem(hit.Item) is not null)
                item = hit.Item;
        }
        SetTreeHoverItem(item);
    }

    private void SetTreeHoverItem(nint item)
    {
        if (_hoveredTreeItem == item)
            return;
        nint previous = _hoveredTreeItem;
        _hoveredTreeItem = item;
        InvalidateTreeHoverRow(previous);
        InvalidateTreeHoverRow(item);
    }

    private void InvalidateTreeHoverRow(nint item)
    {
        if (item == 0 || !TryGetTreeItemRectangle(item, out NativeMethods.Rectangle row)
            || !NativeMethods.GetClientRectangle(_fileTree, out NativeMethods.Rectangle client))
            return;
        row.Left = 0;
        row.Right = client.Right;
        row.Top = Math.Max(0, row.Top);
        row.Bottom = Math.Min(client.Bottom, row.Bottom);
        if (row.Bottom > row.Top)
            _ = NativeMethods.InvalidateRectangle(_fileTree, ref row, false);
    }

    private void ClearTreeHover()
    {
        if (_trackingTreeMouseLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
                Window = _fileTree,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingTreeMouseLeave = false;
        SetTreeHoverItem(0);
    }
}
