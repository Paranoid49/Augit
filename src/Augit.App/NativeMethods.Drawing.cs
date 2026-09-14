using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeMethods
{
    internal const uint WindowMessagePaint = 0x000F;
    internal const uint WindowMessageEraseBackground = 0x0014;
    internal const uint WindowMessageControlColorEdit = 0x0133;
    internal const uint WindowMessageControlColorListBox = 0x0134;
    internal const uint WindowMessageControlColorButton = 0x0135;
    internal const uint WindowMessageControlColorStatic = 0x0138;
    internal const uint RasterOperationSourceCopy = 0x00CC0020;
    internal const int StretchModeHalftone = 4;
    internal const uint DeviceIndependentRgbColors = 0;
    internal const int BackgroundModeTransparent = 1;
    internal const int BackgroundModeOpaque = 2;
    internal const int PenStyleSolid = 0;
    internal const uint DrawTextCenter = 0x00000001;
    internal const uint DrawTextRight = 0x00000002;
    internal const uint DrawTextVerticalCenter = 0x00000004;
    internal const uint DrawTextWordBreak = 0x00000010;
    internal const uint DrawTextSingleLine = 0x00000020;
    internal const uint DrawTextNoPrefix = 0x00000800;
    internal const uint DrawTextEndEllipsis = 0x00008000;
    internal const uint DrawTextCalculateRectangle = 0x00000400;
    internal const uint GradientFillRectangleHorizontal = 0x00000000;
    internal const uint RedrawInvalidate = 0x0001;
    internal const uint RedrawEraseBackground = 0x0004;
    internal const uint RedrawAllChildren = 0x0080;
    internal const uint RedrawUpdateNow = 0x0100;
    internal const uint RedrawFrame = 0x0400;

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
    internal struct TriVertex
    {
        internal int X;
        internal int Y;
        internal ushort Red;
        internal ushort Green;
        internal ushort Blue;
        internal ushort Alpha;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GradientRectangle
    {
        internal uint UpperLeft;
        internal uint LowerRight;
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

    [DllImport("user32.dll", EntryPoint = "InvalidateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRectangle(nint window, ref Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", EntryPoint = "RedrawWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(nint window, nint updateRectangle, nint updateRegion, uint flags);

    [DllImport("user32.dll", EntryPoint = "FillRect")]
    internal static extern int FillRectangle(nint deviceContext, ref Rectangle rectangle, nint brush);

    [DllImport("user32.dll", EntryPoint = "GetDC")]
    internal static extern nint GetDeviceContext(nint window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    internal static extern int ReleaseDeviceContext(nint window, nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "FillRgn")]
    internal static extern int FillRegion(nint deviceContext, nint region, nint brush);

    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(
        nint deviceContext,
        string text,
        int length,
        ref Rectangle rectangle,
        uint format);

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

    [DllImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    internal static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreatePen")]
    internal static extern nint CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll", EntryPoint = "MoveToEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveTo(nint deviceContext, int x, int y, nint previousPoint);

    [DllImport("gdi32.dll", EntryPoint = "LineTo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LineTo(nint deviceContext, int x, int y);

    [DllImport("gdi32.dll", EntryPoint = "Ellipse")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawEllipse(nint deviceContext, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", EntryPoint = "GetPixel")]
    internal static extern uint GetPixel(nint deviceContext, int x, int y);

    [DllImport("gdi32.dll", EntryPoint = "Rectangle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawRectangle(nint deviceContext, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    internal static extern nint CreateRoundRectangleRegion(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll", EntryPoint = "SetWindowRgn")]
    internal static extern int SetWindowRegion(
        nint window,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", EntryPoint = "SetBkMode")]
    internal static extern int SetBackgroundMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll", EntryPoint = "SetTextColor")]
    internal static extern uint SetTextColor(nint deviceContext, uint color);

    [DllImport("gdi32.dll", EntryPoint = "SetBkColor")]
    internal static extern uint SetBackgroundColor(nint deviceContext, uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    internal static extern nint CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

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

    [DllImport("msimg32.dll", EntryPoint = "GradientFill", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FillGradient(
        nint deviceContext,
        [In] TriVertex[] vertices,
        uint vertexCount,
        [In] GradientRectangle[] rectangles,
        uint rectangleCount,
        uint mode);
}
