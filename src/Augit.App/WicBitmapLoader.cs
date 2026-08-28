using System.Runtime.InteropServices;

namespace Augit.App;

internal static class WicBitmapLoader
{
    private const uint GenericRead = 0x80000000;
    private static readonly Guid ImagingFactoryClass = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    private static readonly Guid PixelFormat32Bgra = new("6FDDC324-4E03-4BFE-B185-3D77768DC90A");

    internal static WicBitmap Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Type? factoryType = Type.GetTypeFromCLSID(ImagingFactoryClass, throwOnError: false);
        if (factoryType is null || Activator.CreateInstance(factoryType) is not IWICImagingFactory factory)
        {
            throw new InvalidOperationException(UiText.WicUnavailable);
        }

        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICFormatConverter? converter = null;
        try
        {
            factory.CreateDecoderFromFilename(Path.GetFullPath(path), 0, GenericRead, 0, out decoder);
            decoder.GetFrame(0, out frame);
            frame.GetSize(out uint width, out uint height);
            if (width == 0 || height == 0 || checked((long)width * height) > Augit.Core.Documents.DocumentLimits.MaximumImagePixels)
            {
                throw new InvalidOperationException(UiText.ImageDimensionsOutOfRange);
            }

            factory.CreateFormatConverter(out converter);
            Guid format = PixelFormat32Bgra;
            converter.Initialize(frame, ref format, 0, 0, 0, 0);
            int stride = checked((int)width * 4);
            byte[] pixels = new byte[checked(stride * (int)height)];
            nint pixelBuffer = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                converter.CopyPixels(0, stride, pixels.Length, pixelBuffer);
                Marshal.Copy(pixelBuffer, pixels, 0, pixels.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(pixelBuffer);
            }

            NativeMethods.BitmapInfo bitmapInfo = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = checked((int)width),
                    Height = -checked((int)height),
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    ImageSize = checked((uint)pixels.Length),
                },
            };
            nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(
                0,
                ref bitmapInfo,
                NativeMethods.DeviceIndependentRgbColors,
                out nint bits,
                0,
                0);
            if (bitmap == 0 || bits == 0)
            {
                throw new InvalidOperationException(UiText.ImageBitmapCreateFailed);
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);
            return new(bitmap, checked((int)width), checked((int)height));
        }
        finally
        {
            Release(converter);
            Release(frame);
            Release(decoder);
            Release(factory);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    [ComImport]
    [Guid("EC5EC8A9-C395-4314-9C77-54D7A935FF70")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        void CreateDecoderFromFilename(
            [MarshalAs(UnmanagedType.LPWStr)] string fileName,
            nint vendor,
            uint desiredAccess,
            uint metadataOptions,
            out IWICBitmapDecoder decoder);

        void CreateDecoderFromStream(nint stream, nint vendor, uint metadataOptions, out nint decoder);

        void CreateDecoderFromFileHandle(nuint fileHandle, nint vendor, uint metadataOptions, out nint decoder);

        void CreateComponentInfo(in Guid component, out nint componentInfo);

        void CreateDecoder(in Guid containerFormat, nint vendor, out nint decoder);

        void CreateEncoder(in Guid containerFormat, nint vendor, out nint encoder);

        void CreatePalette(out nint palette);

        void CreateFormatConverter(out IWICFormatConverter converter);
    }

    [ComImport]
    [Guid("9EDDE9E7-8DEE-47EA-99DF-E6FAF2ED44BF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        void QueryCapability(nint stream, out uint capability);

        void Initialize(nint stream, uint cacheOptions);

        void GetContainerFormat(out Guid containerFormat);

        void GetDecoderInfo(out nint decoderInfo);

        void CopyPalette(nint palette);

        void GetMetadataQueryReader(out nint metadataReader);

        void GetPreview(out nint bitmapSource);

        void GetColorContexts(uint count, nint colorContexts, out uint actualCount);

        void GetThumbnail(out nint thumbnail);

        void GetFrameCount(out uint count);

        void GetFrame(uint index, out IWICBitmapFrameDecode frame);
    }

    [ComImport]
    [Guid("00000120-A8F2-4877-BA0A-FD2B6645FB94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapSource
    {
        void GetSize(out uint width, out uint height);

        void GetPixelFormat(out Guid pixelFormat);

        void GetResolution(out double dpiX, out double dpiY);

        void CopyPalette(nint palette);

        void CopyPixels(nint rectangle, int stride, int bufferSize, nint buffer);
    }

    [ComImport]
    [Guid("3B16811B-6A43-4EC9-A813-3D930C13B940")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameDecode : IWICBitmapSource
    {
        new void GetSize(out uint width, out uint height);

        new void GetPixelFormat(out Guid pixelFormat);

        new void GetResolution(out double dpiX, out double dpiY);

        new void CopyPalette(nint palette);

        new void CopyPixels(nint rectangle, int stride, int bufferSize, nint buffer);

        void GetMetadataQueryReader(out nint metadataReader);

        void GetColorContexts(uint count, nint colorContexts, out uint actualCount);

        void GetThumbnail(out nint thumbnail);
    }

    [ComImport]
    [Guid("00000301-A8F2-4877-BA0A-FD2B6645FB94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter : IWICBitmapSource
    {
        new void GetSize(out uint width, out uint height);

        new void GetPixelFormat(out Guid pixelFormat);

        new void GetResolution(out double dpiX, out double dpiY);

        new void CopyPalette(nint palette);

        new void CopyPixels(nint rectangle, int stride, int bufferSize, nint buffer);

        void Initialize(
            IWICBitmapSource source,
            ref Guid destinationFormat,
            uint dither,
            nint palette,
            double alphaThresholdPercent,
            uint paletteTranslate);

        void CanConvert(ref Guid sourceFormat, ref Guid destinationFormat, [MarshalAs(UnmanagedType.Bool)] out bool canConvert);
    }
}

internal sealed class WicBitmap(nint handle, int width, int height) : IDisposable
{
    private bool _disposed;

    internal nint Handle { get; private set; } = handle;

    internal int Width { get; } = width;

    internal int Height { get; } = height;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Handle != 0)
        {
            _ = NativeMethods.DeleteObject(Handle);
            Handle = 0;
        }
    }
}
