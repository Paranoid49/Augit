using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeImageView
{
    private nint _frameDc, _frameBitmap, _framePrevious;
    private int _frameWidth, _frameHeight;

    private nint PrepareFrame(int width, int height, nint fallback)
    {
        if (_frameDc != 0 && _frameWidth == width && _frameHeight == height) return _frameDc;
        ReleaseFrame();
        if (width <= 0 || height <= 0) return fallback;
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            },
        };
        _frameBitmap = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out _, 0, 0);
        _frameDc = NativeMethods.CreateCompatibleDeviceContext(fallback);
        if (_frameBitmap == 0 || _frameDc == 0) { ReleaseFrame(); return fallback; }
        _framePrevious = NativeMethods.SelectObject(_frameDc, _frameBitmap);
        _frameWidth = width;
        _frameHeight = height;
        return _frameDc;
    }

    private void ReleaseFrame()
    {
        if (_frameDc != 0)
        {
            if (_framePrevious != 0) _ = NativeMethods.SelectObject(_frameDc, _framePrevious);
            _ = NativeMethods.DeleteDeviceContext(_frameDc);
        }
        if (_frameBitmap != 0) _ = NativeMethods.DeleteObject(_frameBitmap);
        _frameDc = _frameBitmap = _framePrevious = 0;
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
}
