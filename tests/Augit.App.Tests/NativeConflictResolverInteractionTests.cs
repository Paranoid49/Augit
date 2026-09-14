using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 慢保存阻止重复操作且失败保留正文和正确按钮状态(bool throws) => RunAsync(async (dialog, service) =>
    {
        Task pending = dialog.SaveForTestAsync();
        Assert.IsTrue(dialog.SaveInProgressForTest);
        Assert.AreEqual(UiText.ConflictApplying, dialog.NoticeForTest);
        Assert.IsTrue(dialog.ResultIsReadOnlyForTest);
        for (int id = 1; id <= 9; id++) Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, id)));
        nint handle = dialog.HandleForTest;
        _ = NativeMethods.SendMessage(handle, NativeMethods.WindowMessageCommand, 6, Item(dialog, 6));
        _ = NativeMethods.SendMessage(handle, NativeMethods.WindowMessageClose, 0, 0);
        Assert.AreEqual(handle, dialog.HandleForTest, "保存过程中普通关闭不能丢弃仍在提交的结果。");
        Assert.HasCount(1, service.Saves);
        Assert.IsFalse(service.Token.IsCancellationRequested);
        string text = dialog.ResultTextForTest!;
        if (throws) service.Pending.SetException(new IOException("模拟写入失败"));
        else service.Pending.SetResult(Failure());
        await pending;
        Assert.IsFalse(dialog.SaveInProgressForTest);
        Assert.IsFalse(dialog.SavedForTest);
        Assert.IsFalse(dialog.ResultIsReadOnlyForTest);
        Assert.IsTrue(dialog.YoursIsReadOnlyForTest && dialog.TheirsIsReadOnlyForTest);
        Assert.AreEqual(text, dialog.ResultTextForTest);
        Assert.AreEqual("模拟写入失败", dialog.NoticeForTest);
        for (int id = 1; id <= 5; id++) Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, id)), "没有冲突块时不得启用接受或导航。");
        for (int id = 6; id <= 9; id++) Assert.IsTrue(NativeMethods.IsWindowEnabled(Item(dialog, id)));
        service.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task retry = dialog.SaveForTestAsync();
        service.Pending.SetResult(Success());
        await retry;
        Assert.HasCount(2, service.Saves);
        Assert.IsTrue(dialog.SavedForTest);
        Assert.AreEqual((nint)0, dialog.HandleForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 强制销毁取消保存且晚到成功或异常不访问窗口(bool throws) => RunAsync(async (dialog, service) =>
    {
        Task pending = dialog.SaveForTestAsync();
        dialog.Dispose();
        Assert.IsTrue(service.Token.IsCancellationRequested);
        if (throws) service.Pending.SetException(new OperationCanceledException(service.Token));
        else service.Pending.SetResult(Success());
        await pending;
        Assert.IsFalse(dialog.SavedForTest);
        Assert.AreEqual((nint)0, dialog.HandleForTest);
    });

    [TestMethod]
    public Task 未处理冲突不保存并由真实接受按钮更新状态() => RunAsync(async (dialog, service) =>
    {
        await dialog.SaveForTestAsync();
        Assert.IsEmpty(service.Saves);
        Assert.AreEqual(UiText.UnresolvedConflictBlocks, dialog.NoticeForTest);
        _ = NativeMethods.SendMessage(Item(dialog, 3), 0x00F5, 0, 0);
        Assert.AreEqual("中文😀左\n中文😀右\n", dialog.ResultTextForTest);
        Task pending = dialog.SaveForTestAsync();
        Assert.HasCount(1, service.Saves);
        Assert.AreEqual(dialog.ResultTextForTest, service.Saves[0].ResultText);
        service.Pending.SetResult(Failure());
        await pending;
        await Task.Delay(150);
        Assert.AreEqual("模拟写入失败", dialog.NoticeForTest, "接受按钮留下的延迟重绘不能清除保存失败原因。");
        Assert.IsFalse(NativeMethods.IsWindowEnabled(Item(dialog, 3)));
    }, unresolved: true);

    [TestMethod]
    public Task 重复导航复用已解析结果() => RunAsync((dialog, service) =>
    {
        int initial = dialog.ResultParseCountForTest;
        Assert.IsGreaterThan(0, initial);
        _ = NativeMethods.SendMessage(Item(dialog, 4), 0x00F5, 0, 0);
        _ = NativeMethods.SendMessage(Item(dialog, 5), 0x00F5, 0, 0);
        _ = NativeMethods.SendMessage(Item(dialog, 4), 0x00F5, 0, 0);
        Assert.AreEqual(initial, dialog.ResultParseCountForTest);
        return Task.CompletedTask;
    }, unresolved: true);

    [TestMethod]
    public void 多处长短冲突块生成统一虚拟滚动行而不改变正文()
    {
        const string first = "<<<<<<< HEAD\n左一\n=======\n右一\n右二\n右三\n>>>>>>> feature\n";
        const string second = "<<<<<<< HEAD\n左二\n左三\n=======\n右四\n>>>>>>> feature\n";
        string result = first + "公共内容\n" + second;
        string yours = "左一\n左二\n左三\n";
        string theirs = "右一\n右二\n右三\n右四\n";
        IReadOnlyList<(int VirtualStart, int VirtualLength)> rows =
            NativeConflictResolverDialog.CalculateVirtualConflictRowsForTest(yours, result, theirs);
        Assert.HasCount(2, rows);
        Assert.IsGreaterThanOrEqualTo(3, rows[0].VirtualLength);
        Assert.IsGreaterThanOrEqualTo(2, rows[1].VirtualLength);
        Assert.IsGreaterThanOrEqualTo(rows[0].VirtualStart + rows[0].VirtualLength, rows[1].VirtualStart);
        foreach (int side in new[] { 0, 1, 2 })
        {
            (int virtualLine, int yoursLine, int resultLine, int theirsLine) =
                NativeConflictResolverDialog.MapConflictScrollLineForTest(yours, result, theirs, side, 1);
            Assert.IsGreaterThanOrEqualTo(0, virtualLine);
            Assert.IsGreaterThanOrEqualTo(0, yoursLine);
            Assert.IsGreaterThanOrEqualTo(0, resultLine);
            Assert.IsGreaterThanOrEqualTo(0, theirsLine);
        }
    }

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 三栏字号变化保持动作文字可见且不重叠(int dpi, string theme)
    {
        foreach (int size in new[] { 13, 19, 40 })
        {
            await RunAsync((dialog, service) =>
            {
                Assert.IsTrue(NativeMethods.GetWindowRectangle(dialog.HandleForTest, out var outer));
                Assert.AreEqual(string.Empty, NativeMethods.GetWindowTextValue(Item(dialog, 8)), "关闭入口绘制固定图形，不以随字号变大的乘号替代。");
                Assert.AreEqual(UiText.PreviousChange, NativeMethods.GetWindowTextValue(Item(dialog, 4)));
                Assert.AreEqual(UiText.NextChange, NativeMethods.GetWindowTextValue(Item(dialog, 5)));
                int[] ids = [1, 3, 2, 7, 6, 4, 5, 9];
                var bounds = ids.Select(id => Bounds(Item(dialog, id))).ToArray();
                for (int index = 0; index < ids.Length; index++)
                {
                    var box = bounds[index];
                    Assert.IsTrue(box.Left >= outer.Left && box.Right <= outer.Right && box.Top >= outer.Top && box.Bottom <= outer.Bottom);
                    Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight + NativeTheme.Scale(8), box.Bottom - box.Top);
                    string text = NativeMethods.GetWindowTextValue(Item(dialog, ids[index]));
                    nint dc = NativeMethods.GetDeviceContext(dialog.HandleForTest);
                    nint previous = NativeMethods.SelectObject(dc, NativeTheme.UiFont);
                    try
                    {
                        NativeMethods.Rectangle measured = default;
                        _ = NativeMethods.DrawText(dc, text, text.Length, ref measured,
                            NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine);
                        Assert.IsGreaterThanOrEqualTo(measured.Right + NativeTheme.Scale(16), box.Right - box.Left, $"{size}px 的 {text} 被截断。");
                    }
                    finally { _ = NativeMethods.SelectObject(dc, previous); _ = NativeMethods.ReleaseDeviceContext(dialog.HandleForTest, dc); }
                    for (int other = index + 1; other < bounds.Length; other++)
                    {
                        var next = bounds[other];
                        Assert.IsTrue(box.Right <= next.Left || next.Right <= box.Left || box.Bottom <= next.Top || next.Bottom <= box.Top,
                            $"{size}px 的按钮 {ids[index]} 与 {ids[other]} 重叠。");
                    }
                }
                Assert.AreEqual((false, false, false), dialog.LineNumbersVisibleForTest);
                nint region = NativeMethods.CreateRoundRectangleRegion(0, 0, 1, 1, 0, 0);
                try
                {
                    Assert.AreNotEqual(0, GetWindowRgn(dialog.HandleForTest, region));
                    Assert.IsFalse(PtInRegion(region, 0, 0));
                    Assert.IsTrue(PtInRegion(region, (outer.Right - outer.Left) / 2, 0));
                }
                finally { _ = NativeMethods.DeleteObject(region); }
                Assert.IsTrue(dialog.YoursIsReadOnlyForTest && dialog.TheirsIsReadOnlyForTest);
                Assert.IsFalse(dialog.ResultIsReadOnlyForTest);
                Assert.IsGreaterThan(NativeTheme.Scale(60), Bounds(dialog.ResultHandleForTest).Bottom - Bounds(dialog.ResultHandleForTest).Top);
                return Task.CompletedTask;
            }, dpi: dpi, theme: theme, size: size);
        }
    }

    private static GitConflictMutationResult Failure() => GitConflictMutationResult.Failure(GitOperationFailureKind.CommandFailed, "模拟写入失败");
    private static GitConflictMutationResult Success() => GitConflictMutationResult.Success(new(GitOperationKind.Merge, true, false, "main", [], true, false, true));
    private static nint Item(NativeConflictResolverDialog dialog, int id) => id switch
    {
        20 => dialog.YoursHandleForTest,
        21 => dialog.ResultHandleForTest,
        22 => dialog.TheirsHandleForTest,
        _ => GetDlgItem(dialog.HandleForTest, id),
    };
    private static NativeMethods.Rectangle Bounds(nint window)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(window, out var bounds));
        return bounds;
    }

    private static async Task RunAsync(Func<NativeConflictResolverDialog, Service, Task> scenario,
        bool unresolved = false, int dpi = 96, string theme = "Light", int size = 13, string? resultText = null,
        string? yoursText = null, string? theirsText = null, int width = 1024, int height = 760,
        int? codeSize = null, string relativePath = "conflict.txt", string yoursLabel = "当前分支 main",
        string theirsLabel = "合入内容 feature", bool physicalPixels = false)
    {
        using TemporaryDirectory temp = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            // 像素审计与应用清单使用相同的 DPI 感知，避免系统缩放截图后产生混色。
            nint previousDpi = physicalPixels ? SetConflictTestDpiAwareness(-4) : 0;
            Exception? failure = null;
            string font = NativeTheme.UiFontFamilyForTest;
            double fontSize = NativeTheme.UiFontSizeForTest;
            using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
            try
            {
                NativeTheme.ConfigureUiTypography(font, size);
                ApplicationSettings settings = new() { Theme = theme, TextFontSize = size, FontSize = codeSize ?? size };
                using MainWindow window = new(new SettingsStore(temp.GetPath("settings.json")), settings);
                active = window;
                window.Show();
                _ = NativeMethods.SetWindowPosition(window.Handle, 0, 0, 0, NativeTheme.Scale(width), NativeTheme.Scale(height), NativeMethods.SetWindowPositionNoActivate);
                async Task Verify()
                {
                    Service service = new();
                    try
                    {
                        GitRepositorySnapshot repository = new(GitRepositoryKind.WorkingTree, temp.FullPath, temp.FullPath, null, null, GitOperationKind.Merge, true);
                        string text = resultText ?? (unresolved ? "<<<<<<< HEAD\n中文😀左\n=======\n中文😀右\n>>>>>>> feature\n" : "已人工合并😀\n");
                        GitConflictDocument document = new(relativePath, GitConflictContentKind.Text, yoursLabel, theirsLabel,
                            yoursText ?? "中文😀左\n", theirsText ?? "中文😀右\n", text, GitConflictText.Parse(text), new(0, DateTime.UnixEpoch, "version"), GitOperationKind.Merge);
                        using NativeConflictResolverDialog dialog = new(window.Handle, repository, service, document, settings, _ => { });
                        _ = NativeMethods.ShowWindow(dialog.HandleForTest, NativeMethods.ShowNormal);
                        await scenario(dialog, service);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        service.Pending.TrySetResult(Failure());
                        foreach (ReloadRequest reload in service.Reloads) reload.Completion.TrySetCanceled();
                        window.Close();
                    }
                }
                window.Post(() => _ = Verify());
                _ = MainWindow.RunMessageLoop();
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                active = null;
                NativeTheme.ConfigureUiTypography(font, fontSize);
                if (previousDpi != 0) _ = SetConflictTestDpiAwareness(previousDpi);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { active?.Post(active.Close); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "冲突测试窗口未退出。"); }
    }

    [DllImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
    private static extern nint SetConflictTestDpiAwareness(nint context);

    private sealed class Service : IGitConflictService
    {
        internal TaskCompletionSource<GitConflictMutationResult> Pending { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<GitConflictSaveRequest> Saves { get; } = [];
        internal List<ReloadRequest> Reloads { get; } = [];
        internal CancellationToken Token { get; private set; }
        public Task<GitConflictMutationResult> SaveResolvedAsync(GitRepositorySnapshot repository, GitConflictSaveRequest request, CancellationToken cancellationToken = default)
        {
            Saves.Add(request);
            Token = cancellationToken;
            return Pending.Task;
        }
        public Task<GitConflictLoadResult> ReloadWorkingFileAsync(GitRepositorySnapshot repository, GitConflictDocument previous, CancellationToken cancellationToken = default)
        {
            ReloadRequest request = new(previous, new(TaskCreationOptions.RunContinuationsAsynchronously), cancellationToken);
            Reloads.Add(request);
            return request.Completion.Task;
        }
        public Task<GitConflictLoadResult> LoadAsync(GitRepositorySnapshot repository, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitConflictMutationResult> AcceptSideAsync(GitRepositorySnapshot repository, string relativePath, GitConflictSide side, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record ReloadRequest(GitConflictDocument Previous, TaskCompletionSource<GitConflictLoadResult> Completion, CancellationToken Token)
    {
        internal GitConflictLoadResult Changed(string text, string version = "external") => GitConflictLoadResult.Success(Previous with
        {
            ResultText = text,
            Blocks = GitConflictText.Parse(text),
            FileVersion = new(0, DateTime.UnixEpoch, version),
        });
    }

    [DllImport("user32.dll")]
    private static extern nint GetDlgItem(nint dialog, int identifier);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(nint region, int x, int y);
}
