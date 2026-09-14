using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 在原生模态窗口打开期间降低主窗口视觉权重。
/// </summary>
internal sealed class NativeModalScrim : IDisposable
{
    private const string WindowClassName = "Augit.ModalScrim.Native";
    // 视觉稿的遮罩不是黑色半透明层，而是以主题冷灰色轻微覆盖背景。
    // 浅色主题约 32%，深色主题约 44%；这样既降低背景权重，又不会把背景压成脏灰。
    private const byte LightScrimAlpha = 82;
    private const byte DarkScrimAlpha = 112;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeModalScrim> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static readonly NativeMethods.SubclassProcedure OwnerProcedure = HandleOwnerMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly bool _dark;
    private readonly byte _scrimAlpha;
    private nint _handle;
    private (int Width, int Height)? _bounds;
    private bool _disposed;

    private NativeModalScrim(nint owner, bool dark)
    {
        _dark = dark;
        _scrimAlpha = dark ? DarkScrimAlpha : LightScrimAlpha;
        _owner = NativeMethods.GetAncestor(owner, NativeMethods.GetAncestorRoot);
        if (_owner == 0)
        {
            _owner = owner;
        }

        EnsureWindowClass();
        _handle = NativeMethods.CreateWindow(
            NativeMethods.WindowExtendedStyleLayered,
            WindowClassName,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleClipSiblings,
            0,
            0,
            0,
            0,
            _owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "模态遮罩创建失败。");
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        try
        {
            if (!NativeMethods.SetWindowSubclass(_owner, OwnerProcedure, unchecked((nuint)_handle), unchecked((nuint)_handle)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "模态遮罩无法跟随宿主窗口。");
            if (!NativeMethods.SetLayeredWindowAttributes(_handle, 0, _scrimAlpha, NativeMethods.LayeredWindowAttributesAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "模态遮罩透明度设置失败。");
            Resize();
            Raise();
        }
        catch { Dispose(); throw; }
    }

    internal static NativeModalScrim Begin(nint owner)
    {
        return Begin(owner, NativeTheme.IsDark("Follow Windows"));
    }

    internal static NativeModalScrim Begin(nint owner, bool dark)
    {
        return new NativeModalScrim(owner, dark);
    }

    internal static byte LightAlphaForTest => LightScrimAlpha;

    internal static byte DarkAlphaForTest => DarkScrimAlpha;

    internal nint HandleForTest => _handle;
    internal int ResizeCountForTest { get; private set; }
    internal static int RegistrationCountForTest { get { lock (InstancesGate) return Instances.Count; } }

    // 视觉稿最终的浅色遮罩使用 #EEF1F6，深色继续复用主题 Chrome。
    internal static uint ScrimColorForTest(bool dark) => dark ? NativeTheme.Palette(true).Chrome : 0x00F6F1EE;

    internal static bool CoversOwnerForTest(
        NativeMethods.Rectangle ownerClient,
        NativeMethods.Rectangle scrimClient)
    {
        return ownerClient.Left == scrimClient.Left
            && ownerClient.Top == scrimClient.Top
            && ownerClient.Right == scrimClient.Right
            && ownerClient.Bottom == scrimClient.Bottom;
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
                throw new Win32Exception(error, "模态遮罩窗口类注册失败。");
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter)
    {
        NativeModalScrim? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        return message switch
        {
            NativeMethods.WindowMessagePaint => instance.Paint(),
            NativeMethods.WindowMessageEraseBackground => 1,
            NativeMethods.WindowMessageNonClientHitTest => NativeMethods.HitTestTransparent,
            NativeMethods.WindowMessageSize => instance.ResizeMessage(),
            _ => NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter),
        };
    }

    private nint ResizeMessage()
    {
        Resize();
        return 0;
    }

    private static nint HandleOwnerMessage(nint window, uint message, nuint word, nint value, nuint id, nuint data)
    {
        NativeModalScrim? scrim;
        lock (InstancesGate) Instances.TryGetValue(unchecked((nint)data), out scrim);
        nint result = NativeMethods.DefaultSubclassProcedure(window, message, word, value);
        if (scrim is null || scrim._disposed) return result;
        if (message == 0x0082) scrim.Dispose(); // WM_NCDESTROY。
        else if (message == NativeMethods.WindowMessageSize) scrim.Resize();
        return result;
    }

    private void Resize()
    {
        if (_handle == 0 || !NativeMethods.GetClientRectangle(_owner, out NativeMethods.Rectangle client))
        {
            return;
        }

        var next = (Math.Max(0, client.Right - client.Left), Math.Max(0, client.Bottom - client.Top));
        if (_bounds == next) return;
        _bounds = next;
        ResizeCountForTest++;
        _ = NativeMethods.SetWindowPosition(
            _handle,
            NativeMethods.WindowPositionTop,
            0,
            0,
            Math.Max(0, client.Right - client.Left),
            Math.Max(0, client.Bottom - client.Top),
            NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionNoZOrder
                | NativeMethods.SetWindowPositionShowWindow);
    }

    private nint Paint()
    {
        nint deviceContext = NativeMethods.BeginPaint(_handle, out NativeMethods.PaintStructure paint);
        if (deviceContext == 0)
        {
            return 0;
        }

        try
        {
            if (NativeMethods.GetClientRectangle(_handle, out NativeMethods.Rectangle client))
            {
                nint brush = NativeMethods.CreateSolidBrush(
                    ScrimColorForTest(_dark));
                if (brush != 0)
                {
                    _ = NativeMethods.FillRectangle(deviceContext, ref client, brush);
                    _ = NativeMethods.DeleteObject(brush);
                }
            }
        }
        finally
        {
            _ = NativeMethods.EndPaint(_handle, ref paint);
        }

        return 0;
    }

    private void Raise()
    {
        if (_handle == 0)
        {
            return;
        }

        _ = NativeMethods.SetWindowPosition(
            _handle,
            NativeMethods.WindowPositionTop,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate
                | NativeMethods.SetWindowPositionShowWindow);
        _ = NativeMethods.InvalidateRectangle(_handle, 0, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        nint handle = _handle;
        _ = NativeMethods.RemoveWindowSubclass(_owner, OwnerProcedure, unchecked((nuint)handle));
        _handle = 0;
        _bounds = null;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.ShowWindow(handle, NativeMethods.ShowHide);
            _ = NativeMethods.DestroyWindow(handle);
        }

        GC.SuppressFinalize(this);
    }
}
