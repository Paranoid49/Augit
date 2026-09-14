namespace Augit.App.Tests;

internal static class NativeImageTestPump
{
    internal static void Wait(NativeDocumentView view, Task? operation = null)
    {
        long deadline = Environment.TickCount64 + 10_000;
        while (view.ImageLoadingForTest || !view.ImageWorkersForTest.IsCompleted || operation?.IsCompleted == false)
        {
            NativeFindTestPump.Dispatch(view.Handle);
            if (Environment.TickCount64 >= deadline) Assert.Fail("图片加载未在限定时间内完成。");
            Thread.Sleep(1);
        }
        view.ImageWorkersForTest.GetAwaiter().GetResult();
        operation?.GetAwaiter().GetResult();
        NativeFindTestPump.Dispatch(view.Handle);
    }
}
