using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeStashDialogInteractionTests
{
    private static readonly int[] FormIds = [19, 20, 21, 22, 30, 31, 32];
    private static readonly int[] FooterIds = [10, 11];
    [TestMethod]
    [DataRow(false, 96, 13)]
    [DataRow(true, 96, 13)]
    [DataRow(false, 120, 13)]
    [DataRow(true, 120, 13)]
    [DataRow(false, 144, 13)]
    [DataRow(true, 144, 13)]
    [DataRow(false, 96, 40)]
    [DataRow(true, 96, 40)]
    [DataRow(false, 120, 40)]
    [DataRow(true, 120, 40)]
    [DataRow(false, 144, 40)]
    [DataRow(true, 144, 40)]
    public async Task 表单真实字高与固定动作不重叠(bool dark, int dpi, int size)
    {
        using Session session = new(dark, dpi, size);
        await session.InvokeAsync(() =>
        {
            Contains(Bounds(session.Owner), Bounds(session.Dialog.HandleForTest));
            nint body = session.Dialog.BodyForTest;
            foreach (int id in FormIds)
            {
                var bounds = Bounds(session.Item(id));
                Assert.IsGreaterThanOrEqualTo(Measure(session.Item(id), "国Ag").Height, bounds.Bottom - bounds.Top);
                Contains(Bounds(body), bounds);
            }
            Assert.IsLessThanOrEqualTo(Bounds(session.Item(20)).Top, Bounds(session.Item(22)).Bottom);
            Assert.IsLessThanOrEqualTo(Bounds(session.Item(21)).Top, Bounds(session.Item(20)).Bottom);
            foreach (int id in FooterIds)
            {
                var bounds = Bounds(session.Item(id));
                var text = Measure(session.Item(id), NativeMethods.GetWindowTextValue(session.Item(id)));
                Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(16), bounds.Right - bounds.Left);
                Assert.IsGreaterThanOrEqualTo(text.Height, bounds.Bottom - bounds.Top);
                Assert.IsGreaterThanOrEqualTo(Bounds(body).Bottom, bounds.Top);
            }
            Assert.IsLessThanOrEqualTo(Bounds(session.Item(10)).Left, Bounds(session.Item(11)).Right);
            Assert.AreEqual("D:\\fixture", NativeAccessibility.GetNameForTest(session.Item(19)));
            Assert.AreEqual(UiText.StashMessage, NativeAccessibility.GetNameForTest(session.Item(20)));
            Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(session.Item(12)));
            AssertRootArrowBackground(session.Item(19), dark);
            if (size == 13)
            {
                var bounds = Bounds(session.Dialog.HandleForTest);
                Assert.AreEqual(NativeTheme.Scale(620), bounds.Right - bounds.Left);
                Assert.AreEqual(NativeTheme.Scale(323), bounds.Bottom - bounds.Top);
            }
            Assert.AreEqual(0, session.Service.Calls);
        });
    }

    [TestMethod]
    public async Task 成功完成后自动关闭且不把成功转成取消()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetWindowText(session.Item(20), "  保留中文消息  ");
            Click(session.Item(21));
            Click(session.Item(10));
            Assert.AreEqual(1, session.Service.Calls);
            Assert.IsTrue(session.Service.KeepIndex);
            Assert.IsFalse(session.Service.IncludeUntracked);
            Assert.AreEqual("保留中文消息", session.Service.Message);
            Assert.IsTrue(NativeMethods.IsWindow(session.Dialog.HandleForTest));
            Assert.IsFalse(NativeMethods.IsWindowEnabled(session.Item(20)));
            Assert.AreEqual(session.Item(11), NativeMethods.GetFocus());
            Click(session.Item(10));
            Assert.AreEqual(1, session.Service.Calls);
        });
        session.Service.Complete(GitActionResult.Success());
        Assert.IsTrue(await session.ClosedAsync());
        Assert.IsFalse(session.Service.Token.IsCancellationRequested);
        Assert.AreEqual(UiText.StashCreated, session.Status);
        Assert.AreEqual(session.UiThreadId, session.StatusThreadId);
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 失败保留原控件草稿与勾选且允许重试成功()
    {
        using Session session = new();
        nint edit = 0;
        await session.InvokeAsync(() =>
        {
            edit = session.Item(20);
            _ = NativeMethods.SetWindowText(edit, "第一行\r\n第二行");
            Click(session.Item(21)); Click(session.Item(10));
        });
        session.Service.Complete(GitActionResult.Failure(GitOperationFailureKind.CommandFailed, "Git 锁文件被占用。"));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(edit, session.Item(20));
            Assert.AreEqual("第一行\r\n第二行", NativeMethods.GetWindowTextValue(edit));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(session.Item(21), NativeMethods.ButtonMessageGetCheck, 0, 0));
            Assert.AreEqual("Git 锁文件被占用。", NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(10)));
            session.Service.Prepare(); Click(session.Item(10));
        });
        session.Service.Complete(GitActionResult.Success());
        Assert.IsTrue(await session.ClosedAsync());
        Assert.AreEqual(2, session.Service.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task 取消等待服务结束且晚到结果不能显示成功(bool lateSuccess)
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Click(session.Item(10)); Click(session.Item(11));
            Assert.IsTrue(session.Service.Token.IsCancellationRequested);
            Assert.IsTrue(session.Dialog.RunningForTest);
            Assert.IsTrue(NativeMethods.IsWindow(session.Dialog.HandleForTest));
        });
        session.Service.Complete(lateSuccess ? GitActionResult.Success() : GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(UiText.OperationCancelled, NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.AreEqual(UiText.Cancel, NativeMethods.GetWindowTextValue(session.Item(11)));
            Click(session.Item(11));
        });
        Assert.IsFalse(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 服务异常在本窗口提示且不泄漏异常正文()
    {
        using Session session = new();
        await session.InvokeAsync(() => Click(session.Item(10)));
        session.Service.Fail(new InvalidOperationException("不得进入界面的内部诊断"));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(UiText.StashOperationFailed, NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(10)));
        });
    }

    [TestMethod]
    public async Task 大字号长错误可滚动且返回字段保持输入与底栏()
    {
        using Session session = new(true, 144, 40);
        var footer = default(NativeMethods.Rectangle);
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetWindowText(session.Item(20), "还在这里");
            footer = Bounds(session.Item(10)); Click(session.Item(10));
        });
        string error = string.Join('\n', Enumerable.Repeat("暂时无法创建 Stash，请检查 Git 输出。", 12));
        session.Service.Complete(GitActionResult.Failure(GitOperationFailureKind.CommandFailed, error));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            nint body = session.Dialog.BodyForTest;
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageVerticalScroll, 7, 0);
            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(body, 1));
            var bottom = Bounds(session.Item(23)).Bottom;
            Assert.IsLessThanOrEqualTo(Bounds(body).Bottom, bottom);
            Assert.AreEqual(footer.Top, Bounds(session.Item(10)).Top);
            _ = NativeMethods.SetFocus(session.Item(20));
            Contains(Bounds(body), Bounds(session.Item(20)));
            Assert.AreEqual("还在这里", NativeMethods.GetWindowTextValue(session.Item(20)));
            Assert.AreEqual(error, NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.AreEqual(1, session.Service.Calls);
        });
    }

    [TestMethod]
    public async Task 消息只有内容超出时显示滚动条并可输入多行()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            nint edit = session.Item(20);
            Assert.AreEqual(0L, NativeMethods.GetWindowLongPointer(edit, NativeMethods.WindowLongStyle).ToInt64() & NativeMethods.WindowStyleVerticalScroll);
            string longMessage = string.Join("\r\n", Enumerable.Repeat("多行消息", 40));
            _ = NativeMethods.SetWindowText(edit, longMessage);
            Assert.AreEqual(longMessage, NativeMethods.GetWindowTextValue(edit));
            Assert.AreNotEqual(0L, NativeMethods.GetWindowLongPointer(edit, NativeMethods.WindowLongStyle).ToInt64() & NativeMethods.WindowStyleVerticalScroll);
            _ = NativeMethods.SetWindowText(edit, string.Empty);
            Assert.AreEqual(0L, NativeMethods.GetWindowLongPointer(edit, NativeMethods.WindowLongStyle).ToInt64() & NativeMethods.WindowStyleVerticalScroll);
        });
    }

    [TestMethod]
    public async Task 原生复选框鼠标与空格切换且消息Enter仅换行()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(session.Item(21), NativeMethods.ButtonMessageGetCheck, 0, 0));
            Click(session.Item(21));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(session.Item(21), NativeMethods.ButtonMessageGetCheck, 0, 0));
            _ = NativeMethods.SetFocus(session.Item(21));
            Session.PostKey(0x20); Session.PostKeyUp(0x20);
        });
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(session.Item(21), NativeMethods.ButtonMessageGetCheck, 0, 0));
            _ = NativeMethods.SetFocus(session.Item(20));
            _ = NativeMethods.SetWindowText(session.Item(20), "消息");
            Session.PostKey(NativeMethods.VirtualKeyEnter);
        });
        await session.InvokeAsync(() => { }); // 等待 TranslateMessage 生成的 WM_CHAR。
        await session.InvokeAsync(() =>
        {
            StringAssert.Contains(NativeMethods.GetWindowTextValue(session.Item(20)), "\r\n");
            Assert.AreEqual(0, session.Service.Calls);
        });
    }

    [TestMethod]
    public async Task Tab沿视觉顺序循环且组词Esc不关闭()
    {
        using Session session = new();
        foreach (int next in new[] { 20, 21, 11, 10, 12, 19 })
        {
            await session.InvokeAsync(() => Session.PostKey(NativeMethods.VirtualKeyTab));
            await session.InvokeAsync(() => Assert.AreEqual(session.Item(next), NativeMethods.GetFocus()));
        }
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetFocus(session.Item(20));
            _ = NativeMethods.SendMessage(session.Item(20), 0x010D, 0, 0);
            Session.PostKey(NativeMethods.VirtualKeyEscape);
        });
        await session.InvokeAsync(() =>
        {
            Assert.IsTrue(NativeMethods.IsWindow(session.Dialog.HandleForTest));
            _ = NativeMethods.SendMessage(session.Item(20), 0x010E, 0, 0);
            _ = NativeMethods.SetFocus(session.Item(20));
            _ = NativeMethods.PostMessage(session.Item(20), NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
        });
        Assert.IsFalse(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 宿主销毁时取消请求并拒绝晚到成功()
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
        await session.Dialog.OperationTaskForTest.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsNull(session.Status);
        Assert.AreEqual(0, NativeStashDialog.InstanceCountForTest);
    }

    private static void AssertRootArrowBackground(nint combo, bool dark)
    {
        ComboBoxInfo info = new() { Size = Marshal.SizeOf<ComboBoxInfo>() };
        Assert.IsTrue(GetComboBoxInfo(combo, ref info));
        _ = NativeMethods.InvalidateRectangle(combo, 0, true);
        _ = NativeMethods.UpdateWindow(combo);
        nint dc = NativeMethods.GetDeviceContext(combo);
        try
        {
            // 检查系统按钮左侧留白，避免高缩放下只覆盖固定宽度而留下白边和旧箭头。
            Assert.AreEqual(NativeTheme.Palette(dark).Panel,
                NativeMethods.GetPixel(dc, info.Button.Left + 2, (info.Button.Top + info.Button.Bottom) / 2));
        }
        finally { _ = NativeMethods.ReleaseDeviceContext(combo, dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        internal int Size;
        internal NativeMethods.Rectangle Item, Button;
        internal uint ButtonState;
        internal nint Combo, Edit, List;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(nint combo, ref ComboBoxInfo info);

    private static void Click(nint window) => _ = NativeMethods.SendMessage(window, 0x00F5, 0, 0);
    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds)); return bounds;
    }
    private static void Contains(NativeMethods.Rectangle parent, NativeMethods.Rectangle child)
    {
        Assert.IsTrue(child.Left >= parent.Left && child.Top >= parent.Top && child.Right <= parent.Right && child.Bottom <= parent.Bottom,
            $"控件越界：父 {parent.Left},{parent.Top},{parent.Right},{parent.Bottom}；子 {child.Left},{child.Top},{child.Right},{child.Bottom}");
    }
    private static (int Width, int Height) Measure(nint control, string text)
    {
        nint dc = NativeMethods.GetDeviceContext(control);
        nint previous = NativeMethods.SelectObject(dc, NativeMethods.SendMessage(control, 0x0031, 0, 0));
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds, NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine);
            return (bounds.Right, bounds.Bottom);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(control, dc); }
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
        internal NativeStashDialog Dialog { get; private set; } = null!;
        internal Service Service { get; } = new();
        internal string? Status { get; private set; }
        internal int StatusThreadId { get; private set; }
        internal int UiThreadId { get; private set; }

        internal Session(bool dark = false, int dpi = 96, int size = 13)
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
                double fontSize = NativeTheme.UiFontSizeForTest;
                nint awareness = SetThreadDpiAwarenessContext(-4);
                using IDisposable audit = NativeTheme.PushVisualAuditDpiOverride(dpi);
                try
                {
                    UiThreadId = Environment.CurrentManagedThreadId;
                    NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
                    Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "Stash 回归宿主",
                        NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                        20, 20, NativeTheme.Scale(1024), NativeTheme.Scale(640), 0, 0, NativeMethods.GetModuleHandle(null), 0);
                    nint original = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "原焦点", NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                        0, 0, 100, 30, Owner, 0, NativeMethods.GetModuleHandle(null), 0);
                    _ = NativeMethods.SetWindowSubclass(Owner, _procedure, 1, 0);
                    _ = NativeMethods.SetFocus(original);
                    Dialog = new(Owner, GitRepositorySnapshot.PlainDirectory("D:\\fixture"), Service,
                        new() { Theme = dark ? "Dark" : "Light" }, text => { Status = text; StatusThreadId = Environment.CurrentManagedThreadId; }, "main");
                    _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
                    bool result = Dialog.Run();
                    _restored = NativeMethods.IsWindowEnabled(Owner) && NativeMethods.GetFocus() == original
                        && FindWindowEx(Owner, 0, "Augit.ModalScrim.Native", null) == 0;
                    _closed.TrySetResult(result);
                }
                catch (Exception error) { _ready.TrySetException(error); _closed.TrySetException(error); }
                finally
                {
                    Dialog?.Dispose();
                    if (NativeMethods.IsWindow(Owner)) _ = NativeMethods.DestroyWindow(Owner);
                    NativeTheme.ConfigureUiTypography(family, fontSize);
                    _ = SetThreadDpiAwarenessContext(awareness);
                }
            });
            _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        }
        internal nint Item(int id) => GetDlgItem(id is >= 19 and <= 32 ? Dialog.BodyForTest : Dialog.HandleForTest, id);
        internal async Task InvokeAsync(Action action)
        {
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() => { try { action(); done.SetResult(); } catch (Exception error) { done.SetException(error); } });
            _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        internal static void PostKey(int key) => _ = NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, (nuint)key, 0);
        internal static void PostKeyUp(int key) => _ = NativeMethods.PostMessage(NativeMethods.GetFocus(), 0x0101, (nuint)key, 0);
        internal async Task WaitIdleAsync()
        {
            await Dialog.OperationTaskForTest.WaitAsync(TimeSpan.FromSeconds(3));
            await InvokeAsync(() => Assert.IsFalse(Dialog.RunningForTest));
        }
        internal Task<bool> ClosedAsync() => _closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        internal void AssertRestored() { Assert.IsTrue(_restored); Assert.AreEqual(0, NativeStashDialog.InstanceCountForTest); }
        public void Dispose()
        {
            Service.Complete(GitActionResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_thread.Join(20) && DateTime.UtcNow < deadline)
                if (Dialog?.HandleForTest is { } handle && handle != 0) _ = NativeMethods.PostMessage(handle, NativeMethods.WindowMessageClose, 0, 0);
            Assert.IsFalse(_thread.IsAlive, "Stash 测试线程必须退出。");
        }
    }

    private sealed class Service : IGitWorkspaceStateService
    {
        private TaskCompletionSource<GitActionResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal bool KeepIndex { get; private set; }
        internal bool IncludeUntracked { get; private set; }
        internal string? Message { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal void Prepare() => _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(GitActionResult result) => _result.TrySetResult(result);
        internal void Fail(Exception error) => _result.TrySetException(error);
        public Task<GitActionResult> StashWithOptionsAsync(GitRepositorySnapshot repository, string? message, bool includeUntracked, bool keepIndex = false, CancellationToken cancellationToken = default)
        {
            Calls++; Message = message; KeepIndex = keepIndex; IncludeUntracked = includeUntracked; Token = cancellationToken;
            return _result.Task;
        }
        public Task<GitStashListResult> ReadStashesAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitStashContentResult> ReadStashContentAsync(GitRepositorySnapshot repository, string stashReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> StashAsync(GitRepositorySnapshot repository, string? message, bool includeUntracked, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> UnstashAsync(GitRepositorySnapshot repository, string stashReference, bool keepStash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> DeleteStashAsync(GitRepositorySnapshot repository, string stashReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> ResetAsync(GitRepositorySnapshot repository, string targetRevision, GitResetMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitActionResult> RollbackAsync(GitRepositorySnapshot repository, GitChangedFile changedFile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint parent, int id);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? text);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint value);
}
