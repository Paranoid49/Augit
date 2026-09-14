using System.Runtime.InteropServices;

namespace Augit.App.Tests;

internal static class NativeFindTestPump
{
    internal static void Wait(NativeDocumentView view)
    {
        long deadline = Environment.TickCount64 + 5_000;
        while (true)
        {
            Dispatch(view.Handle);
            if (!view.FindBusyForTest && view.FindWorkersForTest.IsCompleted) break;
            if (Environment.TickCount64 >= deadline) Assert.Fail("查找任务没有在限定时间内完成。");
            Thread.Sleep(1);
        }
        view.FindWorkersForTest.GetAwaiter().GetResult();
        Dispatch(view.Handle);
    }

    internal static void Dispatch(nint window)
    {
        if (window == 0) return;
        while (PeekMessage(out NativeMethods.Message message, window, 0, 0, 1))
        {
            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMethods.Message message, nint window, uint minimum, uint maximum, uint flags);
}
