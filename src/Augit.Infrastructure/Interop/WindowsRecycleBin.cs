using System.Runtime.InteropServices;

namespace Augit.Infrastructure.Interop;

internal static class WindowsRecycleBin
{
    private const uint FileOperationDelete = 3;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort Silent = 0x0004;
    private const ushort NoErrorUi = 0x0400;

    internal static RecycleBinResult Move(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(false, "待删除路径无效。");
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new(false, "待删除文件已经不存在，请刷新后重试。");
        }

        FileOperation operation = new()
        {
            Function = FileOperationDelete,
            From = string.Concat(fullPath, "\0\0"),
            Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi,
        };
        int result = ShellFileOperation(ref operation);
        if (result == 0 && !operation.AnyOperationsAborted)
        {
            return new(true, null);
        }

        return new(
            false,
            operation.AnyOperationsAborted
                ? "删除操作已取消，文件未进入回收站。"
                : $"Windows 回收站操作失败，错误码 {result}。");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        internal nint Window;
        internal uint Function;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? To;

        internal ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool AnyOperationsAborted;

        internal nint NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ProgressTitle;
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)]
    private static extern int ShellFileOperation(ref FileOperation operation);
}

internal sealed record RecycleBinResult(bool IsSuccess, string? ErrorMessage);
