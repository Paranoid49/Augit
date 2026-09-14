using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeImageView
{
    private nint _checkerBrush;
    private int _checkerCell;

    private void PaintCheckerboard(nint deviceContext, NativeMethods.Rectangle visibleArea, int imageX, int imageY)
    {
        int cellSize = Math.Max(2, NativeTheme.Scale(8));
        if (_checkerBrush == 0 || _checkerCell != cellSize) CreateCheckerboard(cellSize);
        if (_checkerBrush == 0) return;
        // 棋盘格以图片矩形为原点；裁切、平移和居中不能把原点改成画布或可见部分的左上角。
        _ = SetBrushOrgEx(deviceContext, imageX, imageY, out var previous);
        try { _ = NativeMethods.FillRectangle(deviceContext, ref visibleArea, _checkerBrush); }
        finally { _ = SetBrushOrgEx(deviceContext, previous.X, previous.Y, out _); }
    }

    private void CreateCheckerboard(int cellSize)
    {
        ReleaseCheckerboard();
        int size = cellSize * 2;
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = size,
                Height = -size,
                Planes = 1,
                BitCount = 32,
                ImageSize = (uint)(size * size * 4)
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out nint bits, 0, 0);
        if (bitmap == 0) return;
        try
        {
            NativeThemePalette palette = NativeTheme.Palette(_dark);
            byte[] pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                uint color = (x / cellSize + y / cellSize) % 2 == 0 ? palette.Panel : palette.PanelMuted;
                int offset = (y * size + x) * 4;
                pixels[offset] = (byte)(color >> 16);
                pixels[offset + 1] = (byte)(color >> 8);
                pixels[offset + 2] = (byte)color;
            }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            _checkerBrush = CreatePatternBrush(bitmap);
            _checkerCell = cellSize;
        }
        finally { _ = NativeMethods.DeleteObject(bitmap); }
    }

    private void ReleaseCheckerboard()
    {
        if (_checkerBrush != 0) _ = NativeMethods.DeleteObject(_checkerBrush);
        _checkerBrush = 0;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreatePatternBrush(nint bitmap);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetBrushOrgEx(nint deviceContext, int x, int y, out NativeMethods.Point previous);
}
