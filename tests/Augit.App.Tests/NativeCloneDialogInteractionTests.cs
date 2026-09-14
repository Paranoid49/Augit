using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeCloneDialogInteractionTests
{
    [TestMethod]
    public async Task 空字段及浅克隆深度错误不执行Git且焦点留在对应输入()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(session.Item(21), NativeMethods.GetFocus());
            Click(session.Item(11));
            Assert.AreEqual(0, session.Service.Calls);
            Assert.AreEqual(UiText.CloneFieldsRequired, Notice(session));
            Assert.AreEqual(session.Item(21), NativeMethods.GetFocus());
            Fill(session);
            Click(session.Item(14));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(23)));
            _ = NativeMethods.SetWindowText(session.Item(23), "0");
            Click(session.Item(11));
            Assert.AreEqual(0, session.Service.Calls);
            Assert.AreEqual(UiText.CloneDepthInvalid, Notice(session));
            Assert.AreEqual(session.Item(23), NativeMethods.GetFocus());
            Click(session.Item(14));
            Click(session.Item(11));
            Assert.AreEqual(1, session.Service.Calls);
            Assert.IsNull(session.Service.Depth);
        });
    }

    [TestMethod]
    public async Task 异步成功关闭并返回实际仓库且不取消成功令牌()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Fill(session); Click(session.Item(14));
            _ = NativeMethods.SetWindowText(session.Item(23), "25");
            Click(session.Item(11)); Click(session.Item(11));
            Assert.AreEqual(1, session.Service.Calls);
            Assert.AreEqual("https://example.invalid/team/repo.git", session.Service.Source);
            Assert.AreEqual(@"D:\clone-result", session.Service.Destination);
            Assert.AreEqual(25, session.Service.Depth);
            Assert.IsFalse(NativeMethods.IsWindowEnabled(session.Item(21)));
            Assert.IsFalse(NativeMethods.IsWindowEnabled(session.Item(13)));
            Assert.AreEqual(session.Item(12), NativeMethods.GetFocus());
        });
        session.Service.Complete(Success());
        Assert.AreEqual(@"D:\clone-result", await session.ClosedAsync());
        Assert.IsFalse(session.Service.Token.IsCancellationRequested);
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 失败保留输入控件与浅克隆状态并可重试成功()
    {
        using Session session = new();
        nint edit = 0;
        await session.InvokeAsync(() =>
        {
            Fill(session); Click(session.Item(14));
            _ = NativeMethods.SetWindowText(session.Item(23), "7");
            edit = session.Item(21); Click(session.Item(11));
        });
        session.Service.Complete(GitRepositoryOperationResult.Failure(GitOperationFailureKind.CommandFailed, "服务器暂时不可达。"));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual("服务器暂时不可达。", Notice(session));
            Assert.AreEqual(edit, session.Item(21));
            Assert.AreEqual("7", NativeMethods.GetWindowTextValue(session.Item(23)));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(23)));
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(session.Item(14), NativeMethods.ButtonMessageGetCheck, 0, 0));
            session.Service.Prepare(); Click(session.Item(11));
        });
        session.Service.Complete(Success());
        Assert.AreEqual(@"D:\clone-result", await session.ClosedAsync());
        Assert.AreEqual(2, session.Service.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task 取消等待Git结束并拒绝晚到成功(bool lateSuccess)
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Fill(session); Click(session.Item(11)); Click(session.Item(12));
            Assert.IsTrue(session.Service.Token.IsCancellationRequested);
            Assert.IsTrue(session.Dialog.RunningForTest);
            Assert.IsTrue(NativeMethods.IsWindow(session.Dialog.HandleForTest));
        });
        session.Service.Complete(lateSuccess ? Success() : GitRepositoryOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(UiText.OperationCancelled, Notice(session));
            Assert.AreEqual(UiText.Cancel, NativeMethods.GetWindowTextValue(session.Item(12)));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(21)));
            Click(session.Item(12));
        });
        Assert.IsNull(await session.ClosedAsync());
        session.AssertRestored();
    }

    [TestMethod]
    public async Task 服务异常恢复输入且不泄漏内部异常正文()
    {
        using Session session = new();
        await session.InvokeAsync(() => { Fill(session); Click(session.Item(11)); });
        session.Service.Fail(new InvalidOperationException("内部诊断不进入窗口"));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            Assert.AreEqual(UiText.CloneFailed, Notice(session));
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(11)));
        });
    }

    [TestMethod]
    public async Task 宿主销毁后释放登记与取消源并忽略晚到结果()
    {
        using Session session = new();
        Task? pending = null;
        await session.InvokeAsync(() =>
        {
            Fill(session); Click(session.Item(11)); pending = session.Dialog.OperationTaskForTest;
            _ = NativeMethods.DestroyWindow(session.Owner);
        });
        Assert.IsNull(await session.ClosedAsync());
        Assert.IsTrue(session.Service.Token.IsCancellationRequested);
        Assert.AreEqual(0, NativeCloneDialog.InstanceCountForTest);
        session.Service.Complete(Success());
        await pending!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, session.Dialog.HandleForTest);
    }

    [TestMethod]
    public async Task Tab跳过禁用深度且复选框可通过空格切换()
    {
        using Session session = new();
        await session.InvokeAsync(() => { _ = NativeMethods.SetFocus(session.Item(14)); Session.PostKey(NativeMethods.VirtualKeyTab); });
        await session.InvokeAsync(() => Assert.AreEqual(session.Item(12), NativeMethods.GetFocus()));
        await session.InvokeAsync(() =>
        {
            _ = NativeMethods.SetFocus(session.Item(14));
            Session.PostKey(0x20); Session.PostKeyUp(0x20);
        });
        await session.InvokeAsync(() =>
        {
            Assert.IsTrue(NativeMethods.IsWindowEnabled(session.Item(23)));
            Session.PostKey(NativeMethods.VirtualKeyTab);
        });
        await session.InvokeAsync(() => Assert.AreEqual(session.Item(23), NativeMethods.GetFocus()));
    }

    [TestMethod]
    public async Task 输入法组词时Esc不关闭且Enter不执行克隆()
    {
        using Session session = new();
        await session.InvokeAsync(() =>
        {
            Fill(session);
            _ = NativeMethods.SetFocus(session.Item(21));
            _ = NativeMethods.SendMessage(session.Item(21), 0x010D, 0, 0);
            Session.PostKey(NativeMethods.VirtualKeyEscape);
            Session.PostKey(NativeMethods.VirtualKeyEnter);
        });
        await session.InvokeAsync(() =>
        {
            Assert.IsTrue(NativeMethods.IsWindow(session.Dialog.HandleForTest));
            Assert.AreEqual(0, session.Service.Calls);
            _ = NativeMethods.SendMessage(session.Item(21), 0x010E, 0, 0);
            Session.PostKey(NativeMethods.VirtualKeyEnter);
        });
        await session.InvokeAsync(() => Assert.AreEqual(1, session.Service.Calls));
    }

    private static void Fill(Session session)
    {
        _ = NativeMethods.SetWindowText(session.Item(21), " https://example.invalid/team/repo.git ");
        _ = NativeMethods.SetWindowText(session.Item(22), @" D:\clone-result ");
    }
    private static string Notice(Session session) => NativeMethods.GetWindowTextValue(session.Dialog.NoticeForTest);
    private static void Click(nint window) => _ = NativeMethods.SendMessage(window, 0x00F5, 0, 0);
    private static GitRepositoryOperationResult Success() => GitRepositoryOperationResult.Success(GitRepositorySnapshot.PlainDirectory(@"D:\clone-result"));

    private sealed class Session : IDisposable
    {
        private const uint Dispatch = NativeMethods.WindowMessageApp + 81;
        private readonly Thread _thread;
        private readonly NativeMethods.SubclassProcedure _procedure;
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _restored;
        internal nint Owner { get; private set; }
        internal NativeCloneDialog Dialog { get; private set; } = null!;
        internal Service Service { get; } = new();

        internal Session(bool dark = false, int dpi = 96, int size = 13, int width = 1024, int height = 640)
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
                    NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
                    Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "Clone 回归宿主",
                        NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible,
                        20, 20, NativeTheme.Scale(width), NativeTheme.Scale(height), 0, 0, NativeMethods.GetModuleHandle(null), 0);
                    nint original = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "原焦点", NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                        0, 0, 100, 30, Owner, 0, NativeMethods.GetModuleHandle(null), 0);
                    _ = NativeMethods.SetWindowSubclass(Owner, _procedure, 1, 0);
                    _ = NativeMethods.SetFocus(original);
                    Dialog = new(Owner, Service, new() { Theme = dark ? "Dark" : "Light" });
                    _ = NativeMethods.PostMessage(Owner, Dispatch, 0, 0);
                    string? result = Dialog.Run();
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
        internal nint Item(int id) => GetDlgItem(id is 11 or 12 or 13 ? Dialog.HandleForTest : Dialog.BodyForTest, id);
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
        internal Task<string?> ClosedAsync() => _closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        internal void AssertRestored() { Assert.IsTrue(_restored); Assert.AreEqual(0, NativeCloneDialog.InstanceCountForTest); }
        public void Dispose()
        {
            Service.Complete(GitRepositoryOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_thread.Join(20) && DateTime.UtcNow < deadline)
                if (Dialog?.HandleForTest is { } handle && handle != 0) _ = NativeMethods.PostMessage(handle, NativeMethods.WindowMessageClose, 0, 0);
            Assert.IsFalse(_thread.IsAlive, "Clone 测试线程必须退出。");
        }
    }


    private sealed class Service : IGitRepositoryService
    {
        private TaskCompletionSource<GitRepositoryOperationResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal string? Source { get; private set; }
        internal string? Destination { get; private set; }
        internal int? Depth { get; private set; }
        internal CancellationToken Token { get; private set; }
        public GitRuntimeInfo Runtime => GitRuntimeInfo.Unavailable(GitRuntimeStatus.NotFound, "测试不启动 Git。");
        internal void Prepare() => _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete(GitRepositoryOperationResult result) => _result.TrySetResult(result);
        internal void Fail(Exception error) => _result.TrySetException(error);
        public Task<GitRepositoryOperationResult> CloneAsync(string source, string destinationPath, int? depth = null, CancellationToken cancellationToken = default)
        {
            Calls++; Source = source; Destination = destinationPath; Depth = depth; Token = cancellationToken;
            return _result.Task;
        }
        public Task<GitRepositoryOperationResult> InspectAsync(string workspacePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRepositoryOperationResult> InitializeAsync(string workspacePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint parent, int id);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? text);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint value);
}
