using System.Runtime.InteropServices;

namespace Augit.Shell;

/// <summary>按需读取本机字体族；缓存只保留名称，设备上下文在枚举结束后立即释放。</summary>
internal static class ShellFontCatalog
{
    private static readonly Lazy<string[]> CachedFamilies = new(ReadFamilies);

    internal static IReadOnlyList<string> Families => CachedFamilies.Value;

    private static string[] ReadFamilies()
    {
        nint context = GetDC(0);
        if (context == 0)
        {
            return [];
        }

        try
        {
            HashSet<string> families = new(StringComparer.OrdinalIgnoreCase);
            LogFont filter = new() { CharacterSet = 1, FaceName = string.Empty };
            FontEnum callback = (font, _, _, _) =>
            {
                string name = Marshal.PtrToStructure<LogFont>(font).FaceName?.Trim() ?? string.Empty;
                if (name.Length > 0 && !name.StartsWith('@'))
                {
                    families.Add(name);
                }

                return 1;
            };
            _ = EnumFontFamiliesExW(context, ref filter, callback, 0, 0);
            GC.KeepAlive(callback);
            return families.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _ = ReleaseDC(0, context);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharacterSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    private delegate int FontEnum(nint font, nint metrics, uint type, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint context);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int EnumFontFamiliesExW(nint context, ref LogFont filter, FontEnum callback, nint parameter, uint flags);
}
