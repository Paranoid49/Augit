using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativePushDialog
{
    private readonly NativeMethods.SubclassProcedure _commitProcedure;
    private int _hoveredCommit = -1;
    private bool _trackingCommitLeave;
    private NativeMethods.Point _commitPoint;

    internal int HoveredCommitForTest => _hoveredCommit;

    private bool DrawCommitRow(NativeMethods.DrawItem item)
    {
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        NativeTheme.Fill(item.DeviceContext, item.ItemRectangle, palette.Panel);
        int index = unchecked((int)item.ItemIdentifier);
        if (index < 0 || index >= _commits.Count) return true;
        bool selected = (item.ItemState & NativeMethods.OwnerDrawSelected) != 0;
        if (selected || index == _hoveredCommit)
            _ = NativeGdiPlusDrawing.FillRoundedRectangle(item.DeviceContext, item.ItemRectangle,
                selected ? NativeTheme.SelectionColor(palette, NativeMethods.GetFocus() == _commitList) : palette.Hover, S(10));
        NativeMethods.Rectangle mark = item.ItemRectangle;
        mark.Left += S(20);
        mark.Right = mark.Left + S(16);
        _ = NativeTheme.DrawMenuCheckmark(item.DeviceContext, mark, palette.Muted);
        NativeMethods.Rectangle text = item.ItemRectangle;
        text.Left = mark.Right + S(6);
        text.Right -= S(7);
        DrawText(item.DeviceContext, _commits[index].Subject, text, palette.Text, NativeTheme.UiFont);
        return true;
    }

    private nint HandleCommitListMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            ClearCommitHover();
            _ = NativeMethods.RemoveWindowSubclass(window, _commitProcedure, id);
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        if (message == NativeMethods.WindowMessageMouseMove && !_closed
            && NativeMethods.IsWindowEnabled(window) && NativeMethods.IsWindowVisible(window))
        {
            _commitPoint = new() { X = unchecked((short)(long)parameter), Y = unchecked((short)((long)parameter >> 16)) };
            if (!_trackingCommitLeave)
            {
                NativeMethods.TrackMouseEvent tracking = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                    Flags = NativeMethods.TrackMouseEventLeave,
                    Window = window,
                };
                _trackingCommitLeave = NativeMethods.TrackMouse(ref tracking);
            }
            UpdateCommitHover();
        }
        else if (message == NativeMethods.WindowMessageMouseLeave
            || ((message is NativeMethods.WindowMessageEnable or NativeMethods.WindowMessageShowWindow) && word == 0))
            ClearCommitHover();
        else if (_trackingCommitLeave && message is NativeMethods.WindowMessageMouseWheel or NativeMethods.WindowMessageVerticalScroll
            or NativeMethods.WindowMessageKeyDown or NativeMethods.WindowMessageSize or NativeMethods.ListBoxSetTopIndex
            or NativeMethods.ListBoxSetCurrentSelection or NativeMethods.ListBoxSetItemHeight or NativeMethods.ListBoxResetContent)
            UpdateCommitHover();
        if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
        {
            int selected = unchecked((int)NativeMethods.SendMessage(window, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
            InvalidateCommitRow(selected);
        }
        return result;
    }

    private void UpdateCommitHover()
    {
        int index = -1;
        if (NativeMethods.GetClientRectangle(_commitList, out var client)
            && _commitPoint.X >= 0 && _commitPoint.X < client.Right && _commitPoint.Y >= 0 && _commitPoint.Y < client.Bottom)
        {
            nuint hit = unchecked((nuint)NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxItemFromPoint, 0,
                (nint)((_commitPoint.Y << 16) | (_commitPoint.X & 0xffff))));
            int candidate = NativeMethods.LowWord(hit);
            NativeMethods.Rectangle row = default;
            if (NativeMethods.HighWord(hit) == 0 && candidate < _commits.Count
                && NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxGetItemRectangle, (nuint)candidate, ref row) != -1
                && _commitPoint.Y >= row.Top && _commitPoint.Y < row.Bottom) index = candidate;
        }
        SetCommitHover(index);
    }

    private void SetCommitHover(int index)
    {
        if (_hoveredCommit == index) return;
        int previous = _hoveredCommit;
        _hoveredCommit = index;
        InvalidateCommitRow(previous);
        InvalidateCommitRow(index);
    }

    private void InvalidateCommitRow(int index)
    {
        NativeMethods.Rectangle row = default;
        if (index >= 0 && NativeMethods.SendMessage(_commitList, NativeMethods.ListBoxGetItemRectangle, (nuint)index, ref row) != -1)
            _ = NativeMethods.InvalidateRectangle(_commitList, ref row, false);
    }

    private void ClearCommitHover()
    {
        if (_trackingCommitLeave)
        {
            NativeMethods.TrackMouseEvent tracking = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.TrackMouseEvent>(),
                Window = _commitList,
                Flags = NativeMethods.TrackMouseEventLeave | NativeMethods.TrackMouseEventCancel,
            };
            _ = NativeMethods.TrackMouse(ref tracking);
        }
        _trackingCommitLeave = false;
        SetCommitHover(-1);
    }
}
