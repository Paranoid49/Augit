using System.Runtime.InteropServices;

namespace Augit.App;

/// <summary>
/// 通过 Windows 动态辅助功能注释为原生控件补充可读名称，不改变控件可见文字。
/// </summary>
internal static class NativeAccessibility
{
    private const uint ObjectIdClient = 0xFFFFFFFC;
    private const uint ChildIdSelf = 0;
    private static readonly Guid AccessibleNameProperty = new(
        0x608D3DF8,
        0x8128,
        0x4AA7,
        0xA4,
        0x28,
        0xF5,
        0x5E,
        0x49,
        0x26,
        0x72,
        0x91);
    private static readonly Guid AccessibleInterface = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private static readonly object Gate = new();

    internal static bool SetName(nint control, string name)
    {
        if (control == 0 || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            lock (Gate)
            {
                // IAccPropServices 由当前线程的 COM 公寓创建；不能把 RCW 跨 STA 线程缓存。
                IAccessiblePropertyServices services = (IAccessiblePropertyServices)new AccessiblePropertyServices();
                Guid property = AccessibleNameProperty;
                try
                {
                    return services.SetWindowPropertyString(
                        control,
                        ObjectIdClient,
                        ChildIdSelf,
                        ref property,
                        name.Trim()) >= 0;
                }
                finally
                {
                    if (Marshal.IsComObject(services))
                    {
                        _ = Marshal.FinalReleaseComObject(services);
                    }
                }
            }
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidComObjectException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    internal static string? GetNameForTest(nint control)
    {
        if (control == 0)
        {
            return null;
        }

        Guid accessibleInterface = AccessibleInterface;
        int result = AccessibleObjectFromWindow(
            control,
            ObjectIdClient,
            ref accessibleInterface,
            out IAccessibleName? accessible);
        if (result < 0 || accessible is null)
        {
            return null;
        }

        try
        {
            return accessible.GetName(unchecked((int)ChildIdSelf));
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (Marshal.IsComObject(accessible))
            {
                _ = Marshal.FinalReleaseComObject(accessible);
            }
        }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint window,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessibleName? accessible);

    [ComImport]
    [Guid("B5F8350B-0548-48B1-A6EE-88BD00B4A5E7")]
    private class AccessiblePropertyServices;

    [ComImport]
    [Guid("6E26E776-04F0-495D-80E4-3330352E3169")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessiblePropertyServices
    {
        [PreserveSig]
        int SetPropertyValue(
            nint identity,
            uint identityLength,
            ref Guid property,
            [MarshalAs(UnmanagedType.Struct)] object value);

        [PreserveSig]
        int SetPropertyServer(
            nint identity,
            uint identityLength,
            nint properties,
            int propertyCount,
            nint server,
            uint annotationScope);

        [PreserveSig]
        int ClearProperties(
            nint identity,
            uint identityLength,
            nint properties,
            int propertyCount);

        [PreserveSig]
        int SetWindowProperty(
            nint window,
            uint objectId,
            uint childId,
            ref Guid property,
            [MarshalAs(UnmanagedType.Struct)] object value);

        [PreserveSig]
        int SetWindowPropertyString(
            nint window,
            uint objectId,
            uint childId,
            ref Guid property,
            [MarshalAs(UnmanagedType.LPWStr)] string value);
    }

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessibleName
    {
        [DispId(-5000)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? GetParent();

        [DispId(-5001)]
        int GetChildCount();

        [DispId(-5002)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? GetChild([MarshalAs(UnmanagedType.Struct)] object childId);

        [DispId(-5003)]
        [return: MarshalAs(UnmanagedType.BStr)]
        string? GetName([MarshalAs(UnmanagedType.Struct)] object childId);
    }
}
