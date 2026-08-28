using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed class NativeImageView : IDisposable
{
    private const string WindowClassName = "Augit.ImageView.Native";
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeImageView> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private WicBitmap? _bitmap;
    private bool _disposed;

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

    internal void SetBitmap(WicBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _ = NativeMethods.InvalidateRectangle(Handle, 0, true);
    }

    internal void SetBounds(int x, int y, int width, int height)
    {
        _ = NativeMethods.MoveWindow(Handle, x, y, Math.Max(0, width), Math.Max(0, height), true);
    }

    internal void SetVisible(bool visible)
    {
        _ = NativeMethods.ShowWindow(Handle, visible ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow),
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

        if (message == NativeMethods.WindowMessagePaint && instance is not null)
        {
            instance.Paint(window);
            return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void Paint(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out NativeMethods.PaintStructure paint);
        try
        {
            _ = NativeMethods.FillRectangle(
                deviceContext,
                ref paint.PaintRectangle,
                NativeMethods.GetSystemColorBrush(NativeMethods.ColorWindow));
            if (_bitmap is null || _bitmap.Handle == 0 || !NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client))
            {
                return;
            }

            int availableWidth = Math.Max(1, client.Right - client.Left - 24);
            int availableHeight = Math.Max(1, client.Bottom - client.Top - 24);
            double scale = Math.Min((double)availableWidth / _bitmap.Width, (double)availableHeight / _bitmap.Height);
            scale = Math.Min(1, scale);
            int width = Math.Max(1, (int)Math.Round(_bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(_bitmap.Height * scale));
            int x = (client.Right - client.Left - width) / 2;
            int y = (client.Bottom - client.Top - height) / 2;
            nint memory = NativeMethods.CreateCompatibleDeviceContext(deviceContext);
            if (memory == 0)
            {
                return;
            }

            nint previous = NativeMethods.SelectObject(memory, _bitmap.Handle);
            try
            {
                _ = NativeMethods.SetStretchMode(deviceContext, NativeMethods.StretchModeHalftone);
                _ = NativeMethods.StretchBitmap(
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
                    NativeMethods.RasterOperationSourceCopy);
            }
            finally
            {
                _ = NativeMethods.SelectObject(memory, previous);
                _ = NativeMethods.DeleteDeviceContext(memory);
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(window, ref paint);
        }
    }
}
