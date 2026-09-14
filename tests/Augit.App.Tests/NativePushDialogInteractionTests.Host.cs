using System.Diagnostics;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativePushDialogInteractionTests
{
    private static async Task RunAsync(Func<Context, Task> scenario, bool dark = false, int dpi = 96, int size = 13)
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? activeWindow = null;
        Context? activeContext = null;
        Thread thread = new(() =>
        {
            Exception? failure = null;
            string family = NativeTheme.UiFontFamilyForTest;
            double fontSize = NativeTheme.UiFontSizeForTest;
            using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
            try
            {
                ApplicationSettings settings = new() { Theme = dark ? "Dark" : "Light", TextFontSize = size, Window = new() { Width = 1180, Height = 760 } };
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), settings);
                Volatile.Write(ref activeWindow, window);
                window.Show();
                window.Post(() => _ = VerifyModalAsync());
                _ = MainWindow.RunMessageLoop();

                async Task VerifyModalAsync()
                {
                    Context? context = null;
                    try
                    {
                        // 在既有主窗口的 UI 线程创建，再从真实模态循环派发场景；不发布尚未就绪的 Dialog。
                        nint originalFocus = NativeMethods.CreateWindow(0, NativeMethods.EditClass, "推送前的焦点",
                            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible,
                            0, 0, 120, 30, window.Handle, 0, NativeMethods.GetModuleHandle(null), 0);
                        Assert.AreNotEqual((nint)0, originalFocus);
                        _ = NativeMethods.SetFocus(originalFocus);
                        context = new(window, settings);
                        Volatile.Write(ref activeContext, context);
                        TaskCompletionSource verified = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        window.Post(() => _ = VerifyScenarioAsync());
                        context.Dialog.Run();
                        bool restored = NativeMethods.IsWindowEnabled(window.Handle) && NativeMethods.GetFocus() == originalFocus
                            && FindPushChild(window.Handle, 0, "Augit.ModalScrim.Native", null) == 0;
                        await verified.Task;
                        if (context.ExpectOwnerDestroyed)
                        {
                            // 宿主消失后不能等待它派发测试 continuation；生产任务须独立结束。
                            Assert.IsTrue(Task.WhenAll([context.Dialog.WorkForTest, .. context.ChildWork]).Wait(TimeSpan.FromSeconds(3)));
                            Assert.AreEqual((nint)0, window.Handle);
                        }
                        else
                        {
                            await Task.WhenAll([context.Dialog.WorkForTest, .. context.ChildWork]).WaitAsync(TimeSpan.FromSeconds(3));
                            Assert.IsTrue(restored, "Push 模态结束后必须恢复宿主、原焦点并移除遮罩。");
                        }
                        Assert.AreEqual(0, NativePushDialog.InstanceCountForTest);
                        Assert.AreEqual(0, NativeRemoteDialog.InstanceCountForTest);

                        async Task VerifyScenarioAsync()
                        {
                            try { await scenario(context); verified.TrySetResult(); }
                            catch (Exception error) { verified.TrySetException(error); }
                            finally
                            {
                                context.Service.ReleasePending();
                                context.Dialog.Dispose();
                            }
                        }
                    }
                    catch (Exception error) { failure = error; }
                    finally
                    {
                        context?.Service.ReleasePending();
                        context?.Dialog.Dispose();
                        Volatile.Write(ref activeContext, null);
                        window.Close();
                    }
                }
            }
            catch (Exception error) { failure ??= error; }
            finally
            {
                Volatile.Write(ref activeWindow, null);
                NativeTheme.ConfigureUiTypography(family, fontSize);
                if (failure is null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Exception? scenarioFailure = null;
        bool joined;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception error) { scenarioFailure = error; }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref activeWindow);
            remaining?.Post(() =>
            {
                Context? context = Volatile.Read(ref activeContext);
                context?.Service.ReleasePending();
                context?.Dialog.Dispose();
                remaining.Close();
            });
            joined = thread.Join(TimeSpan.FromSeconds(5));
        }
        if (!joined) throw scenarioFailure is null ? new AssertFailedException("Push 测试线程没有退出。")
            : new AssertFailedException("Push 测试线程没有退出。", scenarioFailure);
        if (scenarioFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(scenarioFailure).Throw();
    }

    private sealed class Context
    {
        internal MainWindow Window { get; }
        internal Service Service { get; } = new();
        internal NativePushDialog Dialog { get; }
        internal string? Status { get; private set; }
        internal int StatusThread { get; private set; }
        internal int UiThread { get; } = Environment.CurrentManagedThreadId;
        internal bool ExpectOwnerDestroyed { get; set; }
        internal List<Task> ChildWork { get; } = [];

        internal Context(MainWindow window, ApplicationSettings settings)
        {
            Window = window;
            Dialog = new(window.Handle, GitRepositorySnapshot.PlainDirectory("D:\\fixture"), Service, settings,
                text => { Status = text; StatusThread = Environment.CurrentManagedThreadId; }, "refs/heads/topic");
        }

        internal nint Item(int id) => PushItem(id is >= 31 and <= 35 ? Dialog.DetailPanelForTest : Dialog.HandleForTest, id);
        internal void Click(int id) => _ = NativeMethods.SendMessage(Item(id), 0x00F5, 0, 0);
        internal async Task ReadyAsync(GitPushPreviewResult? result = null)
        {
            Service.Preview.TrySetResult(result ?? Preview());
            await WaitUntilAsync(() => Dialog.PreviewCompletedForTest);
        }
        internal async Task IdleAsync()
        {
            await Dialog.WorkForTest.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitUntilAsync(() => !Dialog.RunningForTest);
        }
        internal void PostKey(int key)
        {
            Assert.AreEqual(UiThread, Environment.CurrentManagedThreadId);
            _ = NativeMethods.PostMessage(NativeMethods.GetFocus(), NativeMethods.WindowMessageKeyDown, (nuint)key, 0);
        }

        internal async Task<NativeRemoteDialog> OpenRemoteAsync()
        {
            Window.Post(() => { _ = NativeMethods.SetFocus(Item(12)); Click(12); });
            NativeRemoteDialog? remote = null;
            await WaitUntilAsync(() => (remote = NativeRemoteDialog.FindForOwnerForTest(Dialog.HandleForTest)) is not null);
            return remote!;
        }

        internal async Task OpenRemoteRoundTripAsync()
        {
            TaskCompletionSource complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Window.Post(() =>
            {
                // 先把完成远端读取的动作排到宿主队列，再进入嵌套模态循环。
                Window.Post(() =>
                {
                    Service.RemoteRead.TrySetResult(GitRemoteListResult.Success([new("origin", "https://example.invalid/fetch.git", "https://example.invalid/push.git")]));
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                        Window.Post(() =>
                        {
                            NativeRemoteDialog? remote = NativeRemoteDialog.FindForOwnerForTest(Dialog.HandleForTest);
                            try
                            {
                                Assert.IsNotNull(remote);
                                Assert.IsFalse(remote!.BusyForTest);
                                Assert.AreEqual(1, NativeMethods.SendMessage(remote.RemoteListForTest, NativeMethods.ListBoxGetCount, 0, 0));
                            }
                            catch (Exception error) { complete.TrySetException(error); return; }
                            Service.PreparePreview();
                            _ = NativeMethods.PostMessage(remote!.HandleForTest, NativeMethods.WindowMessageClose, 0, 0);
                            complete.TrySetResult();
                        });
                    });
                });
                _ = NativeMethods.SetFocus(Item(12));
                Click(12);
            });
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static GitPushPreviewResult Preview(int count = 2) => GitPushPreviewResult.Success(new("refs/heads/topic", "origin", "refs/heads/review",
        Enumerable.Range(0, count).Select(index => new GitPushCommitPreview(index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture), $"待推送提交 {index + 1}")).ToArray()));

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.IsLessThan(4000, watch.ElapsedMilliseconds, "Push 交互状态等待超时。");
            await Task.Delay(10);
        }
    }

    private static NativeMethods.Rectangle Bounds(nint handle)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(handle, out var bounds));
        return bounds;
    }

    private static void Contains(NativeMethods.Rectangle outer, NativeMethods.Rectangle inner) =>
        Assert.IsTrue(inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom,
            $"控件越界：外 {outer.Left},{outer.Top},{outer.Right},{outer.Bottom}；内 {inner.Left},{inner.Top},{inner.Right},{inner.Bottom}");

    private static uint CommitPixel(nint list, NativeMethods.Rectangle row)
    {
        _ = NativeMethods.GetClientRectangle(list, out var client);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = client.Right, Height = -client.Bottom, Planes = 1, BitCount = 32 },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.AreNotEqual((nint)0, bitmap);
            _ = NativeMethods.SendMessage(list, 0x0318, (nuint)dc, 0x000C);
            return NativeMethods.GetPixel(dc, row.Right - NativeTheme.Scale(12), (row.Top + row.Bottom) / 2);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PushUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    private sealed class Service : IGitRemoteService
    {
        internal TaskCompletionSource<GitPushPreviewResult> Preview { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<GitRemoteOperationResult> Push { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken PreviewToken { get; private set; }
        internal CancellationToken PushToken { get; private set; }
        internal int PreviewCalls { get; private set; }
        internal int PushCalls { get; private set; }
        internal (string Remote, string Local, string Target) Request { get; private set; }
        internal TaskCompletionSource<GitRemoteListResult> RemoteRead { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<GitRemoteOperationResult> RemoteWrite { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken RemoteReadToken { get; private set; }
        internal CancellationToken RemoteWriteToken { get; private set; }
        internal int RemoteReads { get; private set; }
        internal int RemoteWrites { get; private set; }
        internal (string? Current, string Name, string Fetch, string? Push) RemoteRequest { get; private set; }
        internal void PreparePreview() => Preview = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GitPushPreviewResult> ReadPushPreviewAsync(GitRepositorySnapshot repository, string? localReference = null, CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            Assert.AreEqual("refs/heads/topic", localReference);
            PreviewToken = cancellationToken;
            return Preview.Task;
        }

        public Task<GitRemoteOperationResult> PushRefAsync(GitRepositorySnapshot repository, string remoteName, string localReference, string remoteReference, CancellationToken cancellationToken = default)
        {
            PushCalls++;
            Request = (remoteName, localReference, remoteReference);
            PushToken = cancellationToken;
            Push = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Push.Task;
        }

        internal void ReleasePending()
        {
            Preview.TrySetResult(GitPushPreviewResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            Push.TrySetResult(GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            RemoteRead.TrySetResult(GitRemoteListResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
            RemoteWrite.TrySetResult(GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
        }

        public Task<GitRemoteListResult> ReadRemotesAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default)
        {
            RemoteReads++; RemoteReadToken = cancellationToken;
            RemoteRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return RemoteRead.Task;
        }
        public Task<GitRemoteOperationResult> AddRemoteAsync(GitRepositorySnapshot repository, string name, string fetchUrl, string? pushUrl = null, CancellationToken cancellationToken = default) =>
            SaveRemote(null, name, fetchUrl, pushUrl, cancellationToken);
        public Task<GitRemoteOperationResult> UpdateRemoteAsync(GitRepositorySnapshot repository, string currentName, string newName, string fetchUrl, string? pushUrl = null, CancellationToken cancellationToken = default) =>
            SaveRemote(currentName, newName, fetchUrl, pushUrl, cancellationToken);
        private Task<GitRemoteOperationResult> SaveRemote(string? current, string name, string fetch, string? push, CancellationToken cancellationToken)
        {
            RemoteWrites++; RemoteWriteToken = cancellationToken; RemoteRequest = (current, name, fetch, push);
            RemoteWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return RemoteWrite.Task;
        }
        public Task<GitRemoteOperationResult> DeleteRemoteAsync(GitRepositorySnapshot repository, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> SetTrackingAsync(GitRepositorySnapshot repository, string localBranch, string remoteName, string remoteBranch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> FetchAsync(GitRepositorySnapshot repository, string? remoteName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> PullAsync(GitRepositorySnapshot repository, GitPullMode mode = GitPullMode.RepositoryConfigured, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> PushAsync(GitRepositorySnapshot repository, string? remoteName = null, string? branchName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint PushItem(nint parent, int id);


    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindPushChild(nint parent, nint after, string className, string? text);
}
