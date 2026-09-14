using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed partial class ScintillaControl
{
    private const uint GetScrollWidth = 2275;
    private const nuint ViewportSubclassIdentifier = 2;
    private static readonly object ViewportGate = new();
    private static readonly Dictionary<nint, ScintillaControl> Viewports = [];
    private static readonly NativeMethods.SubclassProcedure ViewportProcedure = HandleViewportMessage;
    private int _appliedScrollWidth = 1;
    private bool _viewportNeedsRefresh;
    private bool _updatingViewport;

    internal static int ViewportRegistrationCountForTest
    {
        get { lock (ViewportGate) { return Viewports.Count; } }
    }

    private void RegisterViewport()
    {
        lock (ViewportGate) { Viewports.Add(Handle, this); }
        if (NativeMethods.SetWindowSubclass(Handle, ViewportProcedure, ViewportSubclassIdentifier, 0)) return;
        int error = Marshal.GetLastWin32Error();
        lock (ViewportGate) { Viewports.Remove(Handle); }
        _ = NativeMethods.DestroyWindow(Handle);
        Handle = 0;
        throw new Win32Exception(error, "无法注册正文滚动范围更新。");
    }

    private static nint HandleViewportMessage(nint window, uint message, nuint wordParameter,
        nint longParameter, nuint subclassIdentifier, nuint referenceData)
    {
        ScintillaControl? view;
        lock (ViewportGate) { Viewports.TryGetValue(window, out view); }
        if (message == 0x0082)
        {
            view?.ReleaseBlameDrawing();
            lock (ViewportGate) { Viewports.Remove(window); }
            _ = NativeMethods.RemoveWindowSubclass(window, ViewportProcedure, ViewportSubclassIdentifier);
            return NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        }

        nint result = NativeMethods.DefaultSubclassProcedure(window, message, wordParameter, longParameter);
        if (view is not null && message is NativeMethods.WindowMessagePaint or 0x0318)
        {
            view.UpdateHorizontalViewport();
            view.DrawBlameMargin(message == 0x0318 ? unchecked((nint)wordParameter) : 0);
        }
        return result;
    }

    private void UpdateHorizontalViewport()
    {
        if (_disposed || _updatingViewport || Handle == 0) return;
        int measured = (int)NativeMethods.SendMessage(Handle, GetScrollWidth, 0, 0);
        if (!_viewportNeedsRefresh && measured == _appliedScrollWidth) return;
        _updatingViewport = true;
        try
        {
            // 原生宽度跟踪完成于绘制；提交一次新宽度使 Windows 滚动条同步。
            // 保留一物理像素末端空隙，只在内容度量发生变化时执行，稳定帧不增长。
            _appliedScrollWidth = Math.Min(int.MaxValue - 1, measured) + 1;
            _ = NativeMethods.SendMessage(Handle, SetScrollWidth, (nuint)_appliedScrollWidth, 0);
            _viewportNeedsRefresh = false;
            if (!NativeMethods.GetClientRectangle(Handle, out NativeMethods.Rectangle client)) return;
            int margins = (int)NativeMethods.SendMessage(Handle, 2156, 0, 0)
                + (int)NativeMethods.SendMessage(Handle, 2158, 0, 0);
            int marginCount = MarginCount;
            for (int index = 0; index < marginCount; index++)
                margins += (int)NativeMethods.SendMessage(Handle, 2243, (nuint)index, 0);
            int maximum = Math.Max(0, _appliedScrollWidth - Math.Max(1, client.Right - margins));
            int offset = (int)NativeMethods.SendMessage(Handle, GetXOffset, 0, 0);
            if (offset > maximum)
                _ = NativeMethods.SendMessage(Handle, SetXOffset, (nuint)maximum, 0);
        }
        finally { _updatingViewport = false; }
    }

}
