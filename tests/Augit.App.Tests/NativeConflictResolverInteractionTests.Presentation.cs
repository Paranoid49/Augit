namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly int[] ConflictSideIds = [20, 22];
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 大正文计算期间继续输入取消旧版本且过期成功或异常不覆盖最新内容(bool throws) => RunAsync(async (dialog, service) =>
    {
        int uiThread = Environment.CurrentManagedThreadId;
        int workerThread = 0;
        int runs = 0;
        TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken previousToken = default;
        dialog.PresentationBarrierForTest = token =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            if (Interlocked.Increment(ref runs) != 1) return Task.CompletedTask;
            previousToken = token;
            arrived.SetResult();
            return release.Task;
        };
        try
        {
            Append(dialog.ResultHandleForTest, new string('x', 110_000) + "\n");
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreNotEqual(uiThread, workerThread);
            Assert.IsTrue(dialog.ResultPresentationPendingForTest);
            Assert.IsFalse(dialog.ResultIsReadOnlyForTest);
            Assert.IsTrue(NativeMethods.IsWindowEnabled(Item(dialog, 7)), "后台计算不能冻结关闭和正文输入。");
            for (int id = 1; id <= 6; id++) Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, id)));
            await dialog.SaveForTestAsync();
            Assert.IsEmpty(service.Saves, "不能用过期解析结果保存。");
            Append(dialog.ResultHandleForTest, UndoConflict);
            Assert.IsTrue(previousToken.IsCancellationRequested);
            string current = dialog.ResultTextForTest!;
            _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2160, 9, 3);
            if (throws) release.SetException(new IOException("旧计算失败")); else release.SetResult();
            await WaitForConflictPresentationAsync(dialog);
            Assert.AreEqual(current, dialog.ResultTextForTest);
            Assert.AreEqual(uiThread, dialog.PresentationThreadForTest);
            Assert.Contains("未处理冲突块：2", dialog.NoticeForTest);
            Assert.AreEqual((nint)9, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2009, 0, 0));
            Assert.AreEqual((nint)3, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2008, 0, 0));
            _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
            await WaitForConflictPresentationAsync(dialog);
            Assert.Contains("未处理冲突块：1", dialog.NoticeForTest);
        }
        finally
        {
            release.TrySetResult();
            dialog.Dispose();
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }, unresolved: true);

    [TestMethod]
    public Task 接近上限的真实正文编辑更新期间仍响应消息且关闭结束计算()
    {
        string yours = string.Concat(Enumerable.Repeat(new string('a', 90) + "\n", 50_000));
        string theirs = string.Concat(Enumerable.Repeat(new string('b', 90) + "\n", 50_000));
        string result = "<<<<<<< HEAD\n" + yours + "=======\n" + theirs + ">>>>>>> feature\n";
        return RunAsync(async (dialog, service) =>
        {
            try
            {
                await WaitForConflictPresentationAsync(dialog);
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                Append(dialog.ResultHandleForTest, "继续输入😀\n");
                double inputMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                int heartbeats = 0;
                double largestGap = 0;
                long last = System.Diagnostics.Stopwatch.GetTimestamp();
                while (dialog.ResultPresentationPendingForTest)
                {
                    await Task.Delay(5);
                    largestGap = Math.Max(largestGap, System.Diagnostics.Stopwatch.GetElapsedTime(last).TotalMilliseconds);
                    last = System.Diagnostics.Stopwatch.GetTimestamp();
                    Assert.IsTrue(NativeMethods.IsWindow(dialog.HandleForTest));
                    heartbeats++;
                    Assert.IsLessThan(10_000d, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                Assert.IsGreaterThan(0, heartbeats);
                Assert.IsGreaterThan(1, dialog.PresentationSlicesForTest);
                Assert.AreEqual(result + "继续输入😀\n", dialog.ResultTextForTest);
                Assert.Contains("未处理冲突块：1", dialog.NoticeForTest);
                Assert.IsTrue(dialog.YoursIsReadOnlyForTest && dialog.TheirsIsReadOnlyForTest);
                TestContext.WriteLine($"正文 {System.Text.Encoding.UTF8.GetByteCount(result)} 字节，输入耗时 {inputMs:F1} ms，消息心跳最大间隔 {largestGap:F1} ms，装饰批次 {dialog.PresentationSlicesForTest}。此为同机诊断，不替代正式性能验收。");
                Append(dialog.ResultHandleForTest, "准备关闭");
                dialog.Dispose();
                await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally
            {
                dialog.Dispose();
                await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }, resultText: result, yoursText: yours, theirsText: theirs);
    }

    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 144)]
    public Task 多块大正文后台定位及分片装饰与原生标记一致且接受可撤销(string theme, int dpi)
    {
        const int count = 2_000;
        string result = string.Concat(Enumerable.Repeat(UndoConflict, count));
        string yours = string.Concat(Enumerable.Repeat("中文😀左\n", count));
        string theirs = string.Concat(Enumerable.Repeat("中文😀右\n", count));
        return RunAsync(async (dialog, service) =>
        {
            try
            {
                Assert.IsTrue(dialog.ResultPresentationPendingForTest);
                await WaitForConflictPresentationAsync(dialog);
                Assert.AreEqual(result, dialog.ResultTextForTest);
                Assert.IsFalse(dialog.ResultIsDirtyForTest);
                Assert.Contains($"未处理冲突块：{count}", dialog.NoticeForTest);
                int parseCount = dialog.ResultParseCountForTest;
                Assert.IsGreaterThan(0, dialog.PresentationSlicesForTest);
                foreach (int line in new[] { 0, result.Count(c => c == '\n') - 1 })
                    Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2046, (nuint)line, 0));
                foreach (int side in ConflictSideIds)
                    Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(Item(dialog, side), 2046, count - 1, 0));
                Assert.AreEqual((nint)0, NativeMethods.SendMessage(dialog.ResultHandleForTest, 2046, (nuint)result.Count(c => c == '\n'), 0));
                ClickConflictAction(dialog, 5);
                ClickConflictAction(dialog, 4);
                Assert.AreEqual(parseCount, dialog.ResultParseCountForTest, "导航不能再次扫描大正文。");
                ClickConflictAction(dialog, 1);
                Assert.AreEqual("中文😀左\n" + result[UndoConflict.Length..], dialog.ResultTextForTest);
                await WaitForConflictPresentationAsync(dialog);
                Assert.Contains($"未处理冲突块：{count - 1}", dialog.NoticeForTest);
                _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
                await WaitForConflictPresentationAsync(dialog);
                Assert.AreEqual(result, dialog.ResultTextForTest);
                Assert.IsFalse(dialog.ResultIsDirtyForTest);
                Assert.Contains($"未处理冲突块：{count}", dialog.NoticeForTest);
            }
            finally
            {
                dialog.Dispose();
                await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }, resultText: result, yoursText: yours, theirsText: theirs, theme: theme, dpi: dpi);
    }

    [TestMethod]
    public Task 关闭取消后台解析且晚到失败不触碰销毁窗口() => RunAsync(async (dialog, service) =>
    {
        TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken activeToken = default;
        dialog.PresentationBarrierForTest = token => { activeToken = token; arrived.SetResult(); return release.Task; };
        try
        {
            Append(dialog.ResultHandleForTest, new string('x', 110_000));
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            dialog.Dispose();
            Assert.IsTrue(activeToken.IsCancellationRequested);
            release.SetException(new IOException("窗口关闭后的旧错误"));
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual((nint)0, dialog.HandleForTest);
            Assert.IsEmpty(service.Saves);
        }
        finally
        {
            release.TrySetResult();
            dialog.Dispose();
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }, unresolved: true);

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task 后台计算未结束时外部更新尊重重新载入或保留选择(bool reload) => RunAsync(async (dialog, service) =>
    {
        TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken activeToken = default;
        int entered = 0;
        dialog.PresentationBarrierForTest = token =>
        {
            if (Interlocked.Increment(ref entered) != 1) return Task.CompletedTask;
            activeToken = token;
            arrived.SetResult();
            return release.Task;
        };
        try
        {
            Append(dialog.ResultHandleForTest, new string('x', 110_000));
            string edited = dialog.ResultTextForTest!;
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Task reading = dialog.ReloadForTestAsync();
            Task choosing = ChooseReloadAsync(dialog.HandleForTest, reload);
            service.Reloads.Single().Completion.SetResult(service.Reloads.Single().Changed("外部已解决😀\n"));
            await reading;
            await choosing;
            Assert.IsTrue(activeToken.IsCancellationRequested);
            Assert.AreEqual(!reload, dialog.ResultPresentationPendingForTest);
            release.SetResult();
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitForConflictPresentationAsync(dialog);
            await Task.Delay(150);
            Assert.AreEqual(reload ? "外部已解决😀\n" : edited, dialog.ResultTextForTest);
            Assert.Contains(reload ? "已重新载入" : "已保留当前未保存内容", dialog.NoticeForTest);
            Assert.IsFalse(dialog.ResultPresentationPendingForTest);
            Assert.AreEqual(!reload, dialog.ResultIsDirtyForTest);
            Assert.AreEqual(!reload, NativeMethods.IsWindowEnabled(Item(dialog, 1)));
            Task saving = dialog.SaveForTestAsync();
            if (reload)
            {
                Assert.AreEqual("外部已解决😀\n", service.Saves.Single().ResultText);
                service.Pending.SetResult(Failure());
            }
            else Assert.IsEmpty(service.Saves);
            await saving;
        }
        finally
        {
            release.TrySetResult();
            dialog.Dispose();
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }, unresolved: true);

    [TestMethod]
    public Task 当前后台计算失败保留正文且下一次编辑能够恢复() => RunAsync(async (dialog, service) =>
    {
        dialog.PresentationBarrierForTest = _ => Task.FromException(new IOException("模拟定位失败"));
        try
        {
            Append(dialog.ResultHandleForTest, new string('x', 110_000));
            string current = dialog.ResultTextForTest!;
            await WaitForConflictPresentationAsync(dialog);
            Assert.Contains("模拟定位失败", dialog.NoticeForTest);
            Assert.AreEqual(current, dialog.ResultTextForTest);
            Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, 1)));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(Item(dialog, 6)));
            dialog.PresentationBarrierForTest = null;
            Append(dialog.ResultHandleForTest, "\n重试");
            await WaitForConflictPresentationAsync(dialog);
            Assert.AreEqual(current + "\n重试", dialog.ResultTextForTest);
            Assert.Contains("未处理冲突块：1", dialog.NoticeForTest);
            Assert.IsTrue(NativeMethods.IsWindowEnabled(Item(dialog, 1)));
            Assert.IsEmpty(service.Saves);
        }
        finally
        {
            dialog.Dispose();
            await dialog.PresentationWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }, unresolved: true);
}
