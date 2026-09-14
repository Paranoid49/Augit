using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class NativePushDialogInteractionTests
{
    private static readonly int[] NoRemoteIds = [12, 20, 21];

    [TestMethod]
    public Task 定义远端返回后只重新预览并恢复Push焦点上下文() => RunAsync(async context =>
    {
        await context.ReadyAsync(GitPushPreviewResult.Failure(GitOperationFailureKind.CommandFailed, "请定义远端。", true, "refs/heads/topic"));
        await context.OpenRemoteRoundTripAsync();
        await context.ReadyAsync();
        Assert.AreEqual(2, context.Service.PreviewCalls, "定义远端返回后只重新读取一次 Push 预览。");
        Assert.IsTrue(NativeMethods.IsWindowEnabled(context.Item(10)));
        Assert.AreEqual(context.Item(1), NativeMethods.GetFocus(), "嵌套远端关闭后预览不抢回外部焦点。");
        context.Click(11);
    });
    [TestMethod]
    public Task 预览后台完成后按Enter推送且成功自动关闭恢复主窗口() => RunAsync(async context =>
    {
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Window.Handle));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(10)));
        context.Click(10);
        Assert.AreEqual(0, context.Service.PushCalls);
        await context.ReadyAsync();
        Assert.AreEqual("topic → origin/review", context.Dialog.SummaryTextForTest);
        Assert.AreEqual((nint)2, NativeMethods.SendMessage(context.Item(1), NativeMethods.ListBoxGetCount, 0, 0));
        Assert.AreEqual(context.Item(1), NativeMethods.GetFocus(), "预览完成不转移列表焦点。");
        Assert.IsFalse(NativeMethods.IsWindowVisible(context.Item(20)));
        Assert.IsFalse(NativeMethods.IsWindowVisible(context.Item(21)));
        context.PostKey(NativeMethods.VirtualKeyEnter);
        await WaitUntilAsync(() => context.Dialog.RunningForTest);
        Assert.AreEqual(("origin", "refs/heads/topic", "refs/heads/review"), context.Service.Request);
        Assert.AreEqual(context.Item(11), NativeMethods.GetFocus());
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(10)));
        context.Click(10);
        Assert.AreEqual(1, context.Service.PushCalls, "进行中不重复推送。");
        context.Service.Push.TrySetResult(GitRemoteOperationResult.Success(null, null));
        await WaitUntilAsync(() => context.Dialog.HandleForTest == 0);
        Assert.AreEqual(UiText.PushCompleted, context.Status);
        Assert.AreEqual(context.UiThread, context.StatusThread);
        Assert.IsFalse(context.Service.PushToken.IsCancellationRequested, "成功不能在关闭时被改成取消。");
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 失败或异常保留引用和提交列表且重试不重查预览(bool exception) => RunAsync(async context =>
    {
        await context.ReadyAsync();
        nint list = context.Item(1), target = context.Item(32), push = context.Item(10);
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetCurrentSelection, 1, 0);
        var footer = Bounds(push);
        context.Click(10);
        _ = NativeMethods.SetFocus(context.Dialog.DetailPanelForTest);
        if (exception) context.Service.Push.TrySetException(new InvalidOperationException("不应显示的内部细节"));
        else context.Service.Push.TrySetResult(GitRemoteOperationResult.Failure(GitOperationFailureKind.CommandFailed, "远端拒绝推送，请先更新分支。"));
        await context.IdleAsync();
        Assert.AreEqual(exception ? UiText.PushFailed : "远端拒绝推送，请先更新分支。", context.Dialog.NoticeForTest);
        Assert.AreEqual(list, context.Item(1));
        Assert.AreEqual(target, context.Item(32));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(list, NativeMethods.ListBoxGetCurrentSelection, 0, 0));
        Assert.AreEqual(footer, Bounds(push), "错误不能移动固定动作。");
        Assert.AreEqual(context.Dialog.DetailPanelForTest, NativeMethods.GetFocus(), "失败不能抢回用户已转移的焦点。");
        Assert.IsTrue(NativeMethods.IsWindowEnabled(push));
        context.Click(10);
        Assert.AreEqual(2, context.Service.PushCalls);
        Assert.AreEqual(1, context.Service.PreviewCalls);
        context.Service.Push.TrySetResult(GitRemoteOperationResult.Success(null, null));
        await WaitUntilAsync(() => context.Dialog.HandleForTest == 0);
        Assert.AreEqual(UiText.PushCompleted, context.Status);
    });

    [TestMethod]
    [DataRow("按钮", false)]
    [DataRow("按钮", true)]
    [DataRow("Esc", false)]
    [DataRow("Esc", true)]
    [DataRow("关闭请求", false)]
    [DataRow("关闭请求", true)]
    public Task 取消等待Git退出且晚到成功不能变成推送成功(string action, bool lateSuccess) => RunAsync(async context =>
    {
        await context.ReadyAsync();
        context.Click(10);
        if (action == "按钮") context.Click(11);
        else if (action == "Esc") context.PostKey(NativeMethods.VirtualKeyEscape);
        else _ = NativeMethods.PostMessage(context.Dialog.HandleForTest, NativeMethods.WindowMessageClose, 0, 0);
        await WaitUntilAsync(() => context.Service.PushToken.IsCancellationRequested);
        Assert.IsTrue(context.Dialog.RunningForTest);
        Assert.IsTrue(NativeMethods.IsWindow(context.Dialog.HandleForTest));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(10)));
        Assert.AreEqual("正在取消推送，等待 Git 停止…", context.Dialog.NoticeForTest);
        context.Service.Push.TrySetResult(lateSuccess ? GitRemoteOperationResult.Success(null, null)
            : GitRemoteOperationResult.Failure(GitOperationFailureKind.Cancelled, UiText.OperationCancelled));
        await context.IdleAsync();
        Assert.IsNull(context.Status);
        Assert.AreEqual(UiText.OperationCancelled, context.Dialog.NoticeForTest);
        Assert.AreEqual(UiText.Cancel, NativeMethods.GetWindowTextValue(context.Item(11)));
        Assert.IsTrue(NativeMethods.IsWindowEnabled(context.Item(10)));
        context.Click(11);
        Assert.AreEqual((nint)0, context.Dialog.HandleForTest);
    });

    [TestMethod]
    [DataRow("无远端")]
    [DataRow("查询失败")]
    [DataRow("查询异常")]
    [DataRow("没有提交")]
    public Task 预览无可推送内容时禁止推送且保持可关闭(string state) => RunAsync(async context =>
    {
        if (state == "查询异常")
        {
            context.Service.Preview.TrySetException(new InvalidOperationException("不应显示的内部信息"));
            await WaitUntilAsync(() => context.Dialog.PreviewCompletedForTest);
        }
        else await context.ReadyAsync(state == "没有提交" ? Preview(0)
            : GitPushPreviewResult.Failure(GitOperationFailureKind.CommandFailed, "没有可用远端。", state == "无远端", "refs/heads/topic"));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(10)));
        context.Click(10);
        Assert.AreEqual(0, context.Service.PushCalls);
        foreach (int id in NoRemoteIds)
            Assert.AreEqual(state == "无远端", NativeMethods.IsWindowVisible(context.Item(id)));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(20)));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(21)));
        if (state == "没有提交") Assert.AreEqual(UiText.PushNothingToPush, context.Dialog.NoticeForTest);
        else Assert.AreEqual(state == "无远端" ? UiText.PushNoSelection : state == "查询异常" ? "无法读取待推送提交。" : "没有可用远端。",
            NativeMethods.GetWindowTextValue(context.Item(34)));
        context.Click(11);
    });

    [TestMethod]
    [DataRow("关闭", false)]
    [DataRow("关闭", true)]
    [DataRow("销毁", false)]
    [DataRow("销毁", true)]
    [DataRow("释放", false)]
    [DataRow("释放", true)]
    public Task 预览期间关闭销毁或释放后丢弃晚到结果(string action, bool lateSuccess) => RunAsync(async context =>
    {
        EndDialog(context, action);
        Assert.AreEqual((nint)0, context.Dialog.HandleForTest);
        Assert.IsTrue(context.Service.PreviewToken.IsCancellationRequested);
        context.Service.Preview.TrySetResult(lateSuccess ? Preview()
            : GitPushPreviewResult.Failure(GitOperationFailureKind.CommandFailed, "晚到的失败"));
        await context.Dialog.WorkForTest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsFalse(context.Dialog.PreviewCompletedForTest);
        Assert.IsNull(context.Status);
        Assert.AreEqual(0, NativePushDialog.InstanceCountForTest);
    });

    [TestMethod]
    [DataRow("销毁")]
    [DataRow("释放")]
    public Task 推送期间销毁或释放取消任务且丢弃晚到成功(string action) => RunAsync(async context =>
    {
        await context.ReadyAsync();
        context.Click(10);
        EndDialog(context, action);
        Assert.AreEqual((nint)0, context.Dialog.HandleForTest);
        Assert.IsTrue(context.Service.PushToken.IsCancellationRequested);
        context.Service.Push.TrySetResult(GitRemoteOperationResult.Success(null, null));
        await context.Dialog.WorkForTest.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsNull(context.Status);
        Assert.AreEqual(0, NativePushDialog.InstanceCountForTest);
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 已投递但未接纳的结果在销毁时丢弃(bool pushing) => RunAsync(async context =>
    {
        if (pushing)
        {
            await context.ReadyAsync();
            context.Click(10);
            context.Service.Push.TrySetResult(GitRemoteOperationResult.Success(null, null));
        }
        else context.Service.Preview.TrySetResult(Preview());
        // 短暂等后台投递但不泵 UI，覆盖结果已在消息队列中的关闭边界。
        Assert.IsTrue(context.Dialog.WorkForTest.Wait(TimeSpan.FromSeconds(3)));
        EndDialog(context, "销毁");
        Assert.AreEqual((nint)0, context.Dialog.HandleForTest);
        Assert.IsNull(context.Status);
        Assert.AreEqual(0, NativePushDialog.InstanceCountForTest);
    });

    private static void EndDialog(Context context, string action)
    {
        if (action == "释放") context.Dialog.Dispose();
        else if (action == "销毁") Assert.IsTrue(NativeMethods.DestroyWindow(context.Dialog.HandleForTest));
        else context.Click(11);
    }
}
