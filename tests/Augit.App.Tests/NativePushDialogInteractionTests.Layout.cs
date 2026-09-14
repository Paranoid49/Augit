using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativePushDialogInteractionTests
{
    private static readonly int[] NoRemoteLayoutIds = [10, 11, 12, 13, 20, 21];
    private static readonly int[] PushTabIds = [30, 11, 10, 13, 1];
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
    public Task 长错误在详情区域滚动且标题底栏与引用列表保持不动(bool dark, int dpi, int size) => RunAsync(async context =>
    {
        await context.ReadyAsync();
        nint body = context.Dialog.DetailPanelForTest;
        nint push = context.Item(10), cancel = context.Item(11), close = context.Item(13), list = context.Item(1);
        var dialogBounds = Bounds(context.Dialog.HandleForTest);
        var pushBounds = Bounds(push);
        var cancelBounds = Bounds(cancel);
        var closeBounds = Bounds(close);
        var listBounds = Bounds(list);
        Contains(Bounds(context.Window.Handle), dialogBounds);
        foreach (nint handle in new[] { push, cancel, close, list, body }) Contains(dialogBounds, Bounds(handle));
        Assert.IsTrue(Bounds(body).Bottom <= pushBounds.Top && listBounds.Bottom <= pushBounds.Top);
        Assert.IsLessThanOrEqualTo(pushBounds.Left, cancelBounds.Right);
        context.Click(10);
        string error = string.Join('\n', Enumerable.Repeat("远端拒绝接收提交，请检查分支保护、权限及最新提交状态。", 18));
        context.Service.Push.TrySetResult(GitRemoteOperationResult.Failure(GitOperationFailureKind.CommandFailed, error));
        await context.IdleAsync();
        Assert.AreEqual(error, NativeMethods.GetWindowTextValue(context.Item(35)));
        _ = NativeMethods.SetFocus(body);
        _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageKeyDown, 0x23, 0); // End。
        int end = NativeMethods.GetScrollPosition(body, 1);
        Assert.IsGreaterThan(0, end);
        var notice = Bounds(context.Item(35));
        var bodyBounds = Bounds(body);
        Assert.IsTrue(notice.Bottom <= bodyBounds.Bottom && notice.Bottom >= bodyBounds.Top, "长错误的最后一行必须可滚入视口。");
        _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageKeyDown, 0x24, 0); // Home。
        Assert.AreEqual(0, NativeMethods.GetScrollPosition(body, 1));
        for (int wheel = 0; wheel < 4; wheel++)
            _ = NativeMethods.SendMessage(context.Item(35), NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(-30 << 16)), 0);
        Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(body, 1), "子文字区域的精细滚轮累计后滚动详情。");
        Assert.AreEqual(pushBounds, Bounds(push));
        Assert.AreEqual(cancelBounds, Bounds(cancel));
        Assert.AreEqual(closeBounds, Bounds(close));
        Assert.AreEqual(listBounds, Bounds(list));
        Assert.AreEqual(dialogBounds, Bounds(context.Dialog.HandleForTest));
        Assert.AreEqual(1, context.Service.PreviewCalls);
        Assert.AreEqual(1, context.Service.PushCalls);
    }, dark, dpi, size);

    [TestMethod]
    [DataRow(false, 96, 13)]
    [DataRow(true, 144, 13)]
    [DataRow(false, 96, 40)]
    [DataRow(true, 144, 40)]
    public Task 无远端布局保留定义远端和禁用标签且不覆盖底部动作(bool dark, int dpi, int size) => RunAsync(async context =>
    {
        _ = NativeMethods.MoveWindow(context.Window.Handle, 0, 0, NativeTheme.Scale(1024), NativeTheme.Scale(640), true);
        await context.ReadyAsync(GitPushPreviewResult.Failure(GitOperationFailureKind.CommandFailed, "请定义远端。", true, "refs/heads/topic"));
        var dialog = Bounds(context.Dialog.HandleForTest);
        foreach (int id in NoRemoteLayoutIds) Contains(dialog, Bounds(context.Item(id)));
        Assert.IsLessThanOrEqualTo(Bounds(context.Item(21)).Left, Bounds(context.Item(20)).Right);
        Assert.IsLessThanOrEqualTo(Bounds(context.Item(10)).Top, Bounds(context.Item(20)).Bottom);
        Assert.IsLessThanOrEqualTo(Bounds(context.Item(10)).Top, Bounds(context.Item(21)).Bottom);
        Assert.IsLessThanOrEqualTo(Bounds(context.Item(20)).Top, Bounds(context.Dialog.DetailPanelForTest).Bottom);
        Assert.IsTrue(NativeMethods.IsWindowEnabled(context.Item(12)));
        Assert.IsFalse(NativeMethods.IsWindowEnabled(context.Item(10)));
    }, dark, dpi, size);

    [TestMethod]
    [DataRow(false, 96)]
    [DataRow(true, 96)]
    [DataRow(false, 120)]
    [DataRow(true, 120)]
    [DataRow(false, 144)]
    [DataRow(true, 144)]
    public Task 提交行按真实字高绘制且悬停焦点只改变本行(bool dark, int dpi) => RunAsync(async context =>
    {
        await context.ReadyAsync();
        nint list = context.Item(1);
        int height = NativeTheme.ContentHeight(27, 8);
        NativeMethods.Rectangle first = default, second = default;
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle, 0, ref first);
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle, 1, ref second);
        Assert.AreEqual(height, first.Bottom - first.Top);
        Assert.AreEqual(height, second.Top - first.Top);
        _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetCurrentSelection, 0, 0);
        _ = NativeMethods.SetFocus(context.Dialog.DetailPanelForTest);
        _ = NativeMethods.UpdateWindow(list);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        nint point = (nint)((((second.Top + second.Bottom) / 2) << 16) | (second.Right - NativeTheme.Scale(12)));
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0, point);
        Assert.AreEqual(1, context.Dialog.HoveredCommitForTest);
        Assert.AreEqual(palette.Hover, CommitPixel(list, second));
        Assert.AreEqual(palette.SelectionInactive, CommitPixel(list, first));
        _ = NativeMethods.UpdateWindow(list);
        for (int move = 0; move < 200; move++) _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0, point);
        Assert.IsFalse(PushUpdateRectangle(list, out _, false), "同一行内悬停不重复重绘。");
        Assert.AreEqual(context.Dialog.DetailPanelForTest, NativeMethods.GetFocus());
        _ = NativeMethods.SetFocus(list);
        Assert.AreEqual(palette.AccentSoft, CommitPixel(list, first));
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseLeave, 0, 0);
        Assert.AreEqual(-1, context.Dialog.HoveredCommitForTest);
        Assert.AreEqual(palette.Panel, CommitPixel(list, second));
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0, point);
        context.Click(10);
        Assert.AreEqual(-1, context.Dialog.HoveredCommitForTest, "开始推送禁用列表时清除悬停。");
        Assert.AreEqual(1, context.Service.PreviewCalls);
        Assert.AreEqual(1, context.Service.PushCalls);
    }, dark, dpi);

    [TestMethod]
    public Task Tab沿提交列表详情底部动作和关闭循环且详情键盘滚动不推送() => RunAsync(async context =>
    {
        await context.ReadyAsync();
        foreach (int id in PushTabIds)
        {
            context.PostKey(NativeMethods.VirtualKeyTab);
            await WaitUntilAsync(() => NativeMethods.GetFocus() == context.Item(id));
        }
        _ = NativeMethods.SetFocus(context.Dialog.DetailPanelForTest);
        foreach (int key in new[] { 0x21, 0x22, 0x23, 0x24, NativeMethods.VirtualKeyUp, NativeMethods.VirtualKeyDown })
            _ = NativeMethods.SendMessage(context.Dialog.DetailPanelForTest, NativeMethods.WindowMessageKeyDown, (nuint)key, 0);
        Assert.AreEqual(0, context.Service.PushCalls);
        _ = NativeMethods.SetFocus(context.Item(13));
        context.PostKey(NativeMethods.VirtualKeyEnter);
        await WaitUntilAsync(() => context.Dialog.HandleForTest == 0);
    });
}
