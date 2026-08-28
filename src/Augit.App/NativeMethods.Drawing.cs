using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeMethods
{
    internal const uint WindowMessagePaint = 0x000F;
    internal const uint WindowMessageEraseBackground = 0x0014;
    internal const uint RasterOperationSourceCopy = 0x00CC0020;
    internal const int StretchModeHalftone = 4;
    internal const uint DeviceIndependentRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStructure
    {
        internal nint DeviceContext;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Erase;
        internal Rectangle PaintRectangle;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Restore;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        internal byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint ImageSize;
        internal int XPixelsPerMeter;
        internal int YPixelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RgbQuad
    {
        internal byte Blue;
        internal byte Green;
        internal byte Red;
        internal byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal RgbQuad Colors;
    }

    [DllImport("user32.dll", EntryPoint = "BeginPaint")]
    internal static extern nint BeginPaint(nint window, out PaintStructure paint);

    [DllImport("user32.dll", EntryPoint = "EndPaint")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint window, ref PaintStructure paint);

    [DllImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRectangle(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", EntryPoint = "FillRect")]
    internal static extern int FillRectangle(nint deviceContext, ref Rectangle rectangle, nint brush);

    [DllImport("user32.dll", EntryPoint = "GetSysColorBrush")]
    internal static extern nint GetSystemColorBrush(int colorIndex);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
    internal static extern nint CreateCompatibleDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
    internal static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    internal static extern nint CreateDeviceIndependentBitmap(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll", EntryPoint = "SetStretchBltMode")]
    internal static extern int SetStretchMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll", EntryPoint = "StretchBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StretchBitmap(
        nint destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        nint source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint rasterOperation);
}
