using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeWorktreeManagerInteractionTests
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
    public void 详情和新建表单按字高对齐且视口不覆盖底栏(bool dark, int dpi, int size)
    {
        using Scope scope = new(dark, dpi, size);
        Contains(Bounds(scope.Owner), Bounds(scope.Dialog.HandleForTest));
        var panel = Bounds(scope.Dialog.DetailPanelForTest);
        var footer = Bounds(scope.Item(17));
        Assert.IsLessThanOrEqualTo(footer.Top, panel.Bottom);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight,
            (int)NativeMethods.SendMessage(scope.Item(1), 0x01A1, 0, 0));
        for (int index = 0; index < 3; index++)
        {
            var label = Bounds(scope.Dialog.LabelsForTest[index]);
            var value = Bounds(scope.Dialog.ValuesForTest[index]);
            Assert.AreEqual(label.Top, value.Top, "字段名与值必须从同一基线区域开始。");
            Assert.IsLessThanOrEqualTo(value.Left, label.Right);
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, value.Bottom - value.Top);
        }
        foreach (int identifier in new[] { 13, 19, 15, 17 }) AssertTextFits(scope.Item(identifier));
        if (size == 13)
        {
            Assert.IsLessThanOrEqualTo(1, Math.Abs(NativeTheme.Scale(365) - (Bounds(scope.Dialog.HandleForTest).Bottom - Bounds(scope.Dialog.HandleForTest).Top)),
                "窗口换算的物理像素舍入误差不得超过 1px。");
            Contains(panel, Bounds(scope.Item(13)));
        }
        scope.Click(19);
        foreach (int identifier in new[] { 20, 21, 22 })
        {
            _ = NativeMethods.SetFocus(scope.Item(identifier));
            Contains(Bounds(scope.Dialog.DetailPanelForTest), Bounds(scope.Item(identifier)));
            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, Bounds(scope.Item(identifier)).Bottom - Bounds(scope.Item(identifier)).Top);
            Assert.IsGreaterThan(NativeTheme.Scale(100), Bounds(scope.Item(identifier)).Right - Bounds(scope.Item(identifier)).Left);
        }
        scope.Dialog.MoveFocus(false);
        Assert.AreEqual(scope.Item(14), NativeMethods.GetFocus());
        Contains(Bounds(scope.Dialog.DetailPanelForTest), Bounds(scope.Item(14)));
        AssertTextFits(scope.Item(14));
        Assert.AreEqual(footer.Top, Bounds(scope.Item(17)).Top, "切换模式不移动固定底部动作。");
        Assert.AreEqual(1, scope.Service.Reads, "布局、滚动和模式切换不重读仓库。");
    }

    [TestMethod]
    public void 失败保留新建草稿并在原窗口线程显示可换行原因()
    {
        using Scope scope = new(true, 96, 40);
        scope.PrepareDraft();
        scope.Click(14);
        scope.WaitIdle();
        Assert.AreEqual(1, scope.Service.Creates);
        Assert.AreEqual(1, scope.Service.Reads);
        Assert.AreEqual(Service.DraftPath, NativeMethods.GetWindowTextValue(scope.Item(20)));
        Assert.AreEqual("feature/layout", NativeMethods.GetWindowTextValue(scope.Item(22)));
        Assert.AreEqual(Service.Failure, NativeMethods.GetWindowTextValue(scope.Dialog.NoticeForTest));
        Assert.AreEqual(scope.Thread, scope.LastStatusThread);
        Assert.IsTrue(NativeMethods.IsWindowEnabled(scope.Item(14)));
        Assert.AreEqual(scope.Item(20), NativeMethods.GetFocus());
        _ = NativeMethods.SendMessage(scope.Dialog.DetailPanelForTest, NativeMethods.WindowMessageVerticalScroll, 7, 0);
        Contains(Bounds(scope.Dialog.DetailPanelForTest), Bounds(scope.Dialog.NoticeForTest));
    }

    [TestMethod]
    public void 读取期间进入新建后晚到列表不能覆盖输入()
    {
        Service service = new() { DelayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Scope scope = new(false, 96, 13, service, wait: false);
        Task read = scope.Dialog.WorkForTest;
        scope.Click(10);
        _ = NativeMethods.SetWindowText(scope.Item(20), Service.DraftPath);
        service.DelayedRead.SetResult(GitWorktreeListResult.Success(Service.Worktrees));
        Assert.IsTrue(read.Wait(TimeSpan.FromSeconds(5)));
        NativeFindTestPump.Dispatch(scope.Dialog.HandleForTest);
        Assert.IsTrue(service.ReadToken.IsCancellationRequested);
        Assert.AreEqual(Service.DraftPath, NativeMethods.GetWindowTextValue(scope.Item(20)));
        Assert.IsTrue(NativeMethods.IsWindowVisible(scope.Item(14)));
        Assert.AreEqual(0, service.Inspections);
    }

    [TestMethod]
    public void 已取消的可移除检查不能重新启用新建模式中的移除()
    {
        Service service = new() { DelayedInspection = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Scope scope = new(false, 96, 13, service, wait: false);
        NativeFindTestPump.Dispatch(scope.Dialog.HandleForTest);
        Task work = scope.Dialog.WorkForTest;
        scope.Click(10);
        service.DelayedInspection.SetResult(GitWorktreeRemovalReadinessResult.Success(new(true, true, false, null)));
        Assert.IsTrue(work.Wait(TimeSpan.FromSeconds(5)));
        NativeFindTestPump.Dispatch(scope.Dialog.HandleForTest);
        Assert.IsFalse(NativeMethods.IsWindowEnabled(scope.Item(11)));
        Assert.IsTrue(NativeMethods.IsWindowVisible(scope.Item(14)));
    }

    [TestMethod]
    public void 取消等待服务返回且迟到的真实成功不伪装成取消()
    {
        Service service = new() { DelayedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Scope scope = new(false, 96, 13, service);
        scope.PrepareDraft();
        Assert.IsFalse(Directory.Exists(Service.DraftPath), "假服务不得打开真实工作区。");
        scope.Click(14);
        Assert.IsTrue(scope.Dialog.BusyForTest);
        Assert.AreEqual(scope.Item(16), NativeMethods.GetFocus());
        scope.Dialog.MoveFocus(false);
        Assert.AreEqual(scope.Item(16), NativeMethods.GetFocus());
        scope.Click(16);
        Assert.IsTrue(service.WriteToken.IsCancellationRequested);
        Assert.IsTrue(scope.Dialog.BusyForTest);
        service.DelayedWrite.SetResult(GitActionResult.Success(null));
        scope.WaitIdle();
        Assert.Contains(UiText.WorktreeOperationCompleted, scope.Statuses);
        Assert.AreEqual(2, service.Reads);
    }

    [TestMethod]
    public void 宿主销毁后写操作晚到不访问窗口也不报告成功()
    {
        Service service = new() { DelayedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using Scope scope = new(false, 96, 13, service);
        scope.PrepareDraft(); scope.Click(14);
        Task work = scope.Dialog.WorkForTest;
        nint dialog = scope.Dialog.HandleForTest, panel = scope.Dialog.DetailPanelForTest;
        _ = NativeMethods.DestroyWindow(scope.Owner);
        Assert.IsTrue(service.WriteToken.IsCancellationRequested);
        service.DelayedWrite.SetResult(GitActionResult.Success(null));
        Assert.IsTrue(work.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(NativeMethods.IsWindow(dialog));
        Assert.IsFalse(NativeMethods.IsWindow(panel));
        Assert.AreEqual(0, NativeWorktreeManagerDialog.InstanceCountForTest);
        Assert.DoesNotContain(UiText.WorktreeOperationCompleted, scope.Statuses);
        scope.Dialog.Dispose();
    }

    [TestMethod]
    public void 中文组词不抢占回车与取消且关闭后清除悬停()
    {
        using Scope scope = new(false, 96, 13);
        scope.PrepareDraft();
        _ = NativeMethods.SendMessage(scope.Item(20), 0x010D, 0, 0);
        Assert.IsFalse(scope.Dialog.HandleKey(new() { Window = scope.Item(20), MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = 13 }));
        Assert.IsFalse(scope.Dialog.HandleKey(new() { Window = scope.Item(20), MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = 27 }));
        Assert.AreEqual(0, scope.Service.Creates);
        _ = NativeMethods.SendMessage(scope.Item(20), 0x010E, 0, 0);
        _ = NativeMethods.SendMessage(scope.Item(14), NativeMethods.WindowMessageMouseMove, 0, (5 << 16) | 5);
        Assert.AreEqual(scope.Item(14), scope.Dialog.HoveredForTest);
        _ = NativeMethods.EnableWindow(scope.Item(14), false);
        Assert.AreEqual(0, scope.Dialog.HoveredForTest);
        Assert.IsTrue(scope.Dialog.HandleKey(new() { Window = scope.Item(20), MessageId = NativeMethods.WindowMessageKeyDown, WordParameter = 27 }));
        Assert.AreEqual(0, scope.Dialog.HandleForTest);
        Assert.AreEqual(0, NativeWorktreeManagerDialog.InstanceCountForTest);
    }

    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds));
        return bounds;
    }

    private static void Contains(NativeMethods.Rectangle outer, NativeMethods.Rectangle inner)
    {
        Assert.IsTrue(inner.Left >= outer.Left && inner.Right <= outer.Right && inner.Top >= outer.Top && inner.Bottom <= outer.Bottom,
            $"控件越界：({inner.Left},{inner.Top},{inner.Right},{inner.Bottom}) / ({outer.Left},{outer.Top},{outer.Right},{outer.Bottom})");
    }

    private static void AssertTextFits(nint control)
    {
        var text = NativeDialogBody.Measure(control, NativeMethods.GetWindowTextValue(control));
        var box = Bounds(control);
        Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(16), box.Right - box.Left);
        Assert.IsGreaterThanOrEqualTo(text.Height + NativeTheme.Scale(8), box.Bottom - box.Top);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _family = NativeTheme.UiFontFamilyForTest;
        private readonly double _size = NativeTheme.UiFontSizeForTest;
        private readonly IDisposable _scale;
        private readonly nint _dpi;
        internal readonly Service Service;
        internal nint Owner { get; }
        internal NativeWorktreeManagerDialog Dialog { get; }
        internal int Thread { get; } = Environment.CurrentManagedThreadId;
        internal int LastStatusThread;
        internal List<string> Statuses { get; } = [];

        internal Scope(bool dark, int dpi, int size, Service? service = null, bool wait = true)
        {
            Service = service ?? new();
            _dpi = SetThreadDpiAwarenessContext(-4);
            _scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", size);
            Owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, "Worktree 隔离测试宿主",
                NativeMethods.WindowStylePopup | NativeMethods.WindowStyleVisible, 0, 0,
                NativeTheme.Scale(1024), NativeTheme.Scale(640), 0, 0, NativeMethods.GetModuleHandle(null), 0);
            Dialog = new(Owner, GitRepositorySnapshot.PlainDirectory(AppContext.BaseDirectory), Service,
                new ApplicationSettings { Theme = dark ? "Dark" : "Light", TextFontSize = size, FontSize = 13 },
                status => { Statuses.Add(status); LastStatusThread = Environment.CurrentManagedThreadId; });
            _ = NativeMethods.ShowWindow(Dialog.HandleForTest, NativeMethods.ShowNormal);
            _ = NativeMethods.UpdateWindow(Dialog.HandleForTest);
            if (wait) WaitIdle();
        }

        internal nint Item(int id) => GetDialogItem(id is 13 or 14 or 15 or 19 or 20 or 21 or 22
            ? Dialog.DetailPanelForTest : Dialog.HandleForTest, id);
        internal void Click(int id) => _ = NativeMethods.SendMessage(Item(id), 0x00F5, 0, 0);
        internal void PrepareDraft()
        {
            Click(19);
            _ = NativeMethods.SetWindowText(Item(20), Service.DraftPath);
            _ = NativeMethods.SetWindowText(Item(21), "main");
            _ = NativeMethods.SetWindowText(Item(22), "feature/layout");
        }

        internal void WaitIdle()
        {
            long deadline = Environment.TickCount64 + 5000;
            while (Dialog.BusyForTest || !Dialog.WorkForTest.IsCompleted)
            {
                NativeFindTestPump.Dispatch(Dialog.HandleForTest);
                Assert.IsLessThan(deadline, Environment.TickCount64, "Worktree 结果未按时接纳。");
                System.Threading.Thread.Sleep(1);
            }
            Dialog.WorkForTest.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Dialog.Dispose();
            if (NativeMethods.IsWindow(Owner)) _ = NativeMethods.DestroyWindow(Owner);
            NativeTheme.ConfigureUiTypography(_family, _size);
            _scale.Dispose();
            _ = SetThreadDpiAwarenessContext(_dpi);
        }
    }

    private sealed class Service : IGitWorktreeService
    {
        internal const string DraftPath = @"Z:\Augit-仅用于假服务的不存在目录";
        internal const string Failure = "创建 Worktree 失败：该目标分支已在其他目录检出。请保留当前输入并选择另一个来源分支后重试。";
        internal static readonly GitWorktreeInfo[] Worktrees =
        [new(@"D:\github\Augit", "abcdef", "main", false, false, false, null, false, null, true),
            new(@"D:\github\Augit-ux", "abcdef", "feature/ux", false, false, false, null, false, null, false)];
        internal int Reads, Inspections, Creates;
        internal CancellationToken ReadToken, WriteToken;
        internal TaskCompletionSource<GitWorktreeListResult>? DelayedRead;
        internal TaskCompletionSource<GitWorktreeRemovalReadinessResult>? DelayedInspection;
        internal TaskCompletionSource<GitActionResult>? DelayedWrite;
        public Task<GitWorktreeListResult> ReadAsync(GitRepositorySnapshot repository, CancellationToken cancellationToken = default)
        {
            Reads++; ReadToken = cancellationToken;
            return DelayedRead?.Task ?? Task.FromResult(GitWorktreeListResult.Success(Worktrees));
        }
        public Task<GitWorktreeRemovalReadinessResult> InspectRemovalReadinessAsync(GitRepositorySnapshot repository, string worktreePath, CancellationToken cancellationToken = default)
        {
            Inspections++;
            return DelayedInspection?.Task ?? Task.FromResult(GitWorktreeRemovalReadinessResult.Success(new(true, true, false, null)));
        }
        public Task<GitActionResult> CreateAsync(GitRepositorySnapshot repository, string destinationPath, string sourceBranch, string? newBranch = null, CancellationToken cancellationToken = default)
        {
            Creates++; WriteToken = cancellationToken;
            return DelayedWrite?.Task ?? Task.FromResult(GitActionResult.Failure(GitOperationFailureKind.CommandFailed, Failure));
        }
        public Task<GitActionResult> RemoveAsync(GitRepositorySnapshot repository, string worktreePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")] private static extern nint GetDialogItem(nint parent, int identifier);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}
