using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeTerminalCloseDialog : IDisposable
{
    private const string WindowClassName = "Augit.TerminalCloseDialog.Native";
    private const int DialogWidth = 620;
    private const int DialogHeight = 300;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int FooterRightInset = 12;
    private const int FooterButtonGap = 8;
    private const int KeepButtonWidth = 92;
    private const int CloseButtonWidth = 122;
    private const int CommandKeep = 1;
    private const int CommandClose = 2;
    private const int CommandHeaderClose = 3;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeTerminalCloseDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly ApplicationSettings _settings;
    private readonly Action _keep;
    private readonly Action _close;
    private nint _handle;
    private nint _title;
    private nint _command;
    private nint _message;
    private nint _keepButton;
    private nint _closeButton;
    private nint _headerCloseButton;
    private nint _controlBrush;
    private bool _dark;
    private bool _closed;
    private bool _confirmed;

    private NativeTerminalCloseDialog(
        nint owner,
        ApplicationSettings settings,
        Action keep,
        Action close)
    {
        _owner = owner;
        _settings = settings;
        _keep = keep;
        _close = close;
        EnsureWindowClass();
        (int x, int y) = Center(owner, S(DialogWidth), S(DialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            "关闭终端",
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(DialogWidth),
            S(DialogHeight),
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "终端关闭确认窗口创建失败。");
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        _title = CreateControl(NativeMethods.StaticClass, "终端中仍有命令正在运行", NativeMethods.StaticLeft);
        _command = CreateControl(NativeMethods.StaticClass, "dotnet test Augit.slnx -c Release", NativeMethods.StaticLeft);
        _message = CreateControl(
            NativeMethods.StaticClass,
            "继续将结束前台命令、Shell 及其整个子进程树。",
            NativeMethods.StaticLeft);
        _keepButton = CreateControl(
            NativeMethods.ButtonClass,
            "保留终端",
            NativeMethods.ButtonOwnerDraw);
        _closeButton = CreateControl(
            NativeMethods.ButtonClass,
            "结束命令并关闭",
            NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CloseSymbol,
            NativeMethods.ButtonOwnerDraw);
        ApplyAppearance();
        Layout();
    }

    internal static bool Show(
        nint owner,
        ApplicationSettings settings,
        Action keep,
        Action close)
    {
        using NativeTerminalCloseDialog dialog = new(owner, settings, keep, close);
        return dialog.Run();
    }

    internal static string WindowClassNameForTest => WindowClassName;

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static bool UsesSystemCaptionForTest => false;

    internal static (int KeepWidth, int CloseWidth, int RightInset, int Gap) FooterButtonLayoutForTest =>
        (KeepButtonWidth, CloseButtonWidth, FooterRightInset, FooterButtonGap);

    public void Dispose()
    {
        Close(invokeAction: false);
        GC.SuppressFinalize(this);
    }

    private bool Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetForegroundWindow(_handle);
        _ = NativeMethods.SetFocus(_keepButton);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyTab)
                {
                    MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
                    continue;
                }

                if (message.MessageId == NativeMethods.WindowMessageKeyDown
                    && unchecked((int)message.WordParameter) == NativeMethods.VirtualKeyEscape)
                {
                    Close(invokeAction: true, keep: true);
                    continue;
                }

                if (!NativeMethods.IsDialogMessage(_handle, ref message))
                {
                    _ = NativeMethods.TranslateMessage(ref message);
                    _ = NativeMethods.DispatchMessage(ref message);
                }
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
            focusScope.Restore();
        }

        return _confirmed;
    }

    private nint CreateControl(string className, string text, uint style)
    {
        bool button = className.Equals(NativeMethods.ButtonClass, StringComparison.Ordinal);
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | style,
            0,
            0,
            0,
            0,
            _handle,
            button
                ? text.StartsWith("结束", StringComparison.Ordinal)
                    ? CommandClose
                    : text == UiText.CloseSymbol
                        ? CommandHeaderClose
                        : CommandKeep
                : 0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "终端关闭确认控件创建失败。");
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        _controlBrush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        foreach (nint control in new[] { _title, _command, _message, _keepButton, _closeButton, _headerCloseButton })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int footerTop = Math.Max(S(HeaderHeight), client.Bottom - S(FooterHeight));
        int inset = S(20);
        int contentWidth = Math.Max(S(200), width - inset * 2);
        Move(_title, inset, S(67), contentWidth, S(28));
        Move(_command, inset, S(106), contentWidth, S(26));
        Move(_message, inset, S(140), contentWidth, S(28));
        int buttonTop = footerTop + S(12);
        Move(
            _keepButton,
            Math.Max(inset, width - S(FooterRightInset + FooterButtonGap + CloseButtonWidth + KeepButtonWidth)),
            buttonTop,
            S(KeepButtonWidth),
            S(29));
        Move(
            _closeButton,
            Math.Max(inset, width - S(FooterRightInset + CloseButtonWidth)),
            buttonTop,
            S(CloseButtonWidth),
            S(29));
        Move(_headerCloseButton, Math.Max(S(28), width - S(45)), S(7), S(32), S(31));
    }

    private static void Move(nint control, int x, int y, int width, int height)
    {
        if (control != 0)
        {
            _ = NativeMethods.MoveWindow(control, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
    }

    private static (int X, int Y) Center(nint owner, int width, int height)
    {
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle rectangle))
        {
            return (
                rectangle.Left + Math.Max(0, (rectangle.Right - rectangle.Left - width) / 2),
                rectangle.Top + Math.Max(0, (rectangle.Bottom - rectangle.Top - height) / 2));
        }

        return (NativeMethods.UseDefault, NativeMethods.UseDefault);
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, "终端关闭确认窗口类注册失败。");
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeTerminalCloseDialog? dialog;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out dialog);
        }

        if (dialog is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message is NativeMethods.WindowMessageControlColorButton
            or NativeMethods.WindowMessageControlColorStatic)
        {
            return dialog.ApplyControlColor(unchecked((nint)wordParameter));
        }

        if (message == NativeMethods.WindowMessagePaint)
        {
            return dialog.PaintWindow();
        }

        if (message == NativeMethods.WindowMessageEraseBackground)
        {
            return 1;
        }

        if (message == NativeMethods.WindowMessageNonClientHitTest)
        {
            return dialog.HitTest();
        }

        if (message == NativeMethods.WindowMessageDrawItem)
        {
            return dialog.DrawControl(longParameter) ? 1 : 0;
        }

        if (message == NativeMethods.WindowMessageSize)
        {
            dialog.Layout();
            return 0;
        }

        if (message == NativeMethods.WindowMessageCommand)
        {
            switch (NativeMethods.LowWord(wordParameter))
            {
                case CommandKeep:
                    dialog.Close(invokeAction: true, keep: true);
                    return 0;
                case CommandClose:
                    dialog.Close(invokeAction: true, keep: false);
                    return 0;
                case CommandHeaderClose:
                    dialog.Close(invokeAction: true, keep: true);
                    return 0;
            }
        }

        if (message == NativeMethods.WindowMessageKeyDown
            && wordParameter == NativeMethods.VirtualKeyTab)
        {
            dialog.MoveFocus(NativeMethods.GetKeyState(NativeMethods.VirtualKeyShift) < 0);
            return 0;
        }

        if (message == NativeMethods.WindowMessageKeyDown
            && wordParameter == NativeMethods.VirtualKeyEscape)
        {
            dialog.Close(invokeAction: true, keep: true);
            return 0;
        }

        if (message == NativeMethods.WindowMessageClose)
        {
            dialog.Close(invokeAction: true, keep: true);
            return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    /// <summary>
    /// 按关闭终端确认窗口的视觉顺序循环移动焦点。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [_keepButton, _closeButton, _headerCloseButton],
            NativeMethods.GetFocus(),
            backwards);
    }

    private nint ApplyControlColor(nint deviceContext)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        _ = NativeMethods.SetTextColor(deviceContext, palette.Text);
        _ = NativeMethods.SetBackgroundColor(deviceContext, palette.Panel);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        return _controlBrush;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        return item.ControlIdentifier switch
        {
            CommandClose => NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true, danger: true),
            CommandHeaderClose => NativeTheme.DrawFlatButton(parameter, _dark),
            _ => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
        };
    }

    private nint PaintWindow()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return 0;
        }

        try
        {
            if (!NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                return 0;
            }

            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.Panel);
            Fill(deviceContext, new() { Left = 0, Top = S(HeaderHeight), Right = client.Right, Bottom = S(HeaderHeight + 1) }, palette.Border);
            Fill(deviceContext, new() { Left = 0, Top = client.Bottom - S(FooterHeight), Right = client.Right, Bottom = client.Bottom - S(FooterHeight - 1) }, palette.Border);
            DrawText(
                deviceContext,
                "关闭终端",
                new() { Left = S(17), Top = S(8), Right = client.Right - S(52), Bottom = S(38) },
                palette.Text,
                NativeTheme.UiMediumFont);
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private nint HitTest()
    {
        if (!NativeMethods.GetCursorPosition(out NativeMethods.Point point)
            || !NativeMethods.ScreenToClient(_handle, ref point))
        {
            return NativeMethods.HitTestClient;
        }

        return point.Y < S(HeaderHeight) && point.X < S(DialogWidth - 54)
            ? NativeMethods.HitTestCaption
            : NativeMethods.HitTestClient;
    }

    private static void Fill(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        nint brush = NativeMethods.CreateSolidBrush(color);
        if (brush != 0)
        {
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        _ = NativeMethods.DrawText(
            deviceContext,
            text,
            text.Length,
            ref rectangle,
            NativeMethods.DrawTextSingleLine
                | NativeMethods.DrawTextVerticalCenter
                | NativeMethods.DrawTextNoPrefix
                | NativeMethods.DrawTextEndEllipsis);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private void Close(bool invokeAction, bool keep = true)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _confirmed = invokeAction && !keep;
        nint handle = _handle;
        NativeMethods.WakeWindowMessageLoop(handle);
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }

        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        if (invokeAction)
        {
            if (keep)
            {
                _keep();
            }
            else
            {
                _close();
            }
        }
    }

    private static int S(int logicalPixels) => NativeTheme.Scale(logicalPixels);
}
