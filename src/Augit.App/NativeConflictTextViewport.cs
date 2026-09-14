using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

// 首行前的展示留白由容器承载；Scintilla 始终保存原文，普通冲突仍使用其原生滚动条。
internal sealed class NativeConflictTextViewport : IDisposable
{
    private const nuint SubclassId = 71;
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, NativeConflictTextViewport> Instances = [];
    private static readonly NativeMethods.SubclassProcedure Procedure = HandleMessage;
    private readonly nint _owner;
    private bool _disposed, _updating;
    private int _leadingLines, _firstVisibleLine, _wheelRemainder;
    private uint _background;
    private (int Top, int Width, int Height)? _editorBounds;

    internal NativeConflictTextViewport(nint owner, int identifier)
    {
        _owner = owner;
        Handle = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleClipChildren
            | NativeMethods.WindowStyleClipSiblings | NativeMethods.StaticNotify,
            0, 0, 0, 0, owner, identifier + 100, NativeMethods.GetModuleHandle(null), 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建冲突正文视口。");
        try
        {
            Register(Handle);
            Editor = new(Handle, identifier);
            Register(Editor.Handle);
        }
        catch { Dispose(); throw; }
    }

    internal nint Handle { get; private set; }
    internal ScintillaControl Editor { get; } = null!;
    internal Action? ScrollChanged { get; set; }
    internal int FirstVisibleLine => _leadingLines > 0 ? _firstVisibleLine : Editor.FirstVisibleLine;
    internal int LeadingLines => _leadingLines;
    internal int LayoutCountForTest { get; private set; }
    internal static int RegistrationCountForTest { get { lock (Gate) return Instances.Count; } }

    private void Register(nint window)
    {
        lock (Gate) Instances.Add(window, this);
        if (!NativeMethods.SetWindowSubclass(window, Procedure, SubclassId, 0))
        {
            lock (Gate) Instances.Remove(window);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法连接冲突正文视口。");
        }
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        Refresh();
    }

    internal void ApplyAppearance(bool dark)
    {
        _background = NativeTheme.Palette(dark).Panel;
        _ = NativeMethods.SetWindowTheme(Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        NativeScrollBarCornerTheme.Apply(Handle, _background);
        Refresh();
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal void SetLeadingLines(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_leadingLines == count) return;
        int first = FirstVisibleLine;
        _leadingLines = count;
        _firstVisibleLine = first;
        _wheelRemainder = 0;
        _updating = true;
        try
        {
            Editor.SetScrollBarsVisible(horizontal: count == 0, vertical: count == 0);
            _ = NativeMethods.SendMessage(Editor.Handle, 2277, count == 0 ? 1U : 0U, 0); // SCI_SETENDATLASTLINE。
            if (count == 0)
            {
                SetBar(0, 0, 1, 0);
                SetBar(1, 0, 1, 0);
                Editor.SetFirstVisibleLine(first);
            }
        }
        finally { _updating = false; }
        Refresh();
    }

    internal void SetFirstVisibleLine(int line)
    {
        if (_leadingLines == 0) { Editor.SetFirstVisibleLine(line); return; }
        int before = _firstVisibleLine;
        _firstVisibleLine = Math.Max(0, line);
        Refresh();
        if (_firstVisibleLine != before) ScrollChanged?.Invoke();
    }

    internal void Refresh()
    {
        if (_disposed || _updating || Editor is null || Handle == 0 || !NativeMethods.IsWindow(Handle)) return;
        _updating = true;
        try
        {
            _ = NativeMethods.GetClientRectangle(Handle, out var client);
            if (_leadingLines == 0)
            {
                LayoutEditor(0, client.Right, client.Bottom);
                return;
            }
            // 滚动条出现会改变客户区尺寸；最多再算两次即可收敛两条滚动条。
            int requestedFirst = _firstVisibleLine;
            for (int pass = 0; pass < 3; pass++)
            {
                int page = Math.Max(1, client.Bottom / LineHeight);
                int total = DisplayLineCount;
                _firstVisibleLine = Math.Clamp(requestedFirst, 0, Math.Max(0, total - page));
                SetBar(1, total - 1, page, _firstVisibleLine);
                int width = (int)NativeMethods.SendMessage(Editor.Handle, 2275, 0, 0);
                var padding = Editor.TextPaddingForTest;
                int contentWidth = Math.Max(1, width + padding.Left + padding.Right);
                int x = Math.Clamp((int)NativeMethods.SendMessage(Editor.Handle, 2398, 0, 0),
                    0, Math.Max(0, contentWidth - client.Right));
                SetBar(0, contentWidth - 1, Math.Max(1, client.Right), x);
                _ = NativeMethods.SendMessage(Editor.Handle, 2397, (nuint)x, 0);
                _ = NativeMethods.GetClientRectangle(Handle, out var next);
                if (next.Right == client.Right && next.Bottom == client.Bottom) break;
                client = next;
            }
            int gap = (int)Math.Min(client.Bottom, (long)Math.Max(0, _leadingLines - _firstVisibleLine) * LineHeight);
            LayoutEditor(gap, client.Right, Math.Max(1, client.Bottom - gap));
            Editor.SetFirstVisibleLine(Math.Max(0, _firstVisibleLine - _leadingLines));
        }
        finally { _updating = false; }
    }

    private void LayoutEditor(int top, int width, int height)
    {
        if (_editorBounds == (top, width, height)) return;
        _editorBounds = (top, width, height);
        LayoutCountForTest++;
        Editor.SetBounds(0, top, width, height);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    private int LineHeight => Math.Max(1, (int)NativeMethods.SendMessage(Editor.Handle, 2279, 0, 0));
    private int PageLines
    {
        get { _ = NativeMethods.GetClientRectangle(Handle, out var client); return Math.Max(1, client.Bottom / LineHeight); }
    }
    private int DisplayLineCount
    {
        get
        {
            int last = Math.Max(0, (int)NativeMethods.SendMessage(Editor.Handle, 2154, 0, 0) - 1);
            int display = (int)NativeMethods.SendMessage(Editor.Handle, 2220, (nuint)last, 0);
            int visible = NativeMethods.SendMessage(Editor.Handle, 2228, (nuint)last, 0) != 0 ? 1 : 0;
            int annotations = visible == 0 ? 0 : (int)NativeMethods.SendMessage(Editor.Handle, 2546, (nuint)last, 0);
            return Math.Max(1, _leadingLines + display + visible + annotations);
        }
    }

    private void NativeScrolled(nint notification)
    {
        if (_updating || !Editor.IsVerticalScrollNotification(notification)) return;
        if (_leadingLines > 0)
        {
            int native = Editor.FirstVisibleLine;
            if (native == Math.Max(0, _firstVisibleLine - _leadingLines)) return;
            _firstVisibleLine = _leadingLines + native;
            Refresh();
        }
        ScrollChanged?.Invoke();
    }

    private void Wheel(nuint word)
    {
        _wheelRemainder += unchecked((short)(word >> 16));
        int steps = _wheelRemainder / 120;
        _wheelRemainder %= 120;
        if (steps == 0) return;
        uint lines = 3;
        _ = SystemParametersInfo(0x0068, 0, ref lines, 0); // 只读取 Windows 的滚轮行数设置。
        long amount = lines == uint.MaxValue ? PageLines : lines;
        SetFirstVisibleLine((int)Math.Clamp(_firstVisibleLine - steps * amount, 0, int.MaxValue));
    }

    private void ScrollBar(int bar, int command)
    {
        ScrollInfo info = new() { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x17 };
        _ = GetScrollInfo(Handle, bar, ref info);
        int step = bar == 1 ? 1 : Math.Max(1, Editor.MeasureTextWidth("M"));
        long next = command switch
        {
            0 => (long)info.Position - step,
            1 => (long)info.Position + step,
            2 => (long)info.Position - info.Page,
            3 => (long)info.Position + info.Page,
            4 or 5 => info.TrackPosition,
            6 => 0,
            7 => info.Maximum,
            _ => info.Position,
        };
        int target = (int)Math.Clamp(next, 0, Math.Max(0L, (long)info.Maximum - info.Page + 1));
        if (bar == 1) SetFirstVisibleLine(target);
        else { _ = NativeMethods.SendMessage(Editor.Handle, 2397, (nuint)target, 0); Refresh(); }
    }

    private void SetBar(int bar, int maximum, int page, int position)
    {
        ScrollInfo info = new()
        {
            Size = (uint)Marshal.SizeOf<ScrollInfo>(),
            Mask = 7,
            Maximum = Math.Max(0, maximum),
            Page = (uint)Math.Max(1, page),
            Position = position,
        };
        ScrollInfo previous = new() { Size = info.Size, Mask = 7 };
        if (!GetScrollInfo(Handle, bar, ref previous) || previous.Maximum != info.Maximum
            || previous.Page != info.Page || previous.Position != info.Position)
            _ = SetScrollInfo(Handle, bar, ref info, true);
        // 范围存在不等于滚动条可见；明确匹配当前溢出状态，普通视口不叠加第二组滚动条。
        bool needed = _leadingLines > 0 && info.Maximum >= info.Page;
        uint flag = bar == 0 ? NativeMethods.WindowStyleHorizontalScroll : NativeMethods.WindowStyleVerticalScroll;
        bool visible = (unchecked((uint)NativeMethods.GetWindowLongPointer(Handle, NativeMethods.WindowLongStyle)) & flag) != 0;
        if (visible != needed) _ = ShowScrollBar(Handle, bar, needed);
    }

    private static nint HandleMessage(nint window, uint message, nuint word, nint value, nuint id, nuint data)
    {
        NativeConflictTextViewport? view;
        lock (Gate) Instances.TryGetValue(window, out view);
        if (view is null) return NativeMethods.DefaultSubclassProcedure(window, message, word, value);
        if (message == 0x0082)
        {
            lock (Gate) Instances.Remove(window);
            _ = NativeMethods.RemoveWindowSubclass(window, Procedure, SubclassId);
            return NativeMethods.DefaultSubclassProcedure(window, message, word, value);
        }
        bool host = window == view.Handle;
        if (view._leadingLines > 0 && message == NativeMethods.WindowMessageMouseWheel && (word & 12) == 0)
        {
            view.Wheel(word);
            return 0;
        }
        if (host)
        {
            switch (message)
            {
                case NativeMethods.WindowMessageNotify:
                    nint forwarded = NativeMethods.SendMessage(view._owner, message, word, value);
                    view.NativeScrolled(value);
                    return forwarded;
                case NativeMethods.WindowMessageSize:
                    view.Refresh();
                    return 0;
                case NativeMethods.WindowMessageVerticalScroll when view._leadingLines > 0:
                case NativeMethods.WindowMessageHorizontalScroll when view._leadingLines > 0:
                    view.ScrollBar(message == NativeMethods.WindowMessageVerticalScroll ? 1 : 0, NativeMethods.LowWord(word));
                    return 0;
                case NativeMethods.WindowMessageLeftButtonDown:
                    _ = NativeMethods.SetFocus(view.Editor.Handle);
                    return 0;
                case NativeMethods.WindowMessageEraseBackground:
                    return 1;
                case NativeMethods.WindowMessagePaint:
                    nint dc = NativeMethods.BeginPaint(window, out var paint);
                    try { NativeTheme.Fill(dc, paint.PaintRectangle, view._background); }
                    finally { _ = NativeMethods.EndPaint(window, ref paint); }
                    return 0;
                case 0x0318: // WM_PRINTCLIENT；审计截图使用同一背景。
                    _ = NativeMethods.GetClientRectangle(window, out var client);
                    NativeTheme.Fill(unchecked((nint)word), client, view._background);
                    return 0;
            }
        }
        int? scrollAfterKey = null;
        if (!host && view._leadingLines > 0 && message == NativeMethods.WindowMessageKeyDown
            && NativeMethods.GetKeyState(0x12) >= 0)
        {
            bool control = NativeMethods.GetKeyState(0x11) < 0;
            scrollAfterKey = (int)word switch
            {
                0x24 when control => 0,
                0x23 when control => int.MaxValue,
                0x21 => Math.Max(0, view._firstVisibleLine - view.PageLines),
                0x22 => view._firstVisibleLine + view.PageLines,
                _ => null,
            };
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, value);
        if (scrollAfterKey is { } line) view.SetFirstVisibleLine(line);
        if (!host && view._leadingLines > 0 && message is NativeMethods.WindowMessagePaint or 0x0318)
            view.Refresh();
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ScrollChanged = null;
        Editor?.Dispose();
        if (Handle != 0 && NativeMethods.IsWindow(Handle)) _ = NativeMethods.DestroyWindow(Handle);
        Handle = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        internal uint Size, Mask;
        internal int Minimum, Maximum;
        internal uint Page;
        internal int Position, TrackPosition;
    }

    [DllImport("user32.dll")]
    private static extern int SetScrollInfo(nint window, int bar, ref ScrollInfo info, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll", EntryPoint = "ShowScrollBar")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(nint window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref uint value, uint flags);
}
