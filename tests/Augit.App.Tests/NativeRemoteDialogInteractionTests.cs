using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeRemoteDialogInteractionTests
{
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
    public void 字段按钮和列表真实字高在两种主题和三档Dpi下不裁切(bool dark, int dpi, int size)
    {
        using Scope scope = new(dark, dpi, size);
        var dialog = Bounds(scope.Dialog.HandleForTest);
        var owner = Bounds(scope.Owner);
        Contains(owner, dialog);
        int line = Measure(scope.Item(20), "国Ag").Height;
        Assert.IsGreaterThanOrEqualTo(line, (int)NativeMethods.SendMessage(scope.Item(1), 0x01A1, 0, 0)); // LB_GETITEMHEIGHT。
        foreach (int identifier in new[] { 20, 21, 22 })
        {
            var field = Bounds(scope.Item(identifier));
            Assert.IsGreaterThanOrEqualTo(line, field.Bottom - field.Top, "输入控件须完整容纳实际字体。");
            Assert.IsGreaterThan(NativeTheme.Scale(160), field.Right - field.Left, "标签增宽后仍须保留可用的 URL 输入宽度。");
        }
        foreach (int identifier in new[] { 11, 12, 15 })
        {
            var button = Bounds(scope.Item(identifier));
            var text = Measure(scope.Item(identifier), NativeMethods.GetWindowTextValue(scope.Item(identifier)));
            Assert.IsGreaterThanOrEqualTo(text.Height + NativeTheme.Scale(8), button.Bottom - button.Top);
            Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(16), button.Right - button.Left);
        }
        var panel = Bounds(scope.Dialog.DetailPanelForTest);
        var footerButton = Bounds(scope.Item(15));
        Assert.IsLessThanOrEqualTo(footerButton.Top, panel.Bottom, "详情视口必须在固定底部动作之前结束。");
        Assert.AreEqual(0, NativeMethods.GetScrollPosition(scope.Dialog.DetailPanelForTest, 1));
        if (size == 13)
        {
            Contains(panel, Bounds(scope.Item(11)));
            Assert.AreEqual(NativeTheme.Scale(407), dialog.Bottom - dialog.Top);
        }
        Assert.AreEqual(1, scope.Service.Reads);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 详情滚轮和分页不移动底栏且键盘焦点自动带回被裁切控件(int dpi)
    {
        using Scope scope = new(false, dpi, 40);
        nint detail = scope.Dialog.DetailPanelForTest;
        var footer = Bounds(scope.Item(15));
        _ = NativeMethods.SetWindowText(scope.Item(20), "保留的远端草稿");
        _ = NativeMethods.SetFocus(scope.Item(20));
        _ = NativeMethods.SendMessage(scope.Item(20), NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(nint)(-120 << 16)), 0);
        Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(detail, 1));
        Assert.AreEqual(footer.Top, Bounds(scope.Item(15)).Top);
        Assert.AreEqual("保留的远端草稿", NativeMethods.GetWindowTextValue(scope.Item(20)));
        _ = NativeMethods.SendMessage(detail, NativeMethods.WindowMessageVerticalScroll, 7, 0);
        Contains(Bounds(detail), Bounds(scope.Item(11)));
        _ = NativeMethods.SetFocus(scope.Item(15));
        _ = NativeMethods.SetFocus(scope.Item(20));
        Contains(Bounds(detail), Bounds(scope.Item(20)));
        _ = NativeMethods.SetFocus(scope.Item(22));
        scope.Dialog.MoveFocus(false);
        Assert.AreEqual(scope.Item(12), NativeMethods.GetFocus());
        Contains(Bounds(detail), Bounds(scope.Item(12)));
        scope.Dialog.MoveFocus(false);
        Assert.AreEqual(scope.Item(11), NativeMethods.GetFocus());
        Contains(Bounds(detail), Bounds(scope.Item(11)));
        Assert.AreEqual(1, scope.Service.Reads, "滚动和焦点切换不能重新读取 Git。");
    }

    [TestMethod]
    public void 保存和新增经详情容器正确转发且失败后保留输入和显示原因()
    {
        using Scope scope = new(true, 96, 40);
        _ = NativeMethods.SetWindowText(scope.Item(20), "renamed");
        _ = NativeMethods.SetWindowText(scope.Item(21), "https://example.com/edited.git");
        _ = NativeMethods.SendMessage(scope.Item(11), 0x00F5, 0, 0); // BM_CLICK 走真实父窗口命令。
        scope.WaitIdle();
        Assert.AreEqual("renamed", scope.Service.SavedName);
        Assert.AreEqual("https://example.com/edited.git", scope.Service.SavedFetchUrl);
        Assert.AreEqual("renamed", NativeMethods.GetWindowTextValue(scope.Item(20)));
        nint notice = FindWindowEx(scope.Dialog.DetailPanelForTest, 0, NativeMethods.StaticClass, RemoteService.Failure);
        Assert.AreNotEqual(0, notice);
        Contains(Bounds(scope.Dialog.DetailPanelForTest), Bounds(notice));
        _ = NativeMethods.SendMessage(scope.Item(10), 0x00F5, 0, 0);
        Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(scope.Item(20)));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(scope.Item(12)));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(scope.Item(16)));
        Assert.AreEqual(0, NativeMethods.GetScrollPosition(scope.Dialog.DetailPanelForTest, 1));
    }

    [TestMethod]
    public void 关闭远端窗口释放详情及字段窗口且可重复释放()
    {
        using Scope scope = new(false, 96, 40);
        nint detail = scope.Dialog.DetailPanelForTest, input = scope.Item(20);
        _ = NativeMethods.SendMessage(scope.Item(15), 0x00F5, 0, 0);
        Assert.IsFalse(NativeMethods.IsWindow(detail));
        Assert.IsFalse(NativeMethods.IsWindow(input));
        scope.Dialog.Dispose();
    }

    [TestMethod]
    public async Task 关闭读取中的远端窗口后晚到结果不更新控件()
    {
        DelayedRemoteService service = new();
        using Scope scope = new(false, 96, 13, service, waitForIdle: false);
        nint dialog = scope.Dialog.HandleForTest;
        nint list = scope.Dialog.RemoteListForTest;
        Assert.IsTrue(scope.Dialog.BusyForTest);

        scope.Dialog.Dispose();
        Assert.AreEqual(0, NativeRemoteDialog.InstanceCountForTest);
        Assert.IsFalse(NativeMethods.IsWindow(dialog));
        Assert.IsFalse(NativeMethods.IsWindow(list));

        service.CompleteRead();
        await scope.Dialog.WorkForTest;
        Assert.AreEqual(0, NativeRemoteDialog.InstanceCountForTest);
        Assert.IsFalse(NativeMethods.IsWindow(dialog));
    }

    [TestMethod]
    public async Task 宿主销毁期间远端读取结果不访问已销毁窗口()
    {
        DelayedRemoteService service = new();
        using Scope scope = new(false, 96, 13, service, waitForIdle: false);
        nint dialog = scope.Dialog.HandleForTest;
        Assert.IsTrue(scope.Dialog.BusyForTest);

        _ = NativeMethods.DestroyWindow(scope.Owner);
        scope.Dialog.Dispose();
        service.CompleteRead();
        await scope.Dialog.WorkForTest;

        Assert.AreEqual(0, NativeRemoteDialog.InstanceCountForTest);
        Assert.IsFalse(NativeMethods.IsWindow(dialog));
    }

    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds));
        return bounds;
    }

    private static void Contains(NativeMethods.Rectangle outer, NativeMethods.Rectangle inner)
    {
        Assert.IsTrue(inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom,
            $"控件 [{inner.Left},{inner.Top},{inner.Right},{inner.Bottom}] 越过区域 [{outer.Left},{outer.Top},{outer.Right},{outer.Bottom}]。");
    }

    private static (int Width, int Height) Measure(nint control, string text)
    {
        nint dc = NativeMethods.GetDeviceContext(control);
        nint previous = NativeMethods.SelectObject(dc, NativeMethods.SendMessage(control, 0x0031, 0, 0));
        try
        {
            NativeMethods.Rectangle bounds = new();
            _ = NativeMethods.DrawText(dc, text, text.Length, ref bounds,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            return (bounds.Right, bounds.Bottom);
        }
        finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(control, dc); }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _family = NativeTheme.UiFontFamilyForTest;
        private readonly double _size = NativeTheme.UiFontSizeForTest;
        private readonly IDisposable _scale;
        private readonly nint _dpi;
        internal readonly RemoteService Service;
        internal nint Owner { get; }
        internal NativeRemoteDialog Dialog { get; }

        internal Scope(bool dark, int dpi, int size, RemoteService? service = null, bool waitForIdle = true)
        {
            Service = service ?? new RemoteService();
            _dpi = SetThreadDpiAwarenessContext(-4);
            _scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "远端布局测试宿主",
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0,
                NativeTheme.Scale(1024), NativeTheme.Scale(640), 0, 0, NativeMethods.GetModuleHandle(null), 0);
            Dialog = new(Owner, GitRepositorySnapshot.PlainDirectory(AppContext.BaseDirectory), Service,
                new ApplicationSettings { Theme = dark ? "Dark" : "Light", TextFontSize = size, FontSize = 13 }, _ => { });
            _ = NativeMethods.ShowWindow(Dialog.HandleForTest, NativeMethods.ShowNormal);
            _ = NativeMethods.UpdateWindow(Dialog.HandleForTest);
            if (waitForIdle) WaitIdle();
        }

        internal void WaitIdle()
        {
            long deadline = Environment.TickCount64 + 5000;
            while (Dialog.BusyForTest || !Dialog.WorkForTest.IsCompleted)
            {
                NativeFindTestPump.Dispatch(Dialog.HandleForTest);
                Assert.IsLessThan(deadline, Environment.TickCount64, "远端结果未在限定时间内接纳。");
                Thread.Sleep(1);
            }
            Dialog.WorkForTest.GetAwaiter().GetResult();
        }

        internal nint Item(int identifier) => GetDialogItem(identifier is 20 or 21 or 22 or 11 or 12
            ? Dialog.DetailPanelForTest : Dialog.HandleForTest, identifier);

        public void Dispose()
        {
            Dialog.Dispose();
            _ = NativeMethods.DestroyWindow(Owner);
            NativeTheme.ConfigureUiTypography(_family, _size);
            _scale.Dispose();
            _ = SetThreadDpiAwarenessContext(_dpi);
        }
    }

    private class RemoteService : IGitRemoteService
    {
        internal const string Failure = "保存远端失败，请检查地址后重试。";
        internal int Reads;
        internal string? SavedName, SavedFetchUrl;
        public virtual Task<GitRemoteListResult> ReadRemotesAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(GitRemoteListResult.Success([new("origin", "https://example.com/team/Augit.git", "https://example.com/team/Augit.git")]));
        }
        public Task<GitRemoteOperationResult> UpdateRemoteAsync(GitRepositorySnapshot repository, string currentName, string newName, string fetchUrl, string? pushUrl = null, CancellationToken cancellationToken = default)
        {
            SavedName = newName; SavedFetchUrl = fetchUrl;
            return Task.FromResult(GitRemoteOperationResult.Failure(GitOperationFailureKind.CommandFailed, Failure));
        }
        public Task<GitRemoteOperationResult> AddRemoteAsync(GitRepositorySnapshot repository, string name, string fetchUrl, string? pushUrl = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> DeleteRemoteAsync(GitRepositorySnapshot repository, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> SetTrackingAsync(GitRepositorySnapshot repository, string localBranch, string remoteName, string remoteBranch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> FetchAsync(GitRepositorySnapshot repository, string? remoteName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> PullAsync(GitRepositorySnapshot repository, GitPullMode mode = GitPullMode.RepositoryConfigured, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> PushAsync(GitRepositorySnapshot repository, string? remoteName = null, string? branchName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitRemoteOperationResult> PushRefAsync(GitRepositorySnapshot repository, string remoteName, string localReference, string remoteReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitPushPreviewResult> ReadPushPreviewAsync(GitRepositorySnapshot repository, string? localReference = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DelayedRemoteService : RemoteService
    {
        private readonly TaskCompletionSource<GitRemoteListResult> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<GitRemoteListResult> ReadRemotesAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default)
        {
            Reads++;
            return _read.Task;
        }

        internal void CompleteRead()
        {
            _read.TrySetResult(GitRemoteListResult.Success([new("late", "https://example.invalid/late.git", "https://example.invalid/late.git")]));
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")] private static extern nint GetDialogItem(nint parent, int identifier);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? text);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
