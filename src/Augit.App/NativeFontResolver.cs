using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Augit.App;

internal static class NativeFontResolver
{
    internal const string DefaultInterfaceFamily = "Microsoft YaHei UI";
    internal const string DefaultMonospaceFamily = "Cascadia Mono";
    private static readonly ConcurrentDictionary<string, bool> AvailableFamilies = new(StringComparer.OrdinalIgnoreCase);

    internal static string ResolveInterface(string? requested)
    {
        return Resolve(requested, [DefaultInterfaceFamily, "Segoe UI", "Microsoft Sans Serif"]);
    }

    internal static string ResolveMonospace(string? requested)
    {
        return Resolve(requested, [DefaultMonospaceFamily, "Consolas", "Courier New"]);
    }

    private static string Resolve(string? requested, ReadOnlySpan<string> fallbacks)
    {
        string family = requested?.Trim() ?? string.Empty;
        if (IsInstalled(family)) return family;
        foreach (string fallback in fallbacks)
        {
            if (IsInstalled(fallback)) return fallback;
        }
        return fallbacks[^1];
    }

    internal static bool IsInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family) || family.Length >= 32) return false;
        return AvailableFamilies.GetOrAdd(family, QueryInstalledFamily);
    }

    private static bool QueryInstalledFamily(string family)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(0);
        if (deviceContext == 0) return false;
        try
        {
            // CreateFont 即使找不到字体也会返回句柄，必须枚举指定字体以避免无声替换为比例字体。
            LogicalFont font = new() { CharacterSet = 1, FaceName = family };
            bool found = false;
            FontEnumeration callback = (_, _, _, _) => { found = true; return 0; };
            _ = EnumFontFamiliesEx(deviceContext, ref font, callback, 0, 0);
            GC.KeepAlive(callback);
            return found;
        }
        finally
        {
            _ = NativeMethods.ReleaseDeviceContext(0, deviceContext);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogicalFont
    {
        internal int Height, Width, Escapement, Orientation, Weight;
        internal byte Italic, Underline, StrikeOut, CharacterSet, OutputPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string FaceName;
    }

    private delegate int FontEnumeration(nint font, nint metrics, uint fontType, nint parameter);

    [DllImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesEx(nint deviceContext, ref LogicalFont font, FontEnumeration callback, nint parameter, uint flags);
}
