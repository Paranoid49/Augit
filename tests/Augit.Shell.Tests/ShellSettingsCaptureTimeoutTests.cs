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
    public async Task 捕获上限为500毫秒时在超时后返回false()
    {
        // 生产代码的硬上限固定为 500ms；用永不完成的任务直接测这个真实上限，
        // 避免只测一个更短的测试值而无法证明设置窗口不会被拖到秒级。
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Stopwatch elapsed = Stopwatch.StartNew();
        bool finished = await ShellWindow.WaitForCaptureAsync(completion.Task, TimeSpan.FromMilliseconds(500));
        elapsed.Stop();

        Assert.IsFalse(finished);
        Assert.IsTrue(elapsed.ElapsedMilliseconds >= 400 && elapsed.ElapsedMilliseconds < 1200,
            $"500ms 上限未按预期生效：{elapsed.ElapsedMilliseconds}ms");
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
