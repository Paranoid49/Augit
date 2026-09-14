using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeGdiPlusDrawing
{
    internal static WicBitmap CreateReducedImage(WicBitmap bitmap, int x, int y, int width, int height, int viewWidth, int viewHeight)
    {
        if (bitmap.Bits == 0 || !EnsureStarted()) throw new InvalidOperationException("图片绘制不可用。");
        int left = Math.Max(0, x), top = Math.Max(0, y);
        int visibleWidth = Math.Min(viewWidth, x + width) - left;
        int visibleHeight = Math.Min(viewHeight, y + height) - top;
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = visibleWidth,
                Height = -visibleHeight,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint handle = NativeMethods.CreateDeviceIndependentBitmap(0, ref info, 0, out nint bits, 0, 0);
        if (handle == 0 || bits == 0)
        {
            if (handle != 0) _ = NativeMethods.DeleteObject(handle);
            throw new InvalidOperationException(UiText.ImageBitmapCreateFailed);
        }
        WicBitmap? result = new(handle, visibleWidth, visibleHeight, bits);
        nint image = 0, target = 0, graphics = 0, attributes = 0;
        try
        {
            // 两端直接包装预乘 DIB。只缓存可见区域，放大或平移不分配整幅缩放副本。
            CheckImageStatus(GdipCreateBitmapFromScan0(bitmap.Width, bitmap.Height, bitmap.Width * 4, 0x000E200B, bitmap.Bits, out image));
            CheckImageStatus(GdipCreateBitmapFromScan0(visibleWidth, visibleHeight, visibleWidth * 4, 0x000E200B, bits, out target));
            CheckImageStatus(GdipGetImageGraphicsContext(target, out graphics));
            CheckImageStatus(GdipSetCompositingMode(graphics, 1));
            CheckImageStatus(GdipGraphicsClear(graphics, 0));
            CheckImageStatus(GdipSetInterpolationMode(graphics, 7));
            CheckImageStatus(GdipSetPixelOffsetMode(graphics, PixelOffsetModeHalf));
            CheckImageStatus(GdipCreateImageAttributes(out attributes));
            CheckImageStatus(GdipSetImageAttributesWrapMode(attributes, 3, 0, false));
            CheckImageStatus(GdipDrawImageRectRectI(graphics, image, x - left, y - top, width, height,
                0, 0, bitmap.Width, bitmap.Height, UnitPixel, attributes, 0, 0));
            WicBitmap completed = result;
            result = null;
            return completed;
        }
        finally
        {
            if (attributes != 0) _ = GdipDisposeImageAttributes(attributes);
            if (graphics != 0) _ = GdipDeleteGraphics(graphics);
            if (target != 0) _ = GdipDisposeImage(target);
            if (image != 0) _ = GdipDisposeImage(image);
            result?.Dispose();
        }
    }

    private static void CheckImageStatus(int status)
    {
        if (status != GdiPlusOk) throw new InvalidOperationException("图片平滑采样失败。");
    }

    [DllImport("gdiplus.dll")] private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan, out nint image);
    [DllImport("gdiplus.dll")] private static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipSetCompositingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipGraphicsClear(nint graphics, uint color);
    [DllImport("gdiplus.dll")] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateImageAttributes(out nint attributes);
    [DllImport("gdiplus.dll")] private static extern int GdipSetImageAttributesWrapMode(nint attributes, int wrapMode, uint color, [MarshalAs(UnmanagedType.Bool)] bool clamp);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawImageRectRectI(nint graphics, nint image, int x, int y, int width, int height, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int unit, nint attributes, nint callback, nint callbackData);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImageAttributes(nint attributes);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(nint image);
}
