using System.Runtime.InteropServices;

namespace Augit.Shell;

/// <summary>
/// 系统文件夹选择框（产品规格 §2 的「选择目录…」/ 视觉稿「打开工作区」页）。
///
/// 用 <c>SHBrowseForFolder</c> 而不是 COM 的 <c>IFileOpenDialog</c>：
/// 后者要按系统定义的 vtable 顺序声明一整套接口，写错会在真机上直接崩溃；
/// 这个 API 只有两个函数与一个结构体，暴露面小得多，功能上同样是"选一个目录"。
/// </summary>
internal static class FolderPicker
{
    private const uint BrowseReturnOnlyFsDirs = 0x0001;
    private const uint BrowseNewDialogStyle = 0x0040;
    private const uint BrowseEditBox = 0x0010;

    /// <summary>弹出选择框；用户取消或调用失败时返回 null（调用方据此如实回话）。</summary>
    public static string? Pick(string title)
    {
        try
        {
            BrowseInfo info = new()
            {
                Owner = GetForegroundWindow(),
                DisplayName = string.Empty,
                Title = title,
                Flags = BrowseReturnOnlyFsDirs | BrowseNewDialogStyle | BrowseEditBox,
            };

            nint idList = SHBrowseForFolder(ref info);
            if (idList == 0)
            {
                return null;
            }

            try
            {
                char[] buffer = new char[260];
                return SHGetPathFromIDList(idList, buffer) ? new string(buffer).TrimEnd('\0') : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(idList);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // 外壳与 shell32 的正常组合不会走到这里；真出现时按"没选到目录"处理，不抛给界面。
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public nint Owner;
        public nint Root;
        [MarshalAs(UnmanagedType.LPWStr)] public string DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public uint Flags;
        public nint Callback;
        public nint Parameter;
        public int Image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHBrowseForFolderW")]
    private static extern nint SHBrowseForFolder(ref BrowseInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetPathFromIDListW")]
    private static extern bool SHGetPathFromIDList(nint idList, char[] path);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
