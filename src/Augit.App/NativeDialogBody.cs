using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 紧凑操作窗口的正文裁切与滚动。标题、底栏和业务动作仍由各自窗口持有。
/// </summary>
internal sealed class NativeDialogBody : IDisposable
{
    private const nuint SubclassId = 74;
    private readonly nint _owner;
    private readonly Func<int, int> _measure;
    private readonly Action<int, int, int> _layout;
    private readonly Action<nint> _paint;
    private readonly Func<int, int, bool>? _click;
    private readonly NativeMethods.SubclassProcedure _procedure;
    private readonly Dictionary<nint, bool> _controls = [];
    private bool _layingOut, _disposed;
    private int _maximum, _wheel;

    internal nint Handle { get; }
    internal int Offset { get; private set; }

    internal NativeDialogBody(nint owner, Func<int, int> measure, Action<int, int, int> layout, Action<nint> paint,
        Func<int, int, bool>? click = null)
    {
        _owner = owner; _measure = measure; _layout = layout; _paint = paint; _click = click;
        _procedure = HandleMessage;
        Handle = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible
            | NativeMethods.WindowStyleClipChildren | NativeMethods.WindowStyleVerticalScroll,
            0, 0, 0, 0, owner, 18, NativeMethods.GetModuleHandle(null), 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { Register(Handle, true); }
        catch { _ = NativeMethods.DestroyWindow(Handle); throw; }
    }

    internal void Register(nint control, bool scrollWheel = false)
    {
        if (_controls.ContainsKey(control)) return;
        if (!NativeMethods.SetWindowSubclass(control, _procedure, SubclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _controls.Add(control, scrollWheel);
    }

    internal void Relayout()
    {
        if (_disposed || _layingOut || !NativeMethods.GetClientRectangle(Handle, out var client)) return;
        _layingOut = true;
        try
        {
            // 原生滚动条出现后会占用宽度，最多补算一次换行。
            for (int pass = 0; pass < 2; pass++)
            {
                int content = _measure(Math.Max(1, client.Right));
                _maximum = Math.Max(0, content - client.Bottom);
                Offset = Math.Clamp(Offset, 0, _maximum);
                ScrollInfo info = new()
                {
                    Size = (uint)Marshal.SizeOf<ScrollInfo>(),
                    Mask = 7,
                    Maximum = Math.Max(0, content - 1),
                    Page = (uint)Math.Max(0, client.Bottom),
                    Position = Offset,
                };
                _ = SetScrollInfo(Handle, 1, ref info, true);
                _ = NativeMethods.GetClientRectangle(Handle, out var next);
                if (next.Right == client.Right) break;
                client = next;
            }
            _layout(client.Right, client.Bottom, Offset);
            _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
        }
        finally { _layingOut = false; }
    }

    internal void EnsureVisible(nint control)
    {
        if (_disposed || _layingOut || !NativeMethods.IsChild(Handle, control)
            || !NativeMethods.GetWindowRectangle(control, out var rect)
            || !NativeMethods.GetClientRectangle(Handle, out var client)) return;
        NativeMethods.Point point = new() { X = rect.Left, Y = rect.Top };
        _ = NativeMethods.ScreenToClient(Handle, ref point);
        int height = rect.Bottom - rect.Top;
        if (point.Y < 0 || height >= client.Bottom) SetOffset(Offset + point.Y);
        else if (point.Y + height > client.Bottom) SetOffset(Offset + point.Y + height - client.Bottom);
    }

    private void SetOffset(int value)
    {
        int next = Math.Clamp(value, 0, _maximum);
        if (next == Offset) return;
        Offset = next; Relayout();
    }

    private nint HandleMessage(nint window, uint message, nuint word, nint parameter, nuint id, nuint data)
    {
        if (message == NativeMethods.WindowMessageNonClientDestroy)
        {
            if (window == Handle) Dispose();
            else
            {
                _controls.Remove(window);
                _ = NativeMethods.RemoveWindowSubclass(window, _procedure, id);
            }
            return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        }
        if (_disposed) return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        if (message == NativeMethods.WindowMessageMouseWheel && _controls.GetValueOrDefault(window)
            && NativeMethods.SendMessage(window, 0x0157, 0, 0) == 0)
        {
            _wheel += unchecked((short)NativeMethods.HighWord(word));
            int steps = _wheel / 120; _wheel %= 120;
            SetOffset(Offset - steps * Math.Max(NativeTheme.Scale(30), NativeTheme.UiLineHeight) * 3);
            return 0;
        }
        if (window != Handle)
        {
            nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            if (message == NativeMethods.WindowMessageSetFocus) EnsureVisible(window);
            if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
                _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
            return result;
        }
        switch (message)
        {
            case NativeMethods.WindowMessageSize: Relayout(); return 0;
            case NativeMethods.WindowMessageEraseBackground: return 1;
            case NativeMethods.WindowMessageLeftButtonDown:
                if (_click?.Invoke(unchecked((short)(long)parameter), unchecked((short)((long)parameter >> 16))) == true) return 0;
                break;
            case NativeMethods.WindowMessagePaint:
                nint dc = NativeMethods.BeginPaint(window, out var paint);
                try { _paint(dc); }
                finally { _ = NativeMethods.EndPaint(window, ref paint); }
                return 0;
            case NativeMethods.WindowMessageVerticalScroll:
                _ = NativeMethods.GetClientRectangle(window, out var client);
                ScrollInfo info = new() { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x10 };
                _ = GetScrollInfo(window, 1, ref info);
                int line = Math.Max(NativeTheme.Scale(30), NativeTheme.UiLineHeight);
                SetOffset(NativeMethods.LowWord(word) switch
                {
                    0 => Offset - line,
                    1 => Offset + line,
                    2 => Offset - client.Bottom,
                    3 => Offset + client.Bottom,
                    4 or 5 => info.TrackPosition,
                    6 => 0,
                    7 => _maximum,
                    _ => Offset,
                });
                return 0;
            case NativeMethods.WindowMessageShowWindow when word == 0:
            case NativeMethods.WindowMessageEnable when word == 0:
                _wheel = 0; break;
            case NativeMethods.WindowMessageCommand:
            case NativeMethods.WindowMessageDrawItem:
            case NativeMethods.WindowMessageControlColorEdit:
            case NativeMethods.WindowMessageControlColorListBox:
            case NativeMethods.WindowMessageControlColorStatic:
            case NativeMethods.WindowMessageControlColorButton:
                return NativeMethods.SendMessage(_owner, message, word, parameter);
        }
        return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (nint window in _controls.Keys)
            if (NativeMethods.IsWindow(window)) _ = NativeMethods.RemoveWindowSubclass(window, _procedure, SubclassId);
        _controls.Clear();
        // HWND 由所属对话框的销毁链释放，避免正文释放过程中再次进入布局。
    }

    internal static (int Width, int Height) Measure(nint window, string value, int width = 0, bool medium = false)
    {
        nint dc = NativeMethods.GetDeviceContext(window);
        nint previous = NativeMethods.SelectObject(dc, medium ? NativeTheme.UiMediumFont : NativeTheme.UiFont);
        try
        {
            NativeMethods.Rectangle bounds = new() { Right = width };
            _ = NativeMethods.DrawText(dc, value, value.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextNoPrefix
                | (width > 0 ? NativeMethods.DrawTextWordBreak : NativeMethods.DrawTextSingleLine));
            return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(window, dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll")] private static extern int SetScrollInfo(nint window, int bar, ref ScrollInfo info, bool redraw);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
}
