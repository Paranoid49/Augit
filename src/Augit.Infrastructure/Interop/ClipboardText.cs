using System;
using System.Runtime.InteropServices;

namespace Augit.Infrastructure.Interop;

/// <summary>
/// 系统剪贴板的文本读写。
/// </summary>
/// <remarks>
/// 放在基础设施层而不是外壳里，是为了让 Win32 调用可被自动化测试覆盖：
/// 外壳只做参数校验与结果映射，真正的 P/Invoke 逻辑在这里，
/// 测试用「写入后读回」验证整对调用（含所有权转移与内存释放）。
///
/// 用 Win32 剪贴板 API 而不是 WinForms：只为一次复制引入整个 WinForms，
/// 会与 `app.manifest` 的自定义 DPI 设置冲突（实测报 WFO0003，
/// 要求从清单删除 DPI 配置）。为一个剪贴板改动应用清单是不划算的取舍。
/// </remarks>
public static class ClipboardText
{
    private const uint GlobalMemoryMoveable = 0x0002;
    private const uint UnicodeTextFormat = 13;

    /// <summary>把文本写入系统剪贴板。</summary>
    public static ClipboardWriteResult TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return ClipboardWriteResult.Failure("没有可复制的文本。");
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return ClipboardWriteResult.Failure("系统剪贴板当前被其他程序占用。");
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                return ClipboardWriteResult.Failure("系统剪贴板当前不可用。");
            }

            // CF_UNICODETEXT：按 UTF-16 写入并以 NUL 结尾。
            int bytes = (text.Length + 1) * 2;
            handle = GlobalAlloc(GlobalMemoryMoveable, (UIntPtr)bytes);
            if (handle == IntPtr.Zero)
            {
                return ClipboardWriteResult.Failure("系统内存不足，无法复制。");
            }

            IntPtr target = GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                return ClipboardWriteResult.Failure("系统剪贴板当前不可用。");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(UnicodeTextFormat, handle) == IntPtr.Zero)
            {
                return ClipboardWriteResult.Failure("系统剪贴板当前不可用。");
            }

            // 所有权已转移给剪贴板，不能再释放。
            handle = IntPtr.Zero;
            return ClipboardWriteResult.Success();
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                GlobalFree(handle);
            }

            CloseClipboard();
        }
    }

    /// <summary>读取系统剪贴板中的文本；没有文本时返回 <c>null</c>。</summary>
    /// <remarks>主要供测试验证写入结果，产品路径不需要读取剪贴板。</remarks>
    public static string? TryGetText()
    {
        if (!IsClipboardFormatAvailable(UnicodeTextFormat) || !OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            IntPtr handle = GetClipboardData(UnicodeTextFormat);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr source = GlobalLock(handle);
            if (source == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(source);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}

/// <summary>剪贴板写入结果。</summary>
public sealed record ClipboardWriteResult(bool IsSuccess, string? ErrorMessage)
{
    public static ClipboardWriteResult Success() => new(true, null);

    public static ClipboardWriteResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, errorMessage);
    }
}
