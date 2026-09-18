using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Augit.Shell;

/// <summary>
/// 同一工作区的单实例登记表（从 Program 搬出，桥接的「打开工作区」也要用它）。
/// </summary>
internal static class WorkspaceWindowRegistry
{
    /// <summary>
    /// 同一工作区的单实例登记表。
    /// 用内存映射登记首个实例的窗口句柄：窗口标题同步在 WebView2 下不可靠
    /// （已实测），因此按句柄定位而不是按标题匹配。
    /// </summary>
    public static string RegistryName(string workspaceRoot)
    {
        // 用工作区全路径的稳定散列作为名字，避免路径中的非法字符。
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(workspaceRoot).ToLowerInvariant()));
        return "Augit.Shell." + Convert.ToHexString(hash, 0, 16);
    }

    /// <summary>把本窗口句柄登记到该工作区的共享内存，供后续实例激活。</summary>
    public static System.IO.MemoryMappedFiles.MemoryMappedFile? Register(nint window, string workspaceRoot)
    {
        try
        {
            System.IO.MemoryMappedFiles.MemoryMappedFile file =
                System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(RegistryName(workspaceRoot), sizeof(long));
            using System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = file.CreateViewAccessor();
            accessor.Write(0, window);
            return file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 该工作区已有实例时激活其窗口并返回 true。
    /// 只在登记的句柄仍是本应用的有效窗口时激活：进程崩溃留下的陈旧句柄会被忽略，
    /// 此时新实例正常启动并接管登记。
    /// </summary>
    public static bool TryActivate(string workspaceRoot)
    {
        try
        {
            using System.IO.MemoryMappedFiles.MemoryMappedFile? file =
                System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(RegistryName(workspaceRoot));
            using System.IO.MemoryMappedFiles.MemoryMappedViewAccessor accessor = file.CreateViewAccessor();
            long window = accessor.ReadInt64(0);
            if (window == 0)
            {
                return false;
            }

            nint handle = (nint)window;
            if (!IsWindow(handle) || !IsWindowVisible(handle))
            {
                return false;
            }

            _ = ShowWindow(handle, SwRestore);
            _ = SetForegroundWindow(handle);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

}
