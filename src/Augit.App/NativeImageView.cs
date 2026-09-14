using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class NativeImageView : IDisposable
{
    private const string WindowClassName = "Augit.ImageView.Native";
    private const double MinimumZoom = 0.10d;
    private const double MaximumZoom = 8.00d;
    private static readonly double[] ZoomSteps =
        [0.10d, 0.25d, 0.50d, 0.75d, 1.00d, 1.25d, 1.50d, 2.00d, 3.00d, 4.00d, 6.00d, 8.00d];
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeImageView> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private WicBitmap? _bitmap;
    private bool _dark;
    private bool _fitToArea = true;
    private double _manualScale = 1d;
    private double _effectiveScale = 1d;
    private bool _disposed;
    private bool _loading;

    internal NativeImageView(nint parent)
    {
        EnsureWindowClass();
        Handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleClipSiblings,
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.ImageViewCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }
    }

    internal nint Handle { get; private set; }

    internal int BitmapWidth => _bitmap?.Width ?? 0;

    internal int BitmapHeight => _bitmap?.Height ?? 0;

    internal int ZoomPercentage => Math.Max(1, (int)Math.Round(_effectiveScale * 100d));

    internal bool IsFitToArea => _fitToArea;

    internal bool CanZoomIn => _bitmap is not null && CalculateNextZoom(_effectiveScale, zoomIn: true) > _effectiveScale + 0.0001d;

    internal bool CanZoomOut => _bitmap is not null && CalculateNextZoom(_effectiveScale, zoomIn: false) < _effectiveScale - 0.0001d;

    internal event Action? ViewChanged;

    internal bool LoadingForTest => _loading;

    internal void SetLoading(bool loading)
    {
        if (_loading == loading) return;
        _loading = loading;
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal void SetBitmap(WicBitmap bitmap, bool preserveView = false)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        CancelSampling();
        _bitmap?.Dispose();
        _bitmap = bitmap;
        ResetWheelInput();
        if (!preserveView) { _fitToArea = true; _panX = 0; _panY = 0; }
        EndDrag();
        UpdateEffectiveScale();
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
        UpdateEffectiveScale();
    }

    internal void SetVisible(bool visible)
    {
        if (!visible) { EndDrag(); ResetWheelInput(); CancelSampling(); ReleaseFrame(); }
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
    }

    internal void ApplyAppearance(bool dark)
    {
        ReleaseCheckerboard();
        _dark = dark;
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal void ZoomIn()
    {
        ResetWheelInput();
        SetManualScale(CalculateNextZoom(_effectiveScale, zoomIn: true));
    }

    internal void ZoomOut()
    {
        ResetWheelInput();
        SetManualScale(CalculateNextZoom(_effectiveScale, zoomIn: false));
    }

    internal void FitToArea()
    {
        ResetWheelInput();
        EndDrag();
        _panX = 0;
        _panY = 0;
        _fitToArea = true;
        UpdateEffectiveScale();
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    internal static double CalculateFitScaleForTest(
        int viewWidth,
        int viewHeight,
        int imageWidth,
        int imageHeight)
    {
        return CalculateFitScale(viewWidth, viewHeight, imageWidth, imageHeight);
    }

    internal static double CalculateNextZoomForTest(double current, bool zoomIn)
    {
        return CalculateNextZoom(current, zoomIn);
    }

    internal static (int X, int Y, int Width, int Height) CalculateImageBoundsForTest(
        int viewWidth,
        int viewHeight,
        int imageWidth,
        int imageHeight,
        double scale)
    {
        return CalculateImageBounds(viewWidth, viewHeight, imageWidth, imageHeight, scale);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelSampling();
        EndDrag();
        ReleaseCheckerboard();
        ViewChanged = null;
        ReleaseFrame();
        _bitmap?.Dispose();
        _bitmap = null;
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
                throw new Win32Exception(error, UiText.ImageViewClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeImageView? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is not null)
        {
            if (instance.HandleImageInput(message, wordParameter, longParameter)) return 0;
            switch (message)
            {
                case 0x0082:
                    instance.CancelSampling();
                    instance.EndDrag();
                    instance.ReleaseCheckerboard();
                    instance.ReleaseFrame();
                    instance._bitmap?.Dispose();
                    instance._bitmap = null;
                    instance.ViewChanged = null;
                    instance.Handle = 0;
                    instance._disposed = true;
                    lock (InstancesGate) Instances.Remove(window);
                    break;
                case ResampleMessage:
                    instance.ApplySample();
                    return 0;
                case NativeMethods.WindowMessageSize:
                    instance.UpdateEffectiveScale();
                    _ = NativeMethods.InvalidateRectangle(window, 0, false);
                    return 0;
                case NativeMethods.WindowMessageEraseBackground:
                    return 1;
                case NativeMethods.WindowMessagePaint:
                    instance.Paint(window);
                    return 0;
                case 0x0318:
                    instance.DrawImageContent((nint)wordParameter);
                    return 0;
            }
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void Paint(nint window)
    {
        nint paintContext = NativeMethods.BeginPaint(window, out NativeMethods.PaintStructure paint);
        nint deviceContext = paintContext;
        try
        {
            if (!NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client))
            {
                return;
            }
            deviceContext = PrepareFrame(client.Right, client.Bottom, paintContext);
            DrawImageContent(deviceContext);
        }
        finally
        {
            if (deviceContext != paintContext)
                _ = BitBlt(paintContext, 0, 0, _frameWidth, _frameHeight, deviceContext, 0, 0, NativeMethods.RasterOperationSourceCopy);
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void DrawImageContent(nint deviceContext)
    {
        if (deviceContext == 0 || !NativeMethods.GetClientRectangle(Handle, out var client)) return;
        nint background = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).Panel);
        try { _ = NativeMethods.FillRectangle(deviceContext, ref client, background); }
        finally { if (background != 0) _ = NativeMethods.DeleteObject(background); }
        if (_bitmap is null || _bitmap.Handle == 0)
        {
            PaintLoading(deviceContext, client);
            return;
        }

        (int x, int y, int width, int height) = CurrentImageBounds;
        // 透明棋盘仅在图片矩形内绘制，缩小后的外围画布保持纯色。
        NativeMethods.Rectangle imageArea = new()
        {
            Left = Math.Max(client.Left, x),
            Top = Math.Max(client.Top, y),
            Right = Math.Min(client.Right, x + width),
            Bottom = Math.Min(client.Bottom, y + height),
        };
        if (imageArea.Right > imageArea.Left && imageArea.Bottom > imageArea.Top)
            PaintCheckerboard(deviceContext, imageArea, x, y);
        if (TryDrawSample(deviceContext))
        {
            PaintImageBorder(deviceContext, x, y, width, height);
            PaintLoading(deviceContext, client);
            return;
        }
        nint memory = NativeMethods.CreateCompatibleDeviceContext(deviceContext);
        if (memory == 0)
        {
            return;
        }

        nint previous = NativeMethods.SelectObject(memory, _bitmap.Handle);
        try
        {
            _ = AlphaBlend(
                deviceContext,
                x,
                y,
                width,
                height,
                memory,
                0,
                0,
                _bitmap.Width,
                _bitmap.Height,
                new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 });
            PaintImageBorder(deviceContext, x, y, width, height);
        }
        finally
        {
            _ = NativeMethods.SelectObject(memory, previous);
            _ = NativeMethods.DeleteDeviceContext(memory);
        }
        PaintLoading(deviceContext, client);
    }

    private void SetManualScale(double scale)
    {
        if (_bitmap is null || Math.Abs(scale - _effectiveScale) < 0.0001d) return;
        EndDrag();
        double ratio = scale / Math.Max(0.000001d, _effectiveScale);
        _panX = (int)Math.Round(_panX * ratio);
        _panY = (int)Math.Round(_panY * ratio);
        _fitToArea = false;
        _manualScale = Math.Clamp(scale, MinimumZoom, MaximumZoom);
        UpdateEffectiveScale();
        _ = NativeMethods.InvalidateRectangle(Handle, 0, false);
    }

    private void UpdateEffectiveScale()
    {
        if (_bitmap is null
            || _bitmap.Width <= 0
            || _bitmap.Height <= 0
            || !NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client))
        {
            _effectiveScale = _fitToArea ? 1d : _manualScale;
            return;
        }

        _effectiveScale = _fitToArea
            ? CalculateFitScale(
                client.Right - client.Left,
                client.Bottom - client.Top,
                _bitmap.Width,
                _bitmap.Height)
            : _manualScale;
        ClampPan();
        ViewChanged?.Invoke();
    }

    private static double CalculateFitScale(
        int viewWidth,
        int viewHeight,
        int imageWidth,
        int imageHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return 1d;
        }

        int padding = NativeTheme.Scale(32);
        int availableWidth = Math.Max(1, viewWidth - padding * 2);
        int availableHeight = Math.Max(1, viewHeight - padding * 2);
        double scale = Math.Min(
            (double)availableWidth / imageWidth,
            (double)availableHeight / imageHeight);
        return Math.Min(1d, scale);
    }

    private static double CalculateNextZoom(double current, bool zoomIn)
    {
        if (current < MinimumZoom) return zoomIn ? MinimumZoom : current;
        double normalized = Math.Clamp(current, MinimumZoom, MaximumZoom);
        const double Epsilon = 0.0001d;
        if (zoomIn)
        {
            return ZoomSteps.FirstOrDefault(step => step > normalized + Epsilon, MaximumZoom);
        }

        return ZoomSteps.LastOrDefault(step => step < normalized - Epsilon, MinimumZoom);
    }

    private static (int X, int Y, int Width, int Height) CalculateImageBounds(
        int viewWidth,
        int viewHeight,
        int imageWidth,
        int imageHeight,
        double scale)
    {
        int width = Math.Max(1, (int)Math.Round(imageWidth * Math.Clamp(scale, double.Epsilon, MaximumZoom)));
        int height = Math.Max(1, (int)Math.Round(imageHeight * Math.Clamp(scale, double.Epsilon, MaximumZoom)));
        return ((viewWidth - width) / 2, (viewHeight - height) / 2, width, height);
    }

    private void PaintImageBorder(nint deviceContext, int x, int y, int width, int height)
    {
        // 与视觉稿的内缩 outline 一致，避免高 DPI 的居中描边侵入图片外的画布。
        int border = Math.Min(Math.Min(width, height), Math.Max(1, NativeTheme.Scale(1)));
        nint brush = NativeMethods.CreateSolidBrush(NativeTheme.Palette(_dark).BorderStrong);
        if (brush == 0) return;
        try
        {
            FillBorder(x, y, x + width, y + border);
            FillBorder(x, y + height - border, x + width, y + height);
            FillBorder(x, y, x + border, y + height);
            FillBorder(x + width - border, y, x + width, y + height);
        }
        finally { _ = NativeMethods.DeleteObject(brush); }

        void FillBorder(int left, int top, int right, int bottom)
        {
            NativeMethods.Rectangle rectangle = new() { Left = left, Top = top, Right = right, Bottom = bottom };
            _ = NativeMethods.FillRectangle(deviceContext, ref rectangle, brush);
        }
    }
}
