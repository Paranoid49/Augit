using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 跳转行和引用名称共用的紧凑输入窗口；只返回文本，不执行文件或 Git 操作。
/// </summary>
internal sealed class NativeTextPrompt : IDisposable
{
    private const string WindowClassName = "Augit.TextPrompt.Native";
    private const int CommandConfirm = 1;
    private const int CommandCancel = 2;
    private const int CommandInput = 3;
    private const int CommandClose = 4;
    private const uint ImeStartComposition = 0x010D;
    private const uint ImeEndComposition = 0x010E;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeTextPrompt> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly bool _dark;
    private readonly string _title;
    private readonly string _label;
    private readonly NativeMethods.SubclassProcedure _inputProcedure;
    private nint _handle;
    private nint _edit;
    private nint _confirmButton;
    private nint _cancelButton;
    private nint _closeButton;
    private nint _backgroundBrush;
    private NativeToolTip? _toolTip;
    private NativeMethods.Rectangle _inputFrame;
    private int _width, _height, _headerHeight, _rowHeight, _fontHeight, _labelWidth, _closeWidth;
    private bool _composing;
    private bool _dpiChanged;
    private bool _closed;
    private string? _result;
    private int? _quitCode;

    private NativeTextPrompt(nint owner, string title, string label, bool dark)
    {
        // 文档传入的是子窗口；模态关系和遮罩必须归属同一个顶层窗口。
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0) _owner = owner;
        _dark = dark;
        _title = title;
        _label = label;
        _inputProcedure = HandleInputMessage;
        EnsureWindowClass();
        _handle = NativeMethods.CreateWindow(0, WindowClassName, title,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            0, 0, 1, 1, _owner, 0, NativeMethods.GetModuleHandle(null), 0);
        if (_handle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InputWindowCreateFailed);

        lock (InstancesGate) Instances.Add(_handle, this);
        try
        {
            _backgroundBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(dark).Panel);
            _edit = CreateChild(NativeMethods.EditClass, string.Empty, NativeMethods.EditAutoHorizontalScroll, CommandInput);
            _cancelButton = CreateChild(NativeMethods.ButtonClass, UiText.Cancel, NativeMethods.ButtonOwnerDraw, CommandCancel);
            _confirmButton = CreateChild(NativeMethods.ButtonClass, UiText.Confirm, NativeMethods.ButtonOwnerDraw, CommandConfirm);
            _closeButton = CreateChild(NativeMethods.ButtonClass, string.Empty, NativeMethods.ButtonOwnerDraw, CommandClose);
            if (!NativeMethods.SetWindowSubclass(_edit, _inputProcedure, 1, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InputControlCreateFailed);
            _ = NativeAccessibility.SetName(_edit, label);
            _toolTip = new NativeToolTip(_handle);
            _toolTip.Add(_closeButton, UiText.Close);
            ApplyAppearance();
            Resize(center: true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static string? Show(nint owner, string title, string label, bool dark)
    {
        NativeTextPrompt prompt = new(owner, title, label, dark);
        try { return prompt.Run(); }
        finally
        {
            prompt.Dispose();
            // 先完成窗口、遮罩和焦点收尾，避免系统内部消息循环再次消费退出通知。
            if (prompt._quitCode is { } exitCode) NativeMethods.PostQuitMessage(exitCode);
        }
    }

    private string? Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        bool ownerEnabled = NativeMethods.IsWindowEnabled(_owner);
        _ = NativeMethods.EnableWindow(_owner, false);
        try
        {
            _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
            _ = NativeMethods.UpdateWindow(_handle);
            _ = NativeMethods.SetForegroundWindow(_handle);
            _ = NativeMethods.SetFocus(_edit);
            while (!_closed)
            {
                int status = NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0);
                if (status <= 0)
                {
                    // 嵌套消息循环不能吞掉主循环的退出请求。
                    if (status == 0) _quitCode = unchecked((int)message.WordParameter);
                    break;
                }
                bool belongsToPrompt = message.Window == _handle || NativeMethods.IsChild(_handle, message.Window);
                if (belongsToPrompt && message.MessageId == NativeMethods.WindowMessageKeyDown && !_composing)
                {
                    switch (unchecked((int)message.WordParameter))
                    {
                        case NativeMethods.VirtualKeyTab:
                            NativeFocusNavigation.MoveWithinRegion(
                                [_edit, _cancelButton, _confirmButton, _closeButton],
                                NativeMethods.GetFocus(), NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                            continue;
                        case NativeMethods.VirtualKeyEnter:
                            nint focus = NativeMethods.GetFocus();
                            if (focus == _cancelButton || focus == _closeButton) Close();
                            else Confirm();
                            continue;
                        case NativeMethods.VirtualKeyEscape:
                            Close();
                            continue;
                    }
                }
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            Close();
            if (NativeMethods.IsWindow(_owner))
            {
                // 主题字体目前按顶层窗口共享；跨屏弹窗关闭后让宿主重新应用自身 DPI。
                if (_dpiChanged && NativeMethods.GetWindowRectangle(_owner, out NativeMethods.Rectangle ownerBounds))
                {
                    nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rectangle>());
                    try
                    {
                        Marshal.StructureToPtr(ownerBounds, buffer, false);
                        uint dpi = NativeMethods.GetDpiForWindow(_owner);
                        _ = NativeMethods.SendMessage(_owner, NativeMethods.WindowMessageDpiChanged, dpi | (dpi << 16), buffer);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                _ = NativeMethods.EnableWindow(_owner, ownerEnabled);
                if (ownerEnabled) _ = NativeMethods.SetForegroundWindow(_owner);
            }
            focusScope.Restore();
        }
        return _result;
    }

    private nint CreateChild(string className, string text, uint style, int identifier)
    {
        nint child = NativeMethods.CreateWindow(0, className, text,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop | style,
            0, 0, 0, 0, _handle, identifier, NativeMethods.GetModuleHandle(null), 0);
        if (child == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InputControlCreateFailed);
        return child;
    }

    private void ApplyAppearance()
    {
        NativeTheme.ApplyToWindow(_handle, _dark);
        foreach (nint control in new[] { _edit, _cancelButton, _confirmButton, _closeButton })
        {
            NativeTheme.ApplyToControl(control, _dark);
            _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        }
        _toolTip?.ApplyAppearance(_dark);
    }

    private (int Width, int Height) Measure(string text, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(_handle);
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(_handle, dc);
        }
    }

    private int ButtonWidth(string text) => Math.Max(S(74), Measure(text, NativeTheme.UiFont).Width + S(24));

    private void Resize(bool center, NativeMethods.Rectangle? suggested = null)
    {
        _fontHeight = Measure("国Ag", NativeTheme.UiFont).Height;
        _rowHeight = Math.Max(S(31), _fontHeight + S(12));
        _headerHeight = Math.Max(S(45), _rowHeight + S(14));
        _closeWidth = S(32);
        _labelWidth = Measure(_label, NativeTheme.UiFont).Width;
        _width = Math.Max(S(400), _labelWidth + S(244));
        _width = Math.Max(_width, Measure(_title, NativeTheme.UiMediumFont).Width + _closeWidth + S(42));
        _width = Math.Max(_width, ButtonWidth(UiText.Cancel) + ButtonWidth(UiText.Confirm) + S(42));
        _height = _headerHeight + (2 * _rowHeight) + S(57);
        _ = NativeMethods.GetWindowRectangle(_handle, out NativeMethods.Rectangle current);
        int x = suggested?.Left ?? current.Left;
        int y = suggested?.Top ?? current.Top;
        if (center && NativeMethods.GetWindowRectangle(_owner, out NativeMethods.Rectangle owner))
        {
            x = owner.Left + Math.Max(0, (owner.Right - owner.Left - _width) / 2);
            y = owner.Top + Math.Max(0, (owner.Bottom - owner.Top - _height) / 2);
        }
        _ = NativeMethods.MoveWindow(_handle, x, y, _width, _height, true);
        nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, _width + 1, _height + 1, S(18), S(18));
        if (region != 0 && NativeMethods.SetWindowRegion(_handle, region, true) == 0)
            _ = NativeMethods.DeleteObject(region);
        Layout();
    }

    private void Layout()
    {
        if (_edit == 0 || _rowHeight == 0) return;
        int top = _headerHeight + S(16);
        _inputFrame = new() { Left = S(29) + _labelWidth, Top = top, Right = _width - S(17), Bottom = top + _rowHeight };
        _ = NativeMethods.MoveWindow(_edit, _inputFrame.Left + S(8), top + (_rowHeight - _fontHeight) / 2,
            _inputFrame.Right - _inputFrame.Left - S(16), _fontHeight, true);
        int confirmWidth = ButtonWidth(UiText.Confirm);
        int cancelWidth = ButtonWidth(UiText.Cancel);
        int confirmLeft = _width - S(17) - confirmWidth;
        int buttonTop = _height - S(12) - _rowHeight;
        _ = NativeMethods.MoveWindow(_confirmButton, confirmLeft, buttonTop, confirmWidth, _rowHeight, true);
        _ = NativeMethods.MoveWindow(_cancelButton, confirmLeft - S(8) - cancelWidth, buttonTop, cancelWidth, _rowHeight, true);
        _ = NativeMethods.MoveWindow(_closeButton, _width - S(13) - _closeWidth, (_headerHeight - _rowHeight) / 2, _closeWidth, _rowHeight, true);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint word, nint parameter)
    {
        NativeTextPrompt? prompt;
        lock (InstancesGate) Instances.TryGetValue(window, out prompt);
        if (prompt is null) return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
        switch (message)
        {
            case NativeMethods.WindowMessagePaint:
                prompt.Paint();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessageControlColorEdit:
                NativeThemePalette palette = NativeTheme.Palette(prompt._dark);
                _ = NativeMethods.SetBackgroundColor(unchecked((nint)word), palette.Panel);
                _ = NativeMethods.SetTextColor(unchecked((nint)word), palette.Text);
                return prompt._backgroundBrush;
            case NativeMethods.WindowMessageDrawItem:
                prompt.DrawButton(parameter);
                return 1;
            case NativeMethods.WindowMessageCommand:
                int command = NativeMethods.LowWord(word);
                if (NativeMethods.HighWord(word) == 0)
                {
                    if (command == CommandConfirm && parameter == prompt._confirmButton) prompt.Confirm();
                    else if ((command == CommandCancel && parameter == prompt._cancelButton)
                        || (command == CommandClose && parameter == prompt._closeButton)) prompt.Close();
                }
                return 0;
            case NativeMethods.WindowMessageDpiChanged:
                prompt._dpiChanged = true;
                _ = NativeTheme.UpdateDpiForWindow(window);
                prompt.ApplyAppearance();
                prompt.Resize(center: false, parameter == 0 ? null : Marshal.PtrToStructure<NativeMethods.Rectangle>(parameter));
                return 0;
            case NativeMethods.WindowMessageNonClientHitTest:
                NativeMethods.Point point = new() { X = unchecked((short)NativeMethods.LowWord(unchecked((nuint)parameter))), Y = unchecked((short)NativeMethods.HighWord(unchecked((nuint)parameter))) };
                _ = NativeMethods.ScreenToClient(window, ref point);
                return point.Y < prompt._headerHeight && point.X < prompt._width - prompt._closeWidth - S(21)
                    ? NativeMethods.HitTestCaption : NativeMethods.HitTestClient;
            case NativeMethods.WindowMessageClose:
                prompt.Close();
                return 0;
        }
        return NativeMethods.DefaultWindowProcedure(window, message, word, parameter);
    }

    private nint HandleInputMessage(nint window, uint message, nuint word, nint parameter, nuint identifier, nuint data)
    {
        if (message == ImeStartComposition) _composing = true;
        if (message == ImeEndComposition) _composing = false;
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
        if (message is NativeMethods.WindowMessageSetFocus or NativeMethods.WindowMessageKillFocus)
            _ = NativeMethods.InvalidateRectangle(_handle, 0, false);
        return result;
    }

    private void Paint()
    {
        nint dc = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        try
        {
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            NativeMethods.Rectangle client = new() { Right = _width, Bottom = _height };
            NativeTheme.Fill(dc, client, palette.Panel);
            NativeTheme.FillRounded(dc, client, palette.BorderStrong, S(18));
            int border = Math.Max(1, S(1));
            NativeTheme.FillRounded(dc, new() { Left = border, Top = border, Right = _width - border, Bottom = _height - border }, palette.Panel, S(16));
            NativeTheme.Fill(dc, new() { Left = border, Top = _headerHeight, Right = _width - border, Bottom = _headerHeight + border }, palette.Border);
            int footer = _height - _rowHeight - S(25);
            NativeTheme.Fill(dc, new() { Left = border, Top = footer, Right = _width - border, Bottom = footer + border }, palette.Border);
            DrawText(dc, _title, new() { Left = S(17), Top = 0, Right = _width - _closeWidth - S(21), Bottom = _headerHeight }, NativeTheme.UiMediumFont, palette.Text);
            DrawText(dc, _label, new() { Left = S(17), Top = _inputFrame.Top, Right = S(17) + _labelWidth, Bottom = _inputFrame.Bottom }, NativeTheme.UiFont, palette.Text);
            NativeTheme.FillRounded(dc, _inputFrame, NativeMethods.GetFocus() == _edit ? palette.Accent : palette.BorderStrong, S(6));
            NativeMethods.Rectangle inner = _inputFrame;
            inner.Left += border; inner.Top += border; inner.Right -= border; inner.Bottom -= border;
            NativeTheme.FillRounded(dc, inner, palette.Panel, S(5));
        }
        finally { _ = NativeMethods.EndPaint(_handle, ref paint); }
    }

    private static void DrawText(nint dc, string text, NativeMethods.Rectangle rectangle, nint font, uint color)
    {
        nint previous = NativeMethods.SelectObject(dc, font);
        _ = NativeMethods.SetBackgroundMode(dc, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(dc, color);
        _ = NativeMethods.DrawText(dc, text, text.Length, ref rectangle,
            NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextNoPrefix | NativeMethods.DrawTextEndEllipsis);
        _ = NativeMethods.SelectObject(dc, previous);
    }

    private void DrawButton(nint parameter)
    {
        if (parameter == 0) return;
        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        _ = NativeTheme.DrawFlatButton(parameter, _dark, emphasized: item.Control == _confirmButton, outlined: item.Control == _cancelButton);
        if (item.Control == _closeButton)
        {
            _ = NativeTheme.DrawTabCloseIcon(item.DeviceContext,
                (item.ItemRectangle.Left + item.ItemRectangle.Right) / 2,
                (item.ItemRectangle.Top + item.ItemRectangle.Bottom) / 2,
                NativeTheme.Palette(_dark).Muted);
        }
        if ((item.ItemState & NativeMethods.OwnerDrawFocus) != 0)
        {
            int inset = S(3);
            NativeMethods.Rectangle rectangle = item.ItemRectangle;
            _ = NativeGdiPlusDrawing.StrokeShapes(item.DeviceContext,
                item.Control == _confirmButton ? 0xFFFFFF : NativeTheme.Palette(_dark).Accent, Math.Max(1, S(1)), [], [],
                [new(rectangle.Left + inset, rectangle.Top + inset, rectangle.Right - rectangle.Left - 2 * inset, rectangle.Bottom - rectangle.Top - 2 * inset)], []);
        }
    }

    private void Confirm()
    {
        if (_closed) return;
        _result = NativeMethods.GetWindowTextValue(_edit);
        Close();
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        if (_edit != 0) _ = NativeMethods.RemoveWindowSubclass(_edit, _inputProcedure, 1);
        _toolTip?.Dispose();
        _toolTip = null;
        if (handle != 0 && NativeMethods.IsWindow(handle)) _ = NativeMethods.DestroyWindow(handle);
        lock (InstancesGate) Instances.Remove(handle);
        _handle = 0;
    }

    public void Dispose()
    {
        Close();
        if (_backgroundBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_backgroundBrush);
            _backgroundBrush = 0;
        }
        GC.SuppressFinalize(this);
    }

    private static int S(int logical) => NativeTheme.Scale(logical);

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered) return;
            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
                throw new Win32Exception(error, UiText.InputWindowClassRegisterFailed);
            _classRegistered = true;
        }
    }
}
