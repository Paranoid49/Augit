using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeImageView
{
    private int _panX, _panY;
    private bool _dragging;
    private NativeMethods.Point _dragStart;
    private int _dragPanX, _dragPanY;
    private int _wheelZoomRemainder;
    private int _wheelPanRemainderX, _wheelPanRemainderY;
    private ImageWheelMode _wheelMode;

    private enum ImageWheelMode { None, Vertical, Horizontal, Zoom }

    private void ResetWheelInput()
    {
        _wheelZoomRemainder = _wheelPanRemainderX = _wheelPanRemainderY = 0;
        _wheelMode = ImageWheelMode.None;
    }

    private void HandleImageWheel(uint message, nuint parameter)
    {
        int delta = unchecked((short)((parameter >> 16) & 0xffff));
        if (delta == 0) return;
        bool horizontalMessage = message == NativeMethods.WindowMessageMouseHorizontalWheel;
        ImageWheelMode mode = !horizontalMessage && (parameter & 8) != 0 ? ImageWheelMode.Zoom
            : horizontalMessage || (parameter & 4) != 0 ? ImageWheelMode.Horizontal : ImageWheelMode.Vertical;
        if (_wheelMode != mode) { ResetWheelInput(); _wheelMode = mode; }
        if (mode == ImageWheelMode.Zoom)
        {
            int total = _wheelZoomRemainder + delta;
            int steps = total / 120;
            _wheelZoomRemainder = total % 120;
            double next = _effectiveScale;
            for (int i = 0; i < Math.Abs(steps); i++) next = CalculateNextZoom(next, steps > 0);
            // 合并同一次输入的多档缩放，只发出一次视图变化通知。
            SetManualScale(next);
            return;
        }

        _wheelZoomRemainder = 0;
        int movement = delta * NativeTheme.Scale(48) * (horizontalMessage ? -1 : 1);
        ref int remainder = ref (mode == ImageWheelMode.Horizontal ? ref _wheelPanRemainderX : ref _wheelPanRemainderY);
        int distance = (movement + remainder) / 120;
        remainder = (movement + remainder) % 120;
        if (mode == ImageWheelMode.Horizontal) Pan(_panX + distance, _panY);
        else Pan(_panX, _panY + distance);
    }

    internal (int X, int Y, int Width, int Height) CurrentImageBounds
    {
        get
        {
            _ = NativeMethods.GetClientRectangle(Handle, out var client);
            var bounds = CalculateImageBounds(client.Right, client.Bottom, BitmapWidth, BitmapHeight, _effectiveScale);
            return (bounds.X + _panX, bounds.Y + _panY, bounds.Width, bounds.Height);
        }
    }

    private void ClampPan()
    {
        if (_bitmap is null || !NativeMethods.GetClientRectangle(Handle, out var client)) return;
        var bounds = CalculateImageBounds(client.Right, client.Bottom, BitmapWidth, BitmapHeight, _effectiveScale);
        _panX = bounds.Width <= client.Right ? 0 : Math.Clamp(_panX, client.Right - bounds.Width - bounds.X, -bounds.X);
        _panY = bounds.Height <= client.Bottom ? 0 : Math.Clamp(_panY, client.Bottom - bounds.Height - bounds.Y, -bounds.Y);
    }

    private void Pan(int x, int y)
    {
        int previousX = _panX, previousY = _panY;
        _panX = x;
        _panY = y;
        ClampPan();
        if (previousX != _panX || previousY != _panY)
            _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    private void EndDrag()
    {
        _dragging = false;
        if (Handle != 0 && NativeMethods.GetCapture() == Handle) _ = NativeMethods.ReleaseCapture();
    }

    private bool HandleImageInput(uint message, nuint wordParameter, nint longParameter)
    {
        if (_disposed || _bitmap is null || !NativeMethods.IsWindowVisible(Handle)) return false;
        int x = unchecked((short)((long)longParameter & 0xffff));
        int y = unchecked((short)(((long)longParameter >> 16) & 0xffff));
        switch (message)
        {
            case 0x0201:
                ResetWheelInput();
                _ = NativeMethods.SetFocus(Handle);
                _ = NativeMethods.GetClientRectangle(Handle, out var client);
                var bounds = CurrentImageBounds;
                if (bounds.Width <= client.Right && bounds.Height <= client.Bottom) return true;
                _dragStart = new() { X = x, Y = y };
                _dragPanX = _panX;
                _dragPanY = _panY;
                _dragging = true;
                _ = NativeMethods.SetCapture(Handle);
                return true;
            case NativeMethods.WindowMessageMouseMove when _dragging:
                Pan(_dragPanX + x - _dragStart.X, _dragPanY + y - _dragStart.Y);
                return true;
            case 0x0202 when _dragging:
                EndDrag();
                return true;
            case NativeMethods.WindowMessageCaptureChanged:
            case 0x001F:
                EndDrag();
                ResetWheelInput();
                return false;
            case NativeMethods.WindowMessageMouseWheel:
            case NativeMethods.WindowMessageMouseHorizontalWheel:
                HandleImageWheel(message, wordParameter);
                return true;
            case NativeMethods.WindowMessageKeyDown:
                ResetWheelInput();
                int step = NativeTheme.Scale(32);
                switch ((int)wordParameter)
                {
                    case 37: Pan(_panX + step, _panY); return true;
                    case 38: Pan(_panX, _panY + step); return true;
                    case 39: Pan(_panX - step, _panY); return true;
                    case 40: Pan(_panX, _panY - step); return true;
                    case 27 when _dragging: EndDrag(); return true;
                }
                break;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        internal byte BlendOperation, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("msimg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AlphaBlend(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, BlendFunction blend);
}
