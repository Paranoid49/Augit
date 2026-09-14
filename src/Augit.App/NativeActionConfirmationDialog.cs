using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App;

/// <summary>
/// 显示带影响说明的轻量原生确认窗口。
/// </summary>
internal sealed class NativeActionConfirmationDialog : IDisposable
{
    private const string WindowClassName = "Augit.ActionConfirmationDialog.Native";
    private const int DialogWidth = 620;
    private const int DialogHeight = 300;
    private const int CompactDialogHeight = 289;
    private const int HeaderHeight = 45;
    private const int FooterHeight = 53;
    private const int CommandConfirm = 10;
    private const int CommandCancel = 11;
    private const int CommandHeaderClose = 12;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeActionConfirmationDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly ApplicationSettings _settings;
    private readonly string _title;
    private readonly string _heading;
    private readonly string _detail;
    private readonly string _confirmLabel;
    private readonly string _cancelLabel;
    private readonly bool _danger;
    private readonly int _dialogHeight;
    private readonly bool _warningLayout;
    private readonly string _warningDetail;
    private readonly IReadOnlyList<(string Label, string Value)> _warningFields;
    private nint _handle;
    private nint _headingLabel;
    private nint _detailLabel;
    private nint _confirmButton;
    private nint _cancelButton;
    private nint _headerCloseButton;
    private nint _controlBrush;
    private nint _warningBrush;
    private NativeToolTip? _toolTip;
    private bool _dark;
    private bool _confirmed;
    private bool _closed;
    private bool _disposed;

    private NativeActionConfirmationDialog(
        nint owner,
        ApplicationSettings settings,
        string title,
        string heading,
        string detail,
        string confirmLabel,
        bool danger,
        string? cancelLabel)
    {
        _owner = owner;
        _settings = settings;
        _title = title;
        _heading = heading;
        _detail = detail;
        _confirmLabel = confirmLabel;
        _cancelLabel = string.IsNullOrWhiteSpace(cancelLabel) ? UiText.Cancel : cancelLabel;
        _danger = danger;
        _dialogHeight = title.Equals(UiText.InitializeGitRepositoryTitle, StringComparison.Ordinal)
            ? CompactDialogHeight
            : DialogHeight;
        _warningLayout = !title.Equals(UiText.InitializeGitRepositoryTitle, StringComparison.Ordinal);
        (_warningDetail, _warningFields) = SplitWarningDetail(detail, _warningLayout);
        EnsureWindowClass();
        (int x, int y) = Center(owner, S(DialogWidth), S(_dialogHeight));
        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            title,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleClipChildren,
            x,
            y,
            S(DialogWidth),
            S(_dialogHeight),
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "确认窗口创建失败。");
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        _headingLabel = CreateControl(NativeMethods.StaticClass, heading, 0, NativeMethods.StaticLeft);
        _detailLabel = CreateControl(NativeMethods.StaticClass, _warningDetail, 0, NativeMethods.StaticLeft);
        _confirmButton = CreateControl(
            NativeMethods.ButtonClass,
            confirmLabel,
            CommandConfirm,
            NativeMethods.ButtonOwnerDraw);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            _cancelLabel,
            CommandCancel,
            NativeMethods.ButtonOwnerDraw);
        _headerCloseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.CloseSymbol,
            CommandHeaderClose,
            NativeMethods.ButtonOwnerDraw);
        _ = NativeMethods.SendMessage(
            _headingLabel,
            NativeMethods.WindowMessageSetFont,
            unchecked((nuint)NativeTheme.UiHeadingFont),
            1);
        _toolTip = new NativeToolTip(_handle);
        _toolTip.Add(_confirmButton, confirmLabel);
        _toolTip.Add(_cancelButton, UiText.Cancel);
        _toolTip.Add(_headerCloseButton, UiText.Close);
        ApplyAppearance();
        Layout();
    }

    internal static bool Show(
        nint owner,
        ApplicationSettings settings,
        string title,
        string heading,
        string detail,
        string confirmLabel,
        bool danger = false,
        string? cancelLabel = null)
    {
        NativeActionConfirmationDialog dialog = new(
            owner,
            settings,
            title,
            heading,
            detail,
            confirmLabel,
            danger,
            cancelLabel);
        try
        {
            return dialog.Run();
        }
        finally
        {
            dialog.Dispose();
        }
    }

    internal static (int Width, int Height, int HeaderHeight, int FooterHeight) LogicalLayoutForTest =>
        (DialogWidth, DialogHeight, HeaderHeight, FooterHeight);

    internal static IReadOnlyList<string> LabelsForTest(
        string title,
        string heading,
        string detail,
        string confirmLabel,
        string? cancelLabel = null)
    {
        return [title, heading, detail, confirmLabel, string.IsNullOrWhiteSpace(cancelLabel) ? UiText.Cancel : cancelLabel];
    }

    internal static (string WarningDetail, IReadOnlyList<(string Label, string Value)> Fields)
        WarningLayoutForTest(string detail) => SplitWarningDetail(detail, warningLayout: true);

    private bool Run()
    {
        using NativeModalFocusScope focusScope = new(_owner);
        using NativeModalScrim scrim = NativeModalScrim.Begin(_owner, _dark);
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetForegroundWindow(_handle);
        _ = NativeMethods.SetFocus(_cancelButton);
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
                    Close();
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

    /// <summary>
    /// 危险确认窗口默认先经过取消，再到确认和标题栏关闭。
    /// </summary>
    private void MoveFocus(bool backwards)
    {
        NativeFocusNavigation.MoveWithinRegion(
            [_cancelButton, _confirmButton, _headerCloseButton],
            NativeMethods.GetFocus(),
            backwards);
    }

    private nint CreateControl(string className, string text, int identifier, uint style)
    {
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
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "确认窗口控件创建失败。");
        }

        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)NativeTheme.UiFont), 1);
        return control;
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeActionConfirmationDialog? dialog;
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
            return dialog.ApplyControlColor(
                unchecked((nint)wordParameter),
                longParameter);
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => dialog.PaintWindow(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => dialog.HitTest(),
            NativeMethods.WindowMessageDrawItem => dialog.DrawControl(longParameter) ? 1 : 0,
            NativeMethods.WindowMessageSize => dialog.LayoutMessage(),
            NativeMethods.WindowMessageCommand => dialog.CommandMessage(wordParameter),
            NativeMethods.WindowMessageKeyDown when wordParameter == NativeMethods.VirtualKeyEscape => dialog.CloseMessage(),
            NativeMethods.WindowMessageClose => dialog.CloseMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private nint CommandMessage(nuint wordParameter)
    {
        switch (NativeMethods.LowWord(wordParameter))
        {
            case CommandConfirm:
                _confirmed = true;
                Close();
                break;
            case CommandCancel:
            case CommandHeaderClose:
                Close();
                break;
        }

        return 0;
    }

    private nint CloseMessage()
    {
        Close();
        return 0;
    }

    private nint LayoutMessage()
    {
        Layout();
        return 0;
    }

    private void Layout()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        int width = Math.Max(0, client.Right - client.Left);
        int footerTop = Math.Max(S(HeaderHeight), client.Bottom - S(FooterHeight));
        int inset = S(17);
        // 非初始化确认窗口的正文位于警告块内，保留视觉稿规定的内层留白。
        int contentInset = !_warningLayout
            ? inset
            : S(29);
        Move(_headingLabel, contentInset, S(74), Math.Max(S(180), width - contentInset - inset), S(28));
        Move(
            _detailLabel,
            contentInset,
            S(104),
            Math.Max(S(180), width - contentInset - inset),
            _warningLayout ? S(30) : Math.Max(S(42), footerTop - S(116)));
        int confirmWidth = S(MeasureButtonWidth(_confirmLabel));
        int cancelWidth = S(MeasureButtonWidth(_cancelLabel));
        int right = width - S(13);
        int confirmLeft = right - confirmWidth;
        int cancelLeft = confirmLeft - S(8) - cancelWidth;
        Move(_cancelButton, Math.Max(inset, cancelLeft), footerTop + S(12), cancelWidth, S(30));
        Move(_confirmButton, Math.Max(inset, confirmLeft), footerTop + S(12), confirmWidth, S(30));
        Move(_headerCloseButton, Math.Max(S(28), width - S(45)), S(7), S(32), S(31));
    }

    private void ApplyAppearance()
    {
        _dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, _dark);
        NativeThemePalette palette = NativeTheme.Palette(_dark);
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
        }

        if (_warningBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_warningBrush);
        }

        _controlBrush = NativeMethods.CreateSolidBrush(palette.Panel);
        _warningBrush = NativeMethods.CreateSolidBrush(_dark ? Rgb(75, 65, 37) : Rgb(255, 240, 194));
        foreach (nint control in new[]
        {
            _headingLabel,
            _detailLabel,
            _confirmButton,
            _cancelButton,
            _headerCloseButton,
        })
        {
            NativeTheme.ApplyToControl(control, _dark);
        }

        _toolTip?.ApplyAppearance(_dark);
    }

    private nint ApplyControlColor(nint deviceContext, nint control)
    {
        if (deviceContext == 0 || _controlBrush == 0)
        {
            return 0;
        }

        NativeThemePalette palette = NativeTheme.Palette(_dark);
        bool warning = _warningLayout
            && (control == _headingLabel || control == _detailLabel);
        _ = NativeMethods.SetBackgroundColor(
            deviceContext,
            warning ? (_dark ? Rgb(75, 65, 37) : Rgb(255, 240, 194)) : palette.Panel);
        _ = NativeMethods.SetTextColor(
            deviceContext,
            warning && control == _detailLabel ? palette.Muted : palette.Text);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        return warning && _warningBrush != 0 ? _warningBrush : _controlBrush;
    }

    private bool DrawControl(nint parameter)
    {
        if (parameter == 0)
        {
            return false;
        }

        NativeMethods.DrawItem item = Marshal.PtrToStructure<NativeMethods.DrawItem>(parameter);
        return unchecked((int)item.ControlIdentifier) switch
        {
            CommandConfirm => NativeTheme.DrawFlatButton(parameter, _dark, emphasized: true, danger: _danger),
            CommandCancel => NativeTheme.DrawFlatButton(parameter, _dark, outlined: true),
            CommandHeaderClose => NativeTheme.DrawFlatButton(parameter, _dark),
            _ => false,
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
            if (_warningLayout)
            {
                NativeMethods.Rectangle warning = new()
                {
                    Left = S(17),
                    Top = S(58),
                    Right = client.Right - S(17),
                    Bottom = Math.Min(client.Bottom - S(FooterHeight + 12), S(142)),
                };
                FillRounded(
                    deviceContext,
                    warning,
                    _dark ? Rgb(75, 65, 37) : Rgb(255, 240, 194),
                    S(5));

                int fieldTop = S(151);
                int labelWidth = S(110);
                int valueLeft = S(137);
                for (int index = 0; index < _warningFields.Count; index++)
                {
                    int rowTop = fieldTop + (index * S(28));
                    DrawText(
                        deviceContext,
                        _warningFields[index].Label,
                        new()
                        {
                            Left = S(17),
                            Top = rowTop,
                            Right = S(17) + labelWidth,
                            Bottom = rowTop + S(24),
                        },
                        palette.Text,
                        NativeTheme.UiFont);
                    DrawText(
                        deviceContext,
                        _warningFields[index].Value,
                        new()
                        {
                            Left = valueLeft,
                            Top = rowTop,
                            Right = client.Right - S(17),
                            Bottom = rowTop + S(24),
                        },
                        palette.Text,
                        NativeTheme.UiFont);
                }
            }
            DrawText(
                deviceContext,
                _title,
                new() { Left = S(17), Top = S(8), Right = client.Right - S(52), Bottom = S(38) },
                palette.Text,
                NativeTheme.UiMediumFont);
            DrawHelpIcon(deviceContext, client.Bottom - S(FooterHeight), palette.Muted);
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

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toolTip?.Dispose();
        _toolTip = null;
        if (_controlBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_controlBrush);
            _controlBrush = 0;
        }

        if (_warningBrush != 0)
        {
            _ = NativeMethods.DeleteObject(_warningBrush);
            _warningBrush = 0;
        }

        Close();
        GC.SuppressFinalize(this);
    }

    private static void Move(nint control, int x, int y, int width, int height)
    {
        if (control != 0)
        {
            _ = NativeMethods.MoveWindow(control, x, y, Math.Max(0, width), Math.Max(0, height), true);
        }
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

    private static void FillRounded(
        nint deviceContext,
        NativeMethods.Rectangle rectangle,
        uint color,
        int radius)
    {
        if (NativeGdiPlusDrawing.FillRoundedRectangle(deviceContext, rectangle, color, radius))
        {
            return;
        }

        nint brush = NativeMethods.CreateSolidBrush(color);
        nint region = NativeMethods.CreateRoundRectangleRegion(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right + 1,
            rectangle.Bottom + 1,
            radius,
            radius);
        if (brush != 0 && region != 0)
        {
            _ = NativeMethods.FillRegion(deviceContext, region, brush);
        }

        if (region != 0)
        {
            _ = NativeMethods.DeleteObject(region);
        }

        if (brush != 0)
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void DrawHelpIcon(nint deviceContext, int footerTop, uint color)
    {
        NativeMethods.Rectangle circle = new()
        {
            Left = S(15),
            Top = footerTop + S(17),
            Right = S(29),
            Bottom = footerTop + S(31),
        };
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, S(1)), color);
        nint previousPen = pen == 0 ? 0 : NativeMethods.SelectObject(deviceContext, pen);
        nint previousBrush = NativeMethods.SelectObject(
            deviceContext,
            NativeMethods.GetStockObject(NativeMethods.NullBrush));
        _ = NativeMethods.DrawEllipse(deviceContext, circle.Left, circle.Top, circle.Right, circle.Bottom);
        if (previousBrush != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousBrush);
        }

        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        if (pen != 0)
        {
            _ = NativeMethods.DeleteObject(pen);
        }

        DrawText(deviceContext, "?", circle, color, NativeTheme.UiSmallFont);
    }

    private static (string WarningDetail, IReadOnlyList<(string Label, string Value)> Fields) SplitWarningDetail(
        string detail,
        bool warningLayout)
    {
        if (!warningLayout)
        {
            return (detail, Array.Empty<(string Label, string Value)>());
        }

        string[] lines = detail.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string warningDetail = lines.Length == 0 ? detail : lines[0];
        List<(string Label, string Value)> fields = [];
        for (int index = 2; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            int separator = line.IndexOf('：');
            if (separator <= 0)
            {
                continue;
            }

            fields.Add((line[..separator], line[(separator + 1)..].Trim()));
        }

        return (warningDetail, fields);
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

    private static int S(int logicalPixels) => NativeTheme.Scale(logicalPixels);

    private static int MeasureButtonWidth(string text)
    {
        int width = 26;
        foreach (char character in text)
        {
            width += character > 0x7F ? 13 : 7;
        }

        return Math.Clamp(width, 53, 180);
    }

    private static uint Rgb(byte red, byte green, byte blue) =>
        (uint)(red | green << 8 | blue << 16);

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
                throw new Win32Exception(error, "确认窗口类注册失败。");
            }

            _classRegistered = true;
        }
    }
}
