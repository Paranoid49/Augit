using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativePushDialog
{
    private readonly NativeMethods.SubclassProcedure _detailProcedure;
    private nint _detailPanel;
    private string _notice = string.Empty;
    private bool _noticeError, _layingOutDetails;
    private int _detailMeasureWidth, _detailOffset, _detailMaximum, _wheelRemainder;
    private int _titleTextHeight, _targetTextHeight, _credentialsTextHeight, _noticeTextHeight, _emptyTextHeight;

    internal nint DetailPanelForTest => _detailPanel;

    private int ContentBottom(int height, int top)
    {
        // 无远端的标签选项也占用正文预算；窗口限高时先缩短列表，不能压住固定动作。
        int tags = _noRemoteLayout ? Math.Max(S(30), _rowHeight + S(3)) : 0;
        return Math.Max(top, Math.Min(height - _footerHeight - _bodyInset - tags, top + _contentHeight));
    }

    private static int MeasureWrappedText(nint control, int width)
    {
        string text = NativeMethods.GetWindowTextValue(control);
        if (text.Length == 0) return 0;
        nint dc = NativeMethods.GetDeviceContext(control);
        nint font = NativeMethods.SendMessage(control, 0x0031, 0, 0); // WM_GETFONT。
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle rectangle = new() { Right = Math.Max(1, width) };
            _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextWordBreak | NativeMethods.DrawTextNoPrefix);
            return Math.Max(NativeTheme.UiLineHeight, rectangle.Bottom - rectangle.Top);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(control, dc);
        }
    }

    private void SetNotice(string message, bool error = false)
    {
        if (_closed || (_notice == message && _noticeError == error)) return;
        _notice = message;
        _noticeError = error;
        _detailMeasureWidth = 0;
        _ = NativeMethods.SetWindowText(_noticeLabel, message);
        _toolTip?.Update(_noticeLabel, message);
        _detailOffset = 0;
        LayoutDetails();
    }

    private void LayoutDetails()
    {
        if (_layingOutDetails || _detailPanel == 0 || !NativeMethods.GetClientRectangle(_detailPanel, out var client)) return;
        _layingOutDetails = true;
        try
        {
            int noticeTop = 0;
            bool empty = _previewLoadCompleted && _preview is null;
            for (int pass = 0; pass < 2; pass++)
            {
                int width = Math.Max(1, client.Right - S(36));
                if (_detailMeasureWidth != width)
                {
                    _titleTextHeight = Math.Max(_rowHeight, MeasureWrappedText(_detailTitleLabel, width));
                    _targetTextHeight = Math.Max(_rowHeight, MeasureWrappedText(_targetLabel, width));
                    _credentialsTextHeight = Math.Max(S(42), MeasureWrappedText(_credentialsLabel, width));
                    _noticeTextHeight = MeasureWrappedText(_noticeLabel, width);
                    _emptyTextHeight = MeasureWrappedText(_emptyStateLabel, width);
                    _detailMeasureWidth = width;
                }
                noticeTop = S(18) + _titleTextHeight + S(8) + _targetTextHeight + S(8) + _credentialsTextHeight + S(16);
                int contentHeight = empty ? _emptyTextHeight + S(36) : noticeTop + _noticeTextHeight + S(18);
                _detailMaximum = Math.Max(0, contentHeight - client.Bottom);
                _detailOffset = Math.Clamp(_detailOffset, 0, _detailMaximum);
                DetailScrollInfo scroll = new()
                {
                    Size = (uint)Marshal.SizeOf<DetailScrollInfo>(),
                    Mask = 7,
                    Maximum = Math.Max(0, contentHeight - 1),
                    Page = (uint)Math.Max(0, client.Bottom),
                    Position = _detailOffset,
                };
                _ = SetDetailScrollInfo(_detailPanel, 1, ref scroll, true);
                _ = NativeMethods.GetClientRectangle(_detailPanel, out client);
                if (Math.Max(1, client.Right - S(36)) == width) break;
            }
            int textWidth = Math.Max(1, client.Right - S(36));
            int y = S(18) - _detailOffset;
            Move(_detailTitleLabel, S(18), y, textWidth, _titleTextHeight);
            y += _titleTextHeight + S(8);
            Move(_targetLabel, S(18), y, textWidth, _targetTextHeight);
            y += _targetTextHeight + S(8);
            Move(_credentialsLabel, S(18), y, textWidth, _credentialsTextHeight);
            Move(_noticeLabel, S(18), noticeTop - _detailOffset, textWidth, _noticeTextHeight);
            Move(_emptyStateLabel, S(18), Math.Max(S(18), (client.Bottom - _emptyTextHeight) / 2) - _detailOffset, textWidth, _emptyTextHeight);
            _ = NativeMethods.ShowWindow(_noticeLabel, !empty && _notice.Length != 0 ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            _ = NativeMethods.InvalidateRectangle(_detailPanel, 0, true);
        }
        finally { _layingOutDetails = false; }
    }

    private void ScrollDetails(int offset)
    {
        int next = Math.Clamp(offset, 0, _detailMaximum);
        if (_detailOffset == next) return;
        _detailOffset = next;
        LayoutDetails();
    }

    private nint HandleDetailMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            _ = NativeMethods.RemoveWindowSubclass(window, _detailProcedure, id);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        if (message == NativeMethods.WindowMessageMouseWheel)
        {
            _wheelRemainder += unchecked((short)NativeMethods.HighWord(word));
            int steps = _wheelRemainder / 120;
            _wheelRemainder %= 120;
            ScrollDetails(_detailOffset - steps * _rowHeight * 3);
            return 0;
        }
        if (window == _detailPanel)
        {
            if (message == NativeMethods.WindowMessageKeyDown)
            {
                _ = NativeMethods.GetClientRectangle(window, out var bounds);
                int next = (int)word switch
                {
                    NativeMethods.VirtualKeyUp => _detailOffset - _rowHeight,
                    NativeMethods.VirtualKeyDown => _detailOffset + _rowHeight,
                    0x21 => _detailOffset - bounds.Bottom,
                    0x22 => _detailOffset + bounds.Bottom,
                    0x24 => 0,
                    0x23 => _detailMaximum,
                    _ => _detailOffset,
                };
                ScrollDetails(next);
            }
            if (message == NativeMethods.WindowMessageVerticalScroll)
            {
                _ = NativeMethods.GetClientRectangle(window, out var bounds);
                DetailScrollInfo scroll = new() { Size = (uint)Marshal.SizeOf<DetailScrollInfo>(), Mask = 0x10 };
                _ = GetDetailScrollInfo(window, 1, ref scroll);
                ScrollDetails(NativeMethods.LowWord(word) switch
                {
                    0 => _detailOffset - _rowHeight,
                    1 => _detailOffset + _rowHeight,
                    2 => _detailOffset - bounds.Bottom,
                    3 => _detailOffset + bounds.Bottom,
                    4 or 5 => scroll.TrackPosition,
                    6 => 0,
                    7 => _detailMaximum,
                    _ => _detailOffset,
                });
                return 0;
            }
            if (message == NativeMethods.WindowMessageSize) { LayoutDetails(); return 0; }
            if (message is NativeMethods.WindowMessageControlColorStatic) return NativeMethods.SendMessage(_handle, message, word, parameter);
            if (message == NativeMethods.WindowMessagePaint)
            {
                nint dc = NativeMethods.BeginPaint(window, out var paint);
                try
                {
                    _ = NativeMethods.GetClientRectangle(window, out var bounds);
                    NativeTheme.Fill(dc, bounds, NativeTheme.Palette(_dark).Panel);
                }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
                return 0;
            }
            if (message == NativeMethods.WindowMessageEraseBackground) return 1;
        }
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetailScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }

    [DllImport("user32.dll", EntryPoint = "SetScrollInfo")]
    private static extern int SetDetailScrollInfo(nint window, int bar, ref DetailScrollInfo info, bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetScrollInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDetailScrollInfo(nint window, int bar, ref DetailScrollInfo info);
}
