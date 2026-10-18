using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Augit.Shell.Tests;

[TestClass]
public sealed class ShellSettingsCaptureTimeoutTests
{
    [TestMethod]
    public async Task 捕获超过上限时返回false且不无限等待()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Stopwatch elapsed = Stopwatch.StartNew();
        bool finished = await ShellWindow.WaitForCaptureAsync(completion.Task, TimeSpan.FromMilliseconds(30));
        elapsed.Stop();

        Assert.IsFalse(finished);
        Assert.IsLessThan(250L, elapsed.ElapsedMilliseconds, $"等待上限未生效：{elapsed.ElapsedMilliseconds}ms");
        completion.SetResult();
        await completion.Task;
    }

    [TestMethod]
    public async Task 捕获正常完成或异常按原任务返回()
    {
        Assert.IsTrue(await ShellWindow.WaitForCaptureAsync(Task.CompletedTask, TimeSpan.FromMilliseconds(100)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ShellWindow.WaitForCaptureAsync(Task.FromException(new InvalidOperationException("capture failed")), TimeSpan.FromMilliseconds(100)));
    }
}
