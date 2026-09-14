using System.Runtime.InteropServices;
using Augit.Core.Search;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeSearchQueryInteractionTests
{
    private static readonly int[] CompositionKeys =
        [NativeMethods.VirtualKeyEnter, NativeMethods.VirtualKeyEscape, NativeMethods.VirtualKeyTab];
    private static readonly string[] FailureNotices = ["搜索组件测试失败"];
    [TestMethod]
    public void 取消回调不占用窗口线程且旧任务退出后才启动最终查询()
    {
        using Scope scope = new(WorkspaceSearchMode.FileNames);
        using ManualResetEventSlim entered = new(), cancelling = new(), release = new();
        int uiThread = Environment.CurrentManagedThreadId, cancellationThread = uiThread, started = 0;
        scope.Panel.SearchBarrierForTest = async (options, token) =>
        {
            Interlocked.Increment(ref started);
            if (options.Query != "alpha") return;
            using var registration = token.Register(() =>
            {
                cancellationThread = Environment.CurrentManagedThreadId;
                cancelling.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            });
            entered.Set();
            await Task.Delay(Timeout.Infinite, token);
        };
        try
        {
            scope.Panel.SetQueryForTest("alpha");
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            var timer = System.Diagnostics.Stopwatch.StartNew();
            scope.Panel.SetQueryForTest("missing");
            scope.Panel.SetQueryForTest("beta");
            Assert.IsLessThan(250, timer.ElapsedMilliseconds, "取消不能等待结束进程的回调。");
            Assert.IsTrue(cancelling.Wait(TimeSpan.FromSeconds(3)));
            Assert.AreNotEqual(uiThread, cancellationThread);
            Assert.AreEqual(1, Volatile.Read(ref started), "旧任务还未退出，新查询不得启动。");
            release.Set();
            scope.Wait();
            Assert.AreEqual(2, started, "已被替换的中间查询不得进入搜索服务。");
            Assert.AreEqual(1, scope.Panel.SearchStartCountForTest);
            StringAssert.EndsWith(scope.Panel.ResultPathForTest(0)!, "beta.txt");
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void 空结果慢查询显示完整局部提示且完成不被晚到加载覆盖()
    {
        using Scope scope = new(WorkspaceSearchMode.Text);
        using ManualResetEventSlim entered = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Panel.SearchBarrierForTest = async (_, token) => { entered.Set(); await release.Task.WaitAsync(token); };
        try
        {
            scope.Panel.SetQueryForTest("alpha");
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            scope.WaitUntil(() => scope.Panel.NoticeTextForTest == UiText.Searching);
            Assert.IsTrue(scope.Panel.NoticeVisibleForTest);
            Assert.IsTrue(scope.Panel.NoticeWithinBoundsForTest, "空结果时也要为加载提示分配高度。");
            Assert.AreEqual(scope.Edit, NativeMethods.GetFocus());
            release.SetResult();
            scope.Wait();
            Assert.AreEqual(UiText.SearchResultCount(1), scope.Panel.NoticeTextForTest);
            Assert.IsTrue(scope.Panel.NoticeWithinBoundsForTest);
            Assert.IsEmpty(scope.Previewed);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public void 正则错误和无结果都有完整状态且改正后恢复搜索()
    {
        using Scope scope = new(WorkspaceSearchMode.Text);
        _ = NativeMethods.SendMessage(Child(scope.Panel.Handle, 6), 0x00F5, 0, 0);
        scope.Panel.SetQueryForTest("[");
        scope.Wait();
        Assert.AreEqual(0, scope.Panel.ResultCount);
        StringAssert.Contains(scope.Panel.NoticeTextForTest, "regex");
        Assert.IsTrue(scope.Panel.NoticeVisibleForTest);
        Assert.IsTrue(scope.Panel.NoticeWithinBoundsForTest);
        scope.Panel.SetQueryForTest("a.*a");
        scope.Wait();
        Assert.AreEqual(1, scope.Panel.ResultCount);
        scope.Panel.SetQueryForTest("missing");
        scope.Wait();
        Assert.AreEqual(UiText.SearchResultCount(0), scope.Panel.NoticeTextForTest);
        Assert.IsTrue(scope.Panel.NoticeWithinBoundsForTest);
        scope.Panel.SetQueryForTest(string.Empty);
        scope.Wait();
        Assert.IsFalse(scope.Panel.NoticeVisibleForTest);
        Assert.AreEqual(string.Empty, scope.Panel.NoticeTextForTest);
    }

    [TestMethod]
    public void 搜索组件失败能结束加载并允许重试()
    {
        using Scope scope = new(WorkspaceSearchMode.FileNames);
        scope.Panel.SearchBarrierForTest = (_, _) => throw new IOException("搜索组件测试失败");
        scope.Panel.SetQueryForTest("alpha");
        scope.Wait();
        Assert.AreEqual(UiText.SearchComponentFailed, scope.Panel.NoticeTextForTest);
        CollectionAssert.AreEqual(FailureNotices, scope.Notices);
        scope.Panel.SearchBarrierForTest = null;
        scope.Panel.SetQueryForTest("alpha");
        scope.Wait();
        Assert.AreEqual(1, scope.Panel.ResultCount);
        Assert.AreEqual(UiText.FileCount(1), scope.Panel.NoticeTextForTest);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 无同步上下文时搜索结果仍回到窗口线程且相同请求去重(bool textMode)
    {
        using Scope scope = new(textMode ? WorkspaceSearchMode.Text : WorkspaceSearchMode.FileNames);
        int inputThread = Environment.CurrentManagedThreadId;
        using ManualResetEventSlim entered = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int workerThread = inputThread;
        scope.Panel.SearchBarrierForTest = async (_, token) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            entered.Set(); await release.Task.WaitAsync(token);
        };
        try
        {
            scope.Panel.SetQueryForTest("alpha");
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            scope.Panel.SetQueryForTest("alpha");
            Assert.AreNotEqual(inputThread, workerThread);
            release.SetResult();
            scope.Wait();
            Assert.AreEqual(inputThread, scope.Panel.ResultThreadForTest);
            Assert.AreEqual(1, scope.Panel.SearchStartCountForTest);
            Assert.AreEqual(1, scope.Panel.ResultCount);
            Assert.AreEqual(scope.Edit, NativeMethods.GetFocus());
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 新查询期间不能打开或预览旧结果且旧完成消息不覆盖新查询(bool textMode)
    {
        using Scope scope = new(textMode ? WorkspaceSearchMode.Text : WorkspaceSearchMode.FileNames);
        scope.Panel.SetQueryForTest("alpha"); scope.Wait();
        scope.Panel.SetQueryForTest("beta");
        Assert.IsTrue(scope.Panel.HandleShortcut(new() { Window = scope.Edit, MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = NativeMethods.VirtualKeyEnter }));
        Assert.IsTrue(scope.Panel.PreviewFirstResultWithClickForTest());
        Assert.IsEmpty(scope.Opened);
        Assert.IsEmpty(scope.Previewed);
        // 等待后台完成但保留未分发的 beta 回写，再清空输入，模拟真实的晚到窗口消息。
        scope.Panel.SearchWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        scope.Panel.SetQueryForTest(string.Empty);
        scope.Wait();
        Assert.AreEqual(0, scope.Panel.ResultCount);
        Assert.AreEqual(string.Empty, scope.Panel.NoticeTextForTest);
        Assert.IsTrue(scope.Panel.SearchCompletedForTest);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 输入法组词不搜索不打开也不处理退出按键(bool textMode)
    {
        using Scope scope = new(textMode ? WorkspaceSearchMode.Text : WorkspaceSearchMode.FileNames);
        scope.Panel.SetQueryForTest("alpha"); scope.Wait();
        int searches = scope.Panel.SearchStartCountForTest;
        _ = NativeMethods.SendMessage(scope.Edit, 0x010D, 0, 0);
        _ = NativeMethods.SetWindowText(scope.Edit, "beta");
        foreach (int key in CompositionKeys)
        {
            NativeMethods.Message message = new() { Window = scope.Edit, MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = (nuint)key };
            Assert.IsTrue(scope.Panel.IsComposingKey(message));
            Assert.IsFalse(scope.Panel.HandleShortcut(message));
        }
        Assert.AreEqual(searches, scope.Panel.SearchStartCountForTest);
        Assert.IsEmpty(scope.Opened);
        _ = NativeMethods.SendMessage(scope.Edit, 0x010E, 0, 0);
        scope.Wait();
        StringAssert.EndsWith(scope.Panel.ResultPathForTest(0)!, "beta.txt");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 关闭和提前销毁窗口使后台结果失效(bool destroy)
    {
        using Scope scope = new(WorkspaceSearchMode.Text);
        using ManualResetEventSlim entered = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Panel.SearchBarrierForTest = async (_, _) => { entered.Set(); await release.Task; };
        try
        {
            scope.Panel.SetQueryForTest("alpha");
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            if (destroy) _ = NativeMethods.DestroyWindow(scope.Panel.Handle); else scope.Panel.Dispose();
            int layouts = scope.Layouts;
            release.TrySetResult();
            scope.Panel.SearchWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            NativeFindTestPump.Dispatch(scope.Panel.Handle);
            Assert.AreEqual(layouts, scope.Layouts);
            Assert.AreEqual(0, scope.Panel.SearchStartCountForTest);
            Assert.AreEqual(0, scope.Panel.ResultCount);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public void 搜索完成时不自动预览且回车传递正确行号()
    {
        using Scope scope = new(WorkspaceSearchMode.Text);
        scope.Panel.SetQueryForTest("alpha"); scope.Wait();
        Assert.IsEmpty(scope.Previewed);
        Assert.IsTrue(scope.Panel.PreviewFirstResultWithClickForTest());
        Assert.HasCount(1, scope.Previewed);
        Assert.AreEqual(2, scope.Previewed[0].Line);
        Assert.IsTrue(scope.Panel.HandleShortcut(new() { Window = scope.List, MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = NativeMethods.VirtualKeyEnter }));
        Assert.HasCount(1, scope.Opened);
        Assert.AreEqual(2, scope.Opened[0].Line);
        Assert.AreEqual(1, scope.Closed);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TemporaryDirectory _workspace = new();
        private readonly SynchronizationContext? _previousContext = SynchronizationContext.Current;
        internal readonly List<(string Path, int? Line)> Previewed = [];
        internal readonly List<(string Path, int? Line)> Opened = [];
        internal readonly List<string> Notices = [];
        internal int Layouts;
        internal int Closed;
        internal nint Owner { get; }
        internal NativeSearchPanel Panel { get; }
        internal nint Edit => Child(Panel.Handle, 1);
        internal nint List => Child(Panel.Handle, 2);

        internal Scope(WorkspaceSearchMode mode)
        {
            SynchronizationContext.SetSynchronizationContext(null);
            File.WriteAllText(_workspace.GetPath("alpha.txt"), "第一行\nalpha\n");
            File.WriteAllText(_workspace.GetPath("beta.txt"), "beta\n");
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0, 900, 550,
                0, 0, NativeMethods.GetModuleHandle(null), 0);
            Panel = new(Owner, _workspace.FullPath, mode, (path, line) => Previewed.Add((path, line)),
                (path, line) => Opened.Add((path, line)), () => Closed++, Notices.Add,
                () => { Layouts++; if (Panel is { } panel) panel.SetBounds(20, 20, 730, panel.PreferredHeightForTest); }, () => false);
            Panel.SetBounds(20, 20, 730, Panel.PreferredHeightForTest);
        }

        internal void Wait()
        {
            WaitUntil(() => Panel.SearchCompletedForTest && Panel.SearchWorkerForTest.IsCompleted);
            Panel.SearchWorkerForTest.GetAwaiter().GetResult();
            NativeFindTestPump.Dispatch(Panel.Handle);
        }

        internal void WaitUntil(Func<bool> condition)
        {
            long deadline = Environment.TickCount64 + 5000;
            while (!condition())
            {
                NativeFindTestPump.Dispatch(Panel.Handle);
                if (Environment.TickCount64 > deadline) Assert.Fail("搜索未在限定时间完成。");
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            Panel.Dispose();
            Panel.SearchWorkerForTest.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _ = NativeMethods.DestroyWindow(Owner);
            _workspace.Dispose();
            SynchronizationContext.SetSynchronizationContext(_previousContext);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint Child(nint window, int identifier);
}
