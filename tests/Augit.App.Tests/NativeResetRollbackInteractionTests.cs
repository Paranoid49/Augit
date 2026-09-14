using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeResetRollbackInteractionTests
{
    private static readonly int[] ResetFocusOrder = [21, 11, 10, 12, 20];
    [TestMethod]
    public async Task Reset失败保留目标模式并在窗口线程接收结果后可重试()
    {
        using Session session = new();
        nint edit = 0;
        await session.InvokeAsync(() =>
        {
            edit = session.Item(20);
            _ = NativeMethods.SetWindowText(edit, "  main~2  ");
            session.SelectMode(1);
            Click(session.Item(10));
            Assert.AreEqual("main~2", session.Service.Target);
            Assert.AreEqual(GitResetMode.Mixed, session.Service.Mode);
            Assert.AreEqual(session.Item(11), NativeMethods.GetFocus());
            Assert.IsFalse(NativeMethods.IsWindowEnabled(edit));
            Click(session.Item(10));
            Assert.AreEqual(1, session.Service.Calls);
        });
        session.Service.Complete(GitActionResult.Failure(GitOperationFailureKind.CommandFailed, "目标不存在，请检查引用。"));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(edit, session.Item(20));
            Assert.AreEqual("  main~2  ", NativeMethods.GetWindowTextValue(edit));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(session.Item(21), NativeMethods.ComboBoxGetCurrentSelection, 0, 0));
            Assert.AreEqual("目标不存在，请检查引用。", session.Status);
            Assert.AreEqual(session.UiThreadId, session.StatusThreadId);
            Assert.IsTrue(NativeMethods.IsWindowEnabled(edit));
            session.Service.Prepare(); Click(session.Item(10));
        });
        session.Service.Complete(GitActionResult.Success());
        Assert.IsTrue(await session.ClosedAsync());
        Assert.AreEqual(UiText.ResetCompleted, session.Status);
        Assert.IsFalse(session.Service.Token.IsCancellationRequested);
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Reset取消等待命令结果并保留输入()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Click(session.Item(10)); Click(session.Item(11));
            Assert.IsTrue(session.Service.Token.IsCancellationRequested);
            Assert.IsTrue(session.Reset!.RunningForTest);
            Assert.IsTrue(NativeMethods.IsWindow(session.Handle));
        });
        session.Service.Complete(GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual("HEAD", NativeMethods.GetWindowTextValue(session.Item(20)));
            Assert.AreEqual(UiText.OperationCancelled, session.Status);
            Click(session.Item(11));
        });
        Assert.IsFalse(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Reset真实成功不因过晚的取消请求伪装成取消()
    {
        using Session session = new();
        await session.InvokeAsync(() => { Click(session.Item(10)); Click(session.Item(11)); });
        session.Service.Complete(GitActionResult.Success());
        Assert.IsTrue(await session.ClosedAsync());
        Assert.AreEqual(UiText.ResetCompleted, session.Status);
    }

    [TestMethod]
    public async Task Reset异常使用通用原因而不显示内部诊断()
    {
        using Session session = new();
        await session.InvokeAsync(() => Click(session.Item(10)));
        session.Service.Fail(new InvalidOperationException("内部路径与敏感诊断不应展示"));
        await session.WaitIdleAsync();
        Assert.AreEqual(UiText.ResetOperationFailed, session.Status);
    }

    [TestMethod]
    public async Task Reset宿主销毁取消请求并丢弃晚到成功()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Click(session.Item(10));
            _ = NativeMethods.DestroyWindow(session.Owner);
        });
        Assert.IsFalse(await session.ClosedAsync());
        Assert.IsTrue(session.Service.Token.IsCancellationRequested);
        session.Service.Complete(GitActionResult.Success());
        await session.Reset!.OperationTaskForTest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(session.Status);
    }

    [TestMethod]
    public async Task Reset输入法和展开的模式列表不触发确认或关闭()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            nint edit = session.Item(20), combo = session.Item(21);
            _ = NativeMethods.SetWindowText(edit, string.Empty);
            session.Key(edit, NativeMethods.VirtualKeyEnter);
            Assert.AreEqual(0, session.Service.Calls);
            Assert.AreEqual(edit, NativeMethods.GetFocus());
            _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
            Assert.IsFalse(session.Key(edit, NativeMethods.VirtualKeyEnter));
            Assert.IsFalse(session.Key(edit, NativeMethods.VirtualKeyEscape));
            Assert.IsFalse(session.Key(edit, NativeMethods.VirtualKeyTab));
            _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
            _ = NativeMethods.SetFocus(combo);
            _ = NativeMethods.SendMessage(combo, 0x014F, 1, 0);
            Assert.IsFalse(session.Key(combo, NativeMethods.VirtualKeyEnter));
            Assert.IsFalse(session.Key(combo, NativeMethods.VirtualKeyEscape));
            _ = NativeMethods.SendMessage(combo, 0x014F, 0, 0);
            Assert.AreEqual(0, session.Service.Calls);
            Assert.IsFalse(session.Key(session.Owner, NativeMethods.VirtualKeyEscape));
        });
    }

    [TestMethod]
    public async Task Reset按视觉顺序循环焦点且Enter取消不运行Git()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            foreach (int id in ResetFocusOrder)
            {
                session.Key(NativeMethods.GetFocus(), NativeMethods.VirtualKeyTab);
                Assert.AreEqual(session.Item(id), NativeMethods.GetFocus());
            }
            _ = NativeMethods.SetFocus(session.Item(11));
            session.Key(session.Item(11), NativeMethods.VirtualKeyEnter);
        });
        Assert.IsFalse(await session.ClosedAsync());
        Assert.AreEqual(0, session.Service.Calls);
    }

    [TestMethod]
    public async Task Reset真实消息循环先关闭下拉框再允许输入确认()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetFocus(session.Item(21));
            _ = NativeMethods.SendMessage(session.Item(21), 0x014F, 1, 0);
            _ = NativeMethods.PostMessage(session.Item(21), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
        });
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(session.Item(21), 0x0157, 0, 0));
            Assert.AreEqual(0, session.Service.Calls);
            _ = NativeMethods.SetFocus(session.Item(20));
            _ = NativeMethods.SetWindowText(session.Item(20), "main");
            _ = NativeMethods.PostMessage(session.Item(20), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        });
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(1, session.Service.Calls);
            Assert.AreEqual("main", session.Service.Target);
        });
        session.Service.Complete(GitActionResult.Success());
        Assert.IsTrue(await session.ClosedAsync());
    }

    [TestMethod]
    public async Task Rollback真实消息循环正文Enter不确认而Escape取消()
    {
        using Session session = new(rollback: true);
        await session.WaitComparisonAsync();
        await session.InvokeAsync(() =>
        {
            nint text = session.Rollback!.ComparisonForTest!.TextHandlesForTest[^1];
            _ = NativeMethods.SetFocus(text);
            _ = NativeMethods.PostMessage(text, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
        });
        await session.InvokeAsync(() =>
        {
            Assert.IsTrue(NativeMethods.IsWindow(session.Handle));
            Assert.AreEqual(0, session.Service.Calls);
            _ = NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
        });
        Assert.IsFalse(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Rollback焦点遍历Diff内部按钮正文并支持Enter切换模式()
    {
        using Session session = new(rollback: true);
        await session.WaitComparisonAsync();
        await session.InvokeAsync(() =>
        {
            NativeGitComparisonView view = session.Rollback!.ComparisonForTest!;
            var expected = view.FocusTargets.Append(session.Item(11)).Append(session.Item(10)).Append(session.Item(12))
                .Where(window => window != 0 && NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window)).Distinct().ToArray();
            _ = NativeMethods.SetFocus(expected[^1]);
            foreach (nint target in expected)
            {
                session.Key(NativeMethods.GetFocus(), NativeMethods.VirtualKeyTab);
                Assert.AreEqual(target, NativeMethods.GetFocus());
            }
            nint single = view.ToolbarButtonForTest(3);
            _ = NativeMethods.SetFocus(single);
            Assert.IsTrue(session.Key(single, NativeMethods.VirtualKeyEnter));
            Assert.IsFalse(view.UsesSideBySideForTest);
            foreach (nint text in view.TextHandlesForTest)
                Assert.AreNotEqual((nint)0, NativeMethods.SendMessage(text, 2140, 0, 0));
            Assert.IsTrue(NativeMethods.IsWindow(session.Handle));
            Assert.AreEqual(0, session.Service.Calls);
        });
        await session.WaitComparisonAsync();
        await session.InvokeAsync(() =>
        {
            Assert.IsTrue(session.Rollback!.ComparisonForTest!.IsReadOnly);
            _ = NativeMethods.SetFocus(session.Item(11));
            session.Key(session.Item(11), NativeMethods.VirtualKeyEnter);
        });
        Assert.IsFalse(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task Rollback确认只返回选择不在窗口内执行Git()
    {
        using Session session = new(rollback: true);
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetFocus(session.Item(10));
            Assert.IsTrue(session.Key(session.Item(10), NativeMethods.VirtualKeyEnter, (nint)(1L << 30)));
            Assert.IsTrue(NativeMethods.IsWindow(session.Handle));
            session.Key(session.Item(10), NativeMethods.VirtualKeyEnter);
        });
        Assert.IsTrue(await session.ClosedAsync());
        Assert.AreEqual(0, session.Service.Calls);
    }

    [TestMethod]
    [DataRow(false, false, 96)]
    [DataRow(false, true, 96)]
    [DataRow(false, true, 120)]
    [DataRow(false, false, 120)]
    [DataRow(false, false, 144)]
    [DataRow(false, true, 144)]
    [DataRow(true, true, 96)]
    [DataRow(true, false, 96)]
    [DataRow(true, false, 120)]
    [DataRow(true, true, 120)]
    [DataRow(true, true, 144)]
    [DataRow(true, false, 144)]
    public async Task 动作按钮悬停焦点按下禁用不移动布局(bool rollback, bool dark, int dpi)
    {
        using Session session = new(rollback, dark, dpi);
        await session.InvokeAsync(() =>
        {
            var palette = NativeTheme.Palette(dark);
            nint cancel = session.Item(11), close = session.Item(12), run = session.Item(10);
            Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(close));
            Assert.AreEqual(UiText.Close, NativeAccessibility.GetNameForTest(close));
            _ = NativeMethods.SetFocus(run);
            var before = Bounds(cancel);
            Assert.AreEqual(palette.Panel, Pixel(cancel, 5, 15));
            Hover(cancel);
            Assert.AreEqual(cancel, session.Hovered);
            Assert.AreEqual(run, NativeMethods.GetFocus());
            Assert.AreEqual(palette.Hover, Pixel(cancel, 5, 15));
            _ = NativeMethods.UpdateWindow(cancel);
            for (int i = 0; i < 100; i++) Hover(cancel);
            Assert.IsFalse(GetUpdateRect(cancel, out _, false));
            _ = NativeMethods.SetFocus(cancel);
            Assert.AreEqual(palette.Accent, Pixel(cancel, 39, 2));
            _ = NativeMethods.SendMessage(cancel, 0x00F3, 1, 0);
            Assert.AreEqual(palette.Hover, Pixel(cancel, 5, 15));
            _ = NativeMethods.SendMessage(cancel, 0x00F3, 0, 0);
            _ = NativeMethods.EnableWindow(cancel, false);
            Hover(cancel);
            Assert.AreEqual((nint)0, session.Hovered);
            Assert.AreEqual(palette.PanelMuted, Pixel(cancel, 5, 15));
            _ = NativeMethods.EnableWindow(cancel, true);
            _ = NativeMethods.SetFocus(cancel);
            Assert.AreEqual(palette.Danger, Pixel(run, 5, 15));
            if (!rollback)
            {
                session.SelectMode(0);
                _ = NativeMethods.SetFocus(run);
                Assert.AreEqual(palette.Accent, Pixel(run, 5, 15));
                Assert.AreEqual(palette.Panel, Pixel(run, 58, 1));
                Assert.AreEqual(UiText.RunReset, NativeAccessibility.GetNameForTest(run));
            }
            Assert.AreEqual(before, Bounds(cancel));
            Assert.AreEqual(NativeTheme.Scale(30), before.Bottom - before.Top);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(8) - 1, Bounds(run).Left - before.Right);
            Hover(cancel); Hover(close);
            _ = NativeMethods.SendMessage(cancel, NativeMethods.WindowMessageMouseLeave, 0, 0);
            Assert.AreEqual(close, session.Hovered);
            _ = NativeMethods.ShowWindow(session.Handle, NativeMethods.ShowHide);
            Assert.AreEqual((nint)0, session.Hovered);
            _ = NativeMethods.ShowWindow(session.Handle, NativeMethods.ShowNormal);
            Hover(close);
            _ = NativeMethods.EnableWindow(session.Handle, false);
            Assert.AreEqual((nint)0, session.Hovered);
            _ = NativeMethods.EnableWindow(session.Handle, true);
            Hover(close);
            _ = NativeMethods.DestroyWindow(close);
            Assert.AreEqual((nint)0, session.Hovered);
            Assert.AreEqual(0, session.Service.Calls);
        });
    }

    private static void Click(nint window) => _ = NativeMethods.SendMessage(window, 0x00F5, 0, 0);
    private static void Hover(nint window) => _ = NativeMethods.SendMessage(window, NativeMethods.WindowMessageMouseMove, 0,
        (nint)((NativeTheme.Scale(8) << 16) | NativeTheme.Scale(8)));
    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds)); return bounds;
    }
    private static uint Pixel(nint window, int x, int y)
    {
        var bounds = Bounds(window);
        nint dc = NativeMethods.GetDeviceContext(window), memory = NativeMethods.CreateCompatibleDeviceContext(dc);
        nint bitmap = CreateCompatibleBitmap(dc, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        nint previous = NativeMethods.SelectObject(memory, bitmap);
        try
        {
            _ = NativeMethods.RedrawWindow(window, 0, 0, NativeMethods.RedrawInvalidate | NativeMethods.RedrawUpdateNow);
            Assert.IsTrue(PrintWindow(window, memory, 2));
            return GetPixel(memory, NativeTheme.Scale(x), NativeTheme.Scale(y));
        }
        finally
        {
            _ = NativeMethods.SelectObject(memory, previous); _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(memory); _ = NativeMethods.ReleaseDeviceContext(window, dc);
        }
    }

    private sealed class Session : IDisposable
    {
        private const uint Dispatch = NativeMethods.WindowMessageApp + 81;
        private readonly Thread _thread;
        private readonly NativeMethods.SubclassProcedure _procedure;
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _restored;
        internal nint Owner { get; private set; }
        internal NativeResetDialog? Reset { get; private set; }
        internal NativeRollbackDialog? Rollback { get; private set; }
        internal nint Handle => Reset?.HandleForTest ?? Rollback!.HandleForTest;
        internal nint Hovered => Reset?.HoveredButtonForTest ?? Rollback!.HoveredButtonForTest;
        internal Service Service { get; } = new();
        internal string? Status { get; private set; }
        internal int StatusThreadId { get; private set; }
        internal int UiThreadId { get; private set; }

        internal Session(bool rollback = false, bool dark = false, int dpi = 96, int uiSize = 13, int viewportHeight = 760, int viewportWidth = 1180, bool rollbackRecycle = false)
        {
            _procedure = (window, message, word, parameter, id, data) =>
            {
                if (message == Dispatch)
                {
                    _ready.TrySetResult();
                    while (_queue.TryDequeue(out var action)) action();
                    return 0;
                }
                return NativeMethods.DefaultSubclassProcedure(window, message, word, parameter);
            };
            _thread = new(() =>
            {
                string family = NativeTheme.UiFontFamilyForTest;
                double size = NativeTheme.UiFontSizeForTest;
                nint awareness = SetThreadDpiAwarenessContext(-4);
                using IDisposable audit = NativeTheme.PushVisualAuditDpiOverride(dpi);
                try
                {
                    UiThreadId = Environment.CurrentManagedThreadId;
                    NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", uiSize);
                    Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "Reset 与回滚定向测试",
                        NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                        20, 20, NativeTheme.Scale(viewportWidth), NativeTheme.Scale(viewportHeight), 0, 0, NativeMethods.GetModuleHandle(null), 0);
                    nint original = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "原焦点",
                        NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                        0, 0, 100, 30, Owner, 0, NativeMethods.GetModuleHandle(null), 0);
                    _ = NativeMethods.SetWindowSubclass(Owner, _procedure, 1, 0);
                    _ = NativeMethods.SetFocus(original);
                    // 比较视图沿用主窗口的 UI 上下文；Reset 刻意不设置，验证自身结果消息分发。
                    if (rollback) SynchronizationContext.SetSynchronizationContext(new TestContext(action =>
                    {
                        _queue.Enqueue(action);
                        _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
                    }));
                    void StatusChanged(string value) { Status = value; StatusThreadId = Environment.CurrentManagedThreadId; }
                    if (rollback)
                        Rollback = new(Owner, GitRepositorySnapshot.PlainDirectory("D:\\fixture"),
                            new("a.txt", null, GitChangeGroup.Changes, rollbackRecycle ? GitChangeKind.Added : GitChangeKind.Modified, false, true),
                            new(GitDiffContentStatus.Ready, "a.txt", null, 4, 4,
                                "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+new\n"),
                            Service, new() { Theme = dark ? "Dark" : "Light" }, StatusChanged);
                    else Reset = new(Owner, GitRepositorySnapshot.PlainDirectory("D:\\fixture"), Service,
                        new() { Theme = dark ? "Dark" : "Light" }, StatusChanged, null, 35);
                    _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
                    bool result = Reset?.Run() ?? Rollback!.Run();
                    _restored = NativeMethods.IsWindowEnabled(Owner) && NativeMethods.GetFocus() == original;
                    _closed.TrySetResult(result);
                }
                catch (Exception error) { _ready.TrySetException(error); _closed.TrySetException(error); }
                finally
                {
                    Reset?.Dispose(); Rollback?.Dispose();
                    if (NativeMethods.IsWindow(Owner)) _ = NativeMethods.DestroyWindow(Owner);
                    NativeTheme.ConfigureUiTypography(family, size);
                    SynchronizationContext.SetSynchronizationContext(null);
                    _ = SetThreadDpiAwarenessContext(awareness);
                }
            });
            _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        }
        internal nint Item(int id) => GetDlgItem(Reset is not null && id is 20 or 21 ? Reset.BodyForTest : Handle, id);
        internal void SelectMode(int mode)
        {
            _ = NativeMethods.SendMessage(Item(21), NativeMethods.ComboBoxSetCurrentSelection, (nuint)mode, 0);
            _ = NativeMethods.SendMessage(Handle, NativeMethods.WindowMessageCommand,
                (nuint)((NativeMethods.ComboBoxNotificationSelectionChanged << 16) | 21), Item(21));
        }
        internal bool Key(nint window, int key, nint parameter = 0)
        {
            NativeMethods.Message message = new()
            {
                Window = window,
                MessageId = NativeMethods.WindowMessageKeyDown,
                WordParameter = (nuint)key,
                LongParameter = parameter
            };
            return Reset?.HandleKey(message) ?? Rollback!.HandleKey(message);
        }
        internal async Task InvokeAsync(Action action)
        {
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
            _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        internal async Task WaitIdleAsync()
        {
            await Reset!.OperationTaskForTest.WaitAsync(TimeSpan.FromSeconds(3));
            await InvokeAsync(() => Assert.IsFalse(Reset.RunningForTest));
        }
        internal async Task WaitComparisonAsync()
        {
            bool busy = true;
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (busy && DateTime.UtcNow < deadline)
            {
                await InvokeAsync(() => busy = Rollback!.ComparisonForTest!.IsBusy);
                if (busy) await Task.Delay(10);
            }
            Assert.IsFalse(busy, "比较视图应完成当前排版。");
        }
        internal Task<bool> ClosedAsync() => _closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        internal void AssertRestored() => Assert.IsTrue(_restored);
        public void Dispose()
        {
            Service.Complete(GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_thread.Join(20) && DateTime.UtcNow < deadline)
                if ((Reset?.HandleForTest ?? Rollback?.HandleForTest ?? 0) is var handle && handle != 0)
                    _ = NativeMethods.PostMessage(handle, NativeMethods.WindowMessageClose, 0, 0);
            Assert.IsFalse(_thread.IsAlive, "本组测试线程必须退出。");
        }
    }

    private sealed class TestContext(Action<Action> post) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => post(() => callback(state));
    }

    private sealed class Service : IGitWorkspaceStateService, IGitDiffService
    {
        private TaskCompletionSource<GitActionResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal string? Target { get; private set; }
        internal GitResetMode Mode { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal void Prepare() => _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(GitActionResult result) => _result.TrySetResult(result);
        internal void Fail(Exception error) => _result.TrySetException(error);
        public Task<GitActionResult> ResetAsync(GitRepositorySnapshot repository, string targetRevision, GitResetMode mode, CancellationToken cancellationToken = default)
        { Calls++; Target = targetRevision; Mode = mode; Token = cancellationToken; return _result.Task; }
        public Task<GitDiffResult> CreateAsync(GitRepositorySnapshot repository, GitChangedFile changedFile, GitDiffOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> StashWithOptionsAsync(GitRepositorySnapshot repository, string? message, bool includeUntracked, bool keepIndex = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitStashListResult> ReadStashesAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitStashContentResult> ReadStashContentAsync(GitRepositorySnapshot repository, string stashReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> StashAsync(GitRepositorySnapshot repository, string? message, bool includeUntracked, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> UnstashAsync(GitRepositorySnapshot repository, string stashReference, bool keepStash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> DeleteStashAsync(GitRepositorySnapshot repository, string stashReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> RollbackAsync(GitRepositorySnapshot repository, GitChangedFile changedFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint parent, int id);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUpdateRect(nint window, out NativeMethods.Rectangle bounds, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
}
