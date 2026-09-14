using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed class NativeDocumentInfoView : IDisposable
{
    private const string WindowClassName = "Augit.DocumentInfoView.Native";
    private const int CommandOpenExternal = 1;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeDocumentInfoView> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly string _title;
    private readonly string _metadata;
    private readonly string _path;
    private readonly string _message;
    private readonly Action _openExternal;
    private nint _openExternalButton;
    private bool _dark;
    private bool _disposed;

    internal NativeDocumentInfoView(
        nint parent,
        string title,
        string metadata,
        string path,
        string message,
        bool canOpenExternally,
        Action openExternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(openExternal);
        _title = title;
        _metadata = metadata;
        _path = path;
        _message = message;
        _openExternal = openExternal;
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            $"{title}\r\n{metadata}\r\n{path}\r\n{message}",
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipChildren
                | NativeMethods.WindowStyleClipSiblings,
            0,
            0,
            0,
            0,
            parent,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentInfoViewCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }

        if (canOpenExternally)
        {
            _openExternalButton = NativeMethods.CreateWindow(
                0,
                NativeMethods.ButtonClass,
                UiText.OpenExternally,
                NativeMethods.WindowStyleChild
                    | NativeMethods.WindowStyleVisible
                    | NativeMethods.WindowStyleTabStop
                    | NativeMethods.ButtonOwnerDraw,
                0,
                0,
                0,
                0,
                Handle,
                CommandOpenExternal,
                NativeMethods.GetModuleHandle(null),
                0);
            if (_openExternalButton == 0)
            {
                Dispose();
                throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.DocumentControlCreateFailed);
            }

            _ = NativeMethods.SendMessage(
                _openExternalButton,
                NativeMethods.WindowMessageSetFont,
                unchecked((nuint)NativeTheme.UiFont),
                1);
        }

        Layout();
    }

    internal nint Handle { get; private set; }

    internal bool HasSinglePrimaryActionForTest => _openExternalButton != 0;

    internal bool OpenButtonWithinBoundsForTest => IsOpenButtonWithinBounds();

    internal bool PrimaryActionHasFocusForTest => _openExternalButton != 0
        && NativeMethods.GetFocus() == _openExternalButton;

    internal static NativeDocumentInfoLayout CalculateLayoutForTest(
        int width,
        int height,
        string? path = null,
        string? message = null,
        bool hasPrimaryAction = true)
    {
        return CalculateLayout(width, height, path, message, hasPrimaryAction);
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        Layout();
    }

    internal void ApplyAppearance(bool dark)
    {
        _dark = dark;
        NativeTheme.ApplyToControl(_openExternalButton, dark);
        _ = NativeMethods.InvalidateRectangle(_openExternalButton, 0, true);
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal void FocusPrimaryAction()
    {
        if (_openExternalButton != 0)
        {
            _ = NativeMethods.SetFocus(_openExternalButton);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        nint handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(handle);
            }

            if (NativeMethods.IsWindow(handle))
            {
                _ = NativeMethods.DestroyWindow(handle);
            }
        }

        _openExternalButton = 0;
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
                Style = NativeMethods.ClassRedrawOnHorizontalChange | NativeMethods.ClassRedrawOnVerticalChange,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = 0,
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.DocumentInfoViewClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeDocumentInfoView? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        switch (message)
        {
            case NativeMethods.WindowMessageSize:
                instance.Layout();
                return 0;
            case NativeMethods.WindowMessageEraseBackground:
                return 1;
            case NativeMethods.WindowMessagePaint:
                instance.Paint();
                return 0;
            case NativeMethods.WindowMessageDrawItem:
                return NativeTheme.DrawFlatButton(longParameter, instance._dark, outlined: true) ? 1 : 0;
            case NativeMethods.WindowMessageCommand:
                if (NativeMethods.LowWord(wordParameter) == CommandOpenExternal)
                {
                    instance._openExternal();
                    return 0;
                }

                break;
            case NativeMethods.WindowMessageSetFocus:
                if (instance._openExternalButton != 0)
                {
                    _ = NativeMethods.SetFocus(instance._openExternalButton);
                    return 0;
                }

                break;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void Layout()
    {
        if (Handle == 0
            || _openExternalButton == 0
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            return;
        }

        NativeDocumentInfoLayout layout = CalculateLayout(
            client.Right - client.Left,
            client.Bottom - client.Top,
            _path,
            _message,
            _openExternalButton != 0);
        NativeMethods.Rectangle button = layout.Button;
        _ = NativeMethods.MoveWindow(
            _openExternalButton,
            button.Left,
            button.Top,
            Math.Max(0, button.Right - button.Left),
            Math.Max(0, button.Bottom - button.Top),
            true);
    }

    private void Paint()
    {
        nint deviceContext = NativeMethods.BeginPaint(Handle, out NativeMethods.PaintStructure paint);
        try
        {
            if (!NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
            {
                return;
            }

            NativeThemePalette palette = NativeTheme.Palette(_dark);
            Fill(deviceContext, client, palette.Panel);
            NativeDocumentInfoLayout layout = CalculateLayout(
                client.Right - client.Left,
                client.Bottom - client.Top,
                _path,
                _message,
                _openExternalButton != 0);
            DrawWarningFileIcon(deviceContext, layout.Icon, _dark ? Rgb(242, 190, 64) : Rgb(218, 159, 40));
            DrawText(deviceContext, _title, layout.Title, palette.Text, NativeTheme.UiHeadingFont, wordBreak: false);
            DrawText(deviceContext, _metadata, layout.Metadata, palette.Muted, NativeTheme.UiFont, wordBreak: false);
            DrawText(deviceContext, _path, layout.Path, palette.Muted, NativeTheme.UiFont, wordBreak: true);
            DrawText(deviceContext, _message, layout.Message, palette.Muted, NativeTheme.UiFont, wordBreak: true);
        }
        finally
        {
            _ = NativeMethods.EndPaint(Handle, ref paint);
        }
    }

    private bool IsOpenButtonWithinBounds()
    {
        if (_openExternalButton == 0
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)
            || !NativeMethods.GetWindowRectangle(_openExternalButton, out NativeMethods.Rectangle button))
        {
            return false;
        }

        NativeMethods.Point topLeft = new() { X = button.Left, Y = button.Top };
        NativeMethods.Point bottomRight = new() { X = button.Right, Y = button.Bottom };
        return NativeMethods.ScreenToClient(Handle, ref topLeft)
            && NativeMethods.ScreenToClient(Handle, ref bottomRight)
            && topLeft.X >= client.Left
            && topLeft.Y >= client.Top
            && bottomRight.X <= client.Right
            && bottomRight.Y <= client.Bottom;
    }

    private static NativeDocumentInfoLayout CalculateLayout(
        int width,
        int height,
        string? path = null,
        string? message = null,
        bool hasPrimaryAction = true)
    {
        int blockWidth = Math.Min(NativeTheme.Scale(620), Math.Max(0, width - NativeTheme.Scale(48)));
        int centerX = width / 2;
        int blockLeft = Math.Max(0, centerX - blockWidth / 2);
        int titleHeight = Math.Max(NativeTheme.Scale(26), MeasureSingleLineHeight(NativeTheme.UiHeadingFont));
        int metadataHeight = Math.Max(NativeTheme.Scale(20), MeasureSingleLineHeight(NativeTheme.UiFont));
        int pathHeight = Math.Max(
            NativeTheme.Scale(38),
            MeasureWrappedHeight(path ?? string.Empty, blockWidth, NativeTheme.UiFont) + NativeTheme.Scale(8));
        int messageHeight = Math.Max(
            NativeTheme.Scale(36),
            MeasureWrappedHeight(message ?? string.Empty, blockWidth, NativeTheme.UiFont) + NativeTheme.Scale(8));
        int titleTop = NativeTheme.Scale(30);
        int metadataTop = titleTop + titleHeight + NativeTheme.Scale(2);
        int pathTop = metadataTop + metadataHeight + NativeTheme.Scale(2);
        int messageTop = pathTop + pathHeight + NativeTheme.Scale(4);
        int buttonTop = messageTop + messageHeight + NativeTheme.Scale(4);
        int buttonHeight = NativeTheme.Scale(30);
        int totalHeight = (hasPrimaryAction ? buttonTop + buttonHeight : messageTop + messageHeight)
            + NativeTheme.Scale(8);
        int top = Math.Max(NativeTheme.Scale(8), (height - totalHeight) / 2);
        int iconSize = NativeTheme.Scale(18);
        NativeMethods.Rectangle icon = Rectangle(
            centerX - iconSize / 2,
            top,
            iconSize,
            iconSize);
        NativeMethods.Rectangle title = Rectangle(blockLeft, top + titleTop, blockWidth, titleHeight);
        NativeMethods.Rectangle metadata = Rectangle(blockLeft, top + metadataTop, blockWidth, metadataHeight);
        NativeMethods.Rectangle pathRectangle = Rectangle(blockLeft, top + pathTop, blockWidth, pathHeight);
        NativeMethods.Rectangle messageRectangle = Rectangle(blockLeft, top + messageTop, blockWidth, messageHeight);
        int buttonWidth = NativeTheme.Scale(178);
        NativeMethods.Rectangle button = Rectangle(
            centerX - buttonWidth / 2,
            top + buttonTop,
            buttonWidth,
            hasPrimaryAction ? buttonHeight : 0);
        return new(icon, title, metadata, pathRectangle, messageRectangle, button);
    }

    private static int MeasureSingleLineHeight(nint font)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        if (deviceContext == 0) return NativeTheme.UiLineHeight;
        try
        {
            nint previous = NativeMethods.SelectObject(deviceContext, font);
            try
            {
                NativeMethods.Rectangle bounds = new();
                _ = NativeMethods.DrawText(
                    deviceContext,
                    "国Ag",
                    3,
                    ref bounds,
                    NativeMethods.DrawTextCalculateRectangle
                        | NativeMethods.DrawTextSingleLine
                        | NativeMethods.DrawTextNoPrefix);
                return Math.Max(1, bounds.Bottom - bounds.Top);
            }
            finally
            {
                _ = NativeMethods.SelectObject(deviceContext, previous);
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    private static int MeasureWrappedHeight(string value, int width, nint font)
    {
        if (string.IsNullOrEmpty(value) || width <= 0) return 0;
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        if (deviceContext == 0) return NativeTheme.UiLineHeight;
        try
        {
            nint previous = NativeMethods.SelectObject(deviceContext, font);
            try
            {
                NativeMethods.Rectangle bounds = new() { Right = width };
                _ = NativeMethods.DrawText(
                    deviceContext,
                    value,
                    value.Length,
                    ref bounds,
                    NativeMethods.DrawTextCalculateRectangle
                        | NativeMethods.DrawTextWordBreak
                        | NativeMethods.DrawTextNoPrefix);
                return Math.Max(0, bounds.Bottom - bounds.Top);
            }
            finally
            {
                _ = NativeMethods.SelectObject(deviceContext, previous);
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    private static NativeMethods.Rectangle Rectangle(int x, int y, int width, int height)
    {
        return new()
        {
            Left = x,
            Top = y,
            Right = x + Math.Max(0, width),
            Bottom = y + Math.Max(0, height),
        };
    }

    private static void DrawText(
        nint deviceContext,
        string text,
        NativeMethods.Rectangle rectangle,
        uint color,
        nint font,
        bool wordBreak)
    {
        nint previousFont = NativeMethods.SelectObject(deviceContext, font);
        _ = NativeMethods.SetBackgroundMode(deviceContext, NativeMethods.BackgroundModeTransparent);
        _ = NativeMethods.SetTextColor(deviceContext, color);
        uint format = NativeMethods.DrawTextCenter | NativeMethods.DrawTextNoPrefix;
        format |= wordBreak
            ? NativeMethods.DrawTextWordBreak
            : NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextVerticalCenter | NativeMethods.DrawTextEndEllipsis;
        _ = NativeMethods.DrawText(deviceContext, text, text.Length, ref rectangle, format);
        if (previousFont != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousFont);
        }
    }

    private static void DrawWarningFileIcon(nint deviceContext, NativeMethods.Rectangle rectangle, uint color)
    {
        int left = rectangle.Left + NativeTheme.Scale(3);
        int top = rectangle.Top + NativeTheme.Scale(1);
        int right = rectangle.Right - NativeTheme.Scale(3);
        int bottom = rectangle.Bottom - NativeTheme.Scale(1);
        int fold = NativeTheme.Scale(4);
        nint pen = NativeMethods.CreatePen(NativeMethods.PenStyleSolid, Math.Max(1, NativeTheme.Scale(1)), color);
        if (pen == 0)
        {
            return;
        }

        nint previousPen = NativeMethods.SelectObject(deviceContext, pen);
        _ = NativeMethods.MoveTo(deviceContext, left, top, 0);
        _ = NativeMethods.LineTo(deviceContext, right - fold, top);
        _ = NativeMethods.LineTo(deviceContext, right, top + fold);
        _ = NativeMethods.LineTo(deviceContext, right, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, bottom);
        _ = NativeMethods.LineTo(deviceContext, left, top);
        _ = NativeMethods.MoveTo(deviceContext, right - fold, top, 0);
        _ = NativeMethods.LineTo(deviceContext, right - fold, top + fold);
        _ = NativeMethods.LineTo(deviceContext, right, top + fold);
        int centerX = (left + right) / 2;
        _ = NativeMethods.MoveTo(deviceContext, centerX, top + NativeTheme.Scale(6), 0);
        _ = NativeMethods.LineTo(deviceContext, centerX, top + NativeTheme.Scale(10));
        _ = NativeMethods.DrawEllipse(
            deviceContext,
            centerX - NativeTheme.Scale(1),
            top + NativeTheme.Scale(12),
            centerX + NativeTheme.Scale(1) + 1,
            top + NativeTheme.Scale(14) + 1);
        if (previousPen != 0)
        {
            _ = NativeMethods.SelectObject(deviceContext, previousPen);
        }

        _ = NativeMethods.DeleteObject(pen);
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

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}

internal readonly record struct NativeDocumentInfoLayout(
    NativeMethods.Rectangle Icon,
    NativeMethods.Rectangle Title,
    NativeMethods.Rectangle Metadata,
    NativeMethods.Rectangle Path,
    NativeMethods.Rectangle Message,
    NativeMethods.Rectangle Button);
