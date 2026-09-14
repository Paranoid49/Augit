using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal static class NativeScrollBarCornerTheme
{
    private const uint WindowMessagePrint = 0x0317;
    private const uint WindowMessageNonClientDestroy = 0x0082;
    private const nuint SubclassIdentifier = 1;
    private static readonly object Gate = new();
    private static readonly Dictionary<nint, uint> Colors = [];
    private static readonly NativeMethods.SubclassProcedure Procedure = HandleMessage;

    internal static void Apply(nint window, uint color)
    {
        bool registered;
        lock (Gate)
        {
            registered = Colors.ContainsKey(window);
            Colors[window] = color;
        }
        if (!registered && !NativeMethods.SetWindowSubclass(window, Procedure, SubclassIdentifier, 0))
        {
            int error = Marshal.GetLastWin32Error();
            lock (Gate) { Colors.Remove(window); }
            throw new Win32Exception(error, "无法设置正文滚动条空角主题。");
        }
        _ = NativeMethods.RedrawWindow(window, 0, 0, NativeMethods.RedrawInvalidate | NativeMethods.RedrawFrame);
    }

    internal static void Unregister(nint window)
    {
        _ = NativeMethods.RemoveWindowSubclass(window, Procedure, SubclassIdentifier);
        lock (Gate) { Colors.Remove(window); }
    }

    internal static bool IsRegisteredForTest(nint window)
    {
        lock (Gate) { return Colors.ContainsKey(window); }
    }

    private static nint HandleMessage(nint window, uint message, nuint wordParameter, nint longParameter,
        nuint subclassIdentifier, nuint referenceData)
    {
        if (message == WindowMessageNonClientDestroy)
        {
            Unregister(window);
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (message is NativeMethods.WindowMessageNonClientPaint or NativeMethods.WindowMessagePaint)
        {
            PaintCorner(window, 0);
        }
        else if (message == WindowMessagePrint && (longParameter & 2) != 0)
        {
            PaintCorner(window, unchecked((nint)wordParameter));
        }
        return result;
    }

    private static void PaintCorner(nint window, nint target)
    {
        uint color;
        lock (Gate)
        {
            if (!Colors.TryGetValue(window, out color)) { return; }
        }
        uint style = unchecked((uint)NativeMethods.GetWindowLongPointer(window, NativeMethods.WindowLongStyle));
        const uint scrollBars = NativeMethods.WindowStyleHorizontalScroll | NativeMethods.WindowStyleVerticalScroll;
        if ((style & scrollBars) != scrollBars
            || !NativeMethods.GetWindowRectangle(window, out NativeMethods.Rectangle bounds)
            || !NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client))
        {
            return;
        }
        NativeMethods.Point corner = new() { X = client.Right, Y = client.Bottom };
        if (!NativeMethods.ClientToScreen(window, ref corner)) { return; }
        NativeMethods.Rectangle area = new()
        {
            Left = corner.X - bounds.Left,
            Top = corner.Y - bounds.Top,
            Right = bounds.Right - bounds.Left,
            Bottom = bounds.Bottom - bounds.Top,
        };
        if (area.Right <= area.Left || area.Bottom <= area.Top) { return; }
        // 只填充非客户区中两条滚动条的交界空角；滚动条、正文和输入仍由原控件处理。
        nint dc = target != 0 ? target : GetWindowDeviceContext(window);
        if (dc == 0) { return; }
        nint brush = NativeMethods.CreateSolidBrush(color);
        try
        {
            if (brush != 0) { _ = NativeMethods.FillRectangle(dc, ref area, brush); }
        }
        finally
        {
            if (brush != 0) { _ = NativeMethods.DeleteObject(brush); }
            if (target == 0) { _ = NativeMethods.ReleaseDeviceContext(window, dc); }
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowDC")]
    private static extern nint GetWindowDeviceContext(nint window);
}
