using System.Runtime.InteropServices;

namespace Augit.App;

internal static class NativeClipboard
{
    internal static bool TrySetText(nint owner, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!NativeMethods.OpenClipboard(owner))
        {
            return false;
        }

        nint memory = 0;
        bool transferred = false;
        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }

            nuint bytes = checked((nuint)((text.Length + 1) * sizeof(char)));
            memory = NativeMethods.GlobalAllocate(NativeMethods.GlobalMemoryMoveable, bytes);
            if (memory == 0)
            {
                return false;
            }

            nint destination = NativeMethods.GlobalLock(memory);
            if (destination == 0)
            {
                return false;
            }

            try
            {
                char[] characters = new char[text.Length + 1];
                text.CopyTo(0, characters, 0, text.Length);
                Marshal.Copy(characters, 0, destination, characters.Length);
            }
            finally
            {
                _ = NativeMethods.GlobalUnlock(memory);
            }

            transferred = NativeMethods.SetClipboardData(NativeMethods.ClipboardUnicodeText, memory) != 0;
            return transferred;
        }
        finally
        {
            if (!transferred && memory != 0)
            {
                _ = NativeMethods.GlobalFree(memory);
            }

            _ = NativeMethods.CloseClipboard();
        }
    }
}
