namespace Augit.App.Tests;

public sealed partial class NativeResetRollbackInteractionTests
{
    private static readonly int[] FooterActionIds = [10, 11];
    [TestMethod]
    [DataRow(false, 96, 13)]
    [DataRow(true, 120, 13)]
    [DataRow(false, 144, 13)]
    [DataRow(false, 96, 40)]
    [DataRow(true, 96, 40)]
    [DataRow(false, 120, 40)]
    [DataRow(true, 120, 40)]
    [DataRow(false, 144, 40)]
    [DataRow(true, 144, 40)]
    public async Task Reset字号变化容纳字段和固定底栏(bool dark, int dpi, int size)
    {
        using Session session = new(dark: dark, dpi: dpi, uiSize: size, viewportHeight: 640, viewportWidth: 1024);
        await session.InvokeAsync(() =>
        {
            NativeResetDialog dialog = session.Reset!;
            Contains(Bounds(session.Owner), Bounds(dialog.HandleForTest));
            var body = Bounds(dialog.BodyForTest);
            foreach (nint field in dialog.FieldsForTest)
            {
                Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, Bounds(field).Bottom - Bounds(field).Top);
                _ = NativeMethods.SetFocus(field);
                Contains(body, Bounds(field));
            }
            foreach (int id in FooterActionIds)
            {
                var button = Bounds(session.Item(id));
                var text = NativeDialogBody.Measure(session.Handle, NativeMethods.GetWindowTextValue(session.Item(id)));
                Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(16), button.Right - button.Left);
                Assert.IsGreaterThanOrEqualTo(text.Height, button.Bottom - button.Top);
                Assert.IsGreaterThanOrEqualTo(body.Bottom, button.Top);
                Contains(Bounds(session.Handle), button);
            }
            _ = NativeMethods.SetFocus(session.Item(21));
            _ = Pixel(session.Item(21), 3, 3);
            var modeText = dialog.ModeTextBoundsForTest;
            int required = NativeDialogBody.Measure(session.Handle, NativeResetDialog.ModeDescriptionsForTest[2]).Width;
            Assert.IsGreaterThanOrEqualTo(required, modeText.Right - modeText.Left,
                $"收起态模式说明不得裁切。窗口宽 {Bounds(session.Handle).Right - Bounds(session.Handle).Left}，字段宽 {Bounds(session.Item(21)).Right - Bounds(session.Item(21)).Left}，文字区 {modeText.Left}..{modeText.Right}。");
            Assert.AreEqual(0, session.Service.Calls);
        });
    }

    [TestMethod]
    public async Task Reset输入留白可聚焦且不清除输入或选择()
    {
        using Session session = new(uiSize: 40, viewportWidth: 1024, viewportHeight: 640);
        await session.InvokeAsync(() =>
        {
            nint edit = session.Item(20), body = session.Reset!.BodyForTest;
            _ = NativeMethods.SetWindowText(edit, "main~2");
            _ = NativeMethods.SetFocus(edit);
            _ = NativeMethods.SendMessage(edit, 0x00B1, 1, 3);
            _ = NativeMethods.SetFocus(session.Item(11));
            var editBounds = Bounds(edit);
            NativeMethods.Point point = new() { X = editBounds.Left - NativeTheme.Scale(4), Y = editBounds.Top + NativeTheme.Scale(3) };
            _ = NativeMethods.ScreenToClient(body, ref point);
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageLeftButtonDown, 0, (nint)((point.Y << 16) | (point.X & 0xFFFF)));
            Assert.AreEqual(edit, NativeMethods.GetFocus());
            Assert.AreEqual("main~2", NativeMethods.GetWindowTextValue(edit));
            Assert.AreEqual((nint)((3 << 16) | 1), NativeMethods.SendMessage(edit, 0x00B0, 0, 0));
            Assert.AreEqual(0, session.Service.Calls);
        });
    }

    [TestMethod]
    public async Task Reset长错误只滚动正文且返回输入保持草稿与底栏()
    {
        using Session session = new(dark: true, dpi: 144, uiSize: 40, viewportHeight: 640, viewportWidth: 1024);
        var footer = default(NativeMethods.Rectangle);
        nint edit = 0;
        await session.InvokeAsync(() =>
        {
            edit = session.Item(20);
            _ = NativeMethods.SetWindowText(edit, "main~2");
            footer = Bounds(session.Item(10));
            Click(session.Item(10));
        });
        string reason = string.Join('\n', Enumerable.Repeat("无法解析目标提交，请检查仓库中的引用名称。", 12));
        session.Service.Complete(Augit.Core.Git.GitActionResult.Failure(Augit.Core.Git.GitOperationFailureKind.CommandFailed, reason));
        await session.WaitIdleAsync();
        await session.InvokeAsync(() =>
        {
            var dialog = session.Reset!;
            nint body = dialog.BodyForTest;
            int measures = dialog.BodyMeasureCountForTest;
            _ = NativeMethods.SendMessage(body, NativeMethods.WindowMessageVerticalScroll, 7, 0);
            Assert.IsGreaterThan(0, NativeMethods.GetScrollPosition(body, 1));
            Assert.IsLessThanOrEqualTo(Bounds(body).Bottom, Bounds(dialog.NoticeForTest).Bottom);
            _ = NativeMethods.SetFocus(edit);
            Contains(Bounds(body), Bounds(edit));
            Assert.AreEqual(footer, Bounds(session.Item(10)));
            Assert.AreEqual("main~2", NativeMethods.GetWindowTextValue(edit));
            Assert.AreEqual(reason, NativeMethods.GetWindowTextValue(dialog.NoticeForTest));
            Assert.AreEqual(measures, dialog.BodyMeasureCountForTest, "滚动与焦点恢复复用换行度量。");
            Assert.AreEqual(1, session.Service.Calls);
        });
    }

    private static void Contains(NativeMethods.Rectangle parent, NativeMethods.Rectangle child)
    {
        Assert.IsTrue(child.Left >= parent.Left && child.Top >= parent.Top && child.Right <= parent.Right && child.Bottom <= parent.Bottom,
            $"控件越界：父 {parent.Left},{parent.Top},{parent.Right},{parent.Bottom}；子 {child.Left},{child.Top},{child.Right},{child.Bottom}");
    }

    [TestMethod]
    [DataRow(false, 96, 13, false)]
    [DataRow(true, 120, 13, true)]
    [DataRow(false, 144, 13, false)]
    [DataRow(false, 96, 40, false)]
    [DataRow(true, 96, 40, true)]
    [DataRow(false, 120, 40, true)]
    [DataRow(true, 120, 40, false)]
    [DataRow(false, 144, 40, false)]
    [DataRow(true, 144, 40, true)]
    public async Task 回滚字号变化保留完整风险说明与固定动作(bool dark, int dpi, int size, bool recycle)
    {
        using Session session = new(rollback: true, dark: dark, dpi: dpi, uiSize: size,
            viewportHeight: 640, viewportWidth: 1024, rollbackRecycle: recycle);
        await session.WaitComparisonAsync();
        await session.InvokeAsync(() =>
        {
            var dialog = session.Rollback!;
            Contains(Bounds(session.Owner), Bounds(session.Handle));
            var body = Bounds(dialog.BodyForTest);
            _ = NativeMethods.GetClientRectangle(dialog.BodyForTest, out var client);
            int textWidth = client.Right - NativeTheme.Scale(58);
            int required = NativeDialogBody.Measure(session.Handle, UiText.RollbackWarningTitle, textWidth, true).Height
                + NativeDialogBody.Measure(session.Handle, UiText.RollbackWarningDetail, textWidth).Height + NativeTheme.Scale(16);
            if (recycle) required += NativeDialogBody.Measure(session.Handle, UiText.RollbackRecycleNotice, textWidth).Height + NativeTheme.Scale(4);
            Assert.IsGreaterThanOrEqualTo(required, dialog.WarningHeightForTest);
            foreach (int id in FooterActionIds)
            {
                var button = Bounds(session.Item(id));
                var text = NativeDialogBody.Measure(session.Handle, NativeMethods.GetWindowTextValue(session.Item(id)));
                Assert.IsGreaterThanOrEqualTo(text.Width + NativeTheme.Scale(16), button.Right - button.Left);
                Assert.IsGreaterThanOrEqualTo(text.Height, button.Bottom - button.Top);
                Assert.IsGreaterThanOrEqualTo(body.Bottom, button.Top);
                Contains(Bounds(session.Handle), button);
            }
            var footer = Bounds(session.Item(10));
            var comparison = dialog.ComparisonForTest!;
            nint handle = comparison.Handle;
            int measures = dialog.BodyMeasureCountForTest;
            _ = NativeMethods.SendMessage(dialog.BodyForTest, NativeMethods.WindowMessageVerticalScroll, 7, 0);
            _ = NativeMethods.SetFocus(session.Item(11));
            foreach (nint target in comparison.FocusTargets.Where(target => target != 0 && NativeMethods.IsWindowVisible(target) && NativeMethods.IsWindowEnabled(target)))
            {
                _ = NativeMethods.SetFocus(target);
                Contains(body, Bounds(target));
            }
            _ = NativeMethods.SendMessage(dialog.BodyForTest, NativeMethods.WindowMessageVerticalScroll, 6, 0);
            Assert.AreEqual(handle, comparison.Handle);
            Assert.AreEqual(footer, Bounds(session.Item(10)));
            Assert.AreEqual(measures, dialog.BodyMeasureCountForTest, "滚动和焦点变化不重复度量风险说明。");
            Assert.AreEqual(0, session.Service.Calls);
        });
    }
}
