using System.Text;

namespace Augit.App.Tests;

public sealed partial class NativeDocumentFindInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 组词内容变化不扫描或定位且结束只接纳最终查询(bool background)
    {
        using ViewScope scope = new("cat 中文 中文\n" + (background ? new string('x', 100_000) : string.Empty));
        nint edit = scope.Open("cat"), editor = Child(scope.View.Handle, 100);
        int scans = scope.View.FindStatusScanCountForTest;
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        foreach (string draft in new[] { "z", "zhong", "中文" })
        {
            _ = NativeMethods.SetWindowText(edit, draft);
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest, "组词中的 EN_CHANGE 不得启动扫描。");
            Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
            Assert.AreEqual(string.Empty, scope.View.FindStatusForTest);
        }
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("1/2", scope.View.FindStatusForTest);
        Assert.AreEqual((nint)4, NativeMethods.SendMessage(editor, 2009, 0, 0));
        Assert.AreEqual(edit, NativeMethods.GetFocus());
        Assert.IsTrue(scope.View.IsTextReadOnly);
    }

    [TestMethod]
    public void 取消组词并恢复原查询保留当前匹配和计数缓存()
    {
        using ViewScope scope = new("cat xx cat yy cat");
        nint edit = scope.Open("cat"), editor = Child(scope.View.Handle, 100);
        Key(edit, NativeMethods.VirtualKeyEnter);
        int scans = scope.View.FindStatusScanCountForTest;
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        _ = NativeMethods.SetWindowText(edit, "临时输入");
        _ = NativeMethods.SetWindowText(edit, "cat");
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("2/3", scope.View.FindStatusForTest);
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
        Key(edit, NativeMethods.VirtualKeyEnter);
        Assert.AreEqual("3/3", scope.View.FindStatusForTest);
    }

    [TestMethod]
    public void 组词最终文字重复通知不丢失后续外部刷新的匹配位置()
    {
        using ViewScope scope = new("cat xx cat yy cat");
        nint edit = scope.Open("cat");
        Key(edit, NativeMethods.VirtualKeyEnter);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        _ = NativeMethods.SetWindowText(edit, "临时");
        _ = NativeMethods.SetWindowText(edit, "cat");
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        _ = NativeMethods.SetWindowText(edit, "cat");
        scope.View.ReloadAsync(scope.Result with { Text = scope.Source + " zz cat" }).GetAwaiter().GetResult();
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual("2/4", scope.View.FindStatusForTest);
        Key(edit, NativeMethods.VirtualKeyEnter);
        Assert.AreEqual("3/4", scope.View.FindStatusForTest);
    }

    [TestMethod]
    public void 组词期间外部刷新等最终文字确定后再统计且保留阅读位置()
    {
        using ViewScope scope = new("cat xx cat yy cat");
        nint edit = scope.Open("cat"), editor = Child(scope.View.Handle, 100);
        Key(edit, NativeMethods.VirtualKeyEnter);
        int scans = scope.View.FindStatusScanCountForTest;
        nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        scope.View.ReloadAsync(scope.Result with { Text = scope.Source + " zz cat" }).GetAwaiter().GetResult();
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("2/4", scope.View.FindStatusForTest);
        Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
        Assert.AreEqual(edit, NativeMethods.GetFocus());
    }

    [TestMethod]
    public void 隐藏文档收到旧组词开始消息不阻止下次显示后的查找()
    {
        using ViewScope scope = new("cat 中文 中文");
        nint edit = scope.Open("cat");
        scope.View.SetVisible(false);
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        scope.View.SetVisible(true);
        _ = NativeMethods.SetFocus(edit);
        _ = NativeMethods.SetWindowText(edit, "中文");
        NativeFindTestPump.Wait(scope.View);
        Assert.IsFalse(scope.View.IsFindInputComposing);
        Assert.AreEqual("1/2", scope.View.FindStatusForTest);
    }

    [TestMethod]
    public void 组词开始取消旧扫描和排队导航且晚到结果不能定位()
    {
        using ViewScope scope = new("cat 中文 中文\n" + new string('x', 100_000));
        nint edit = scope.Open("cat"), editor = Child(scope.View.Handle, 100);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        scope.View.FindWorkBarrierForTest = async token => { oldToken = token; entered.TrySetResult(); await release.Task; };
        try
        {
            _ = NativeMethods.SetWindowText(edit, "中");
            entered.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Key(edit, NativeMethods.VirtualKeyEnter);
            nint caret = NativeMethods.SendMessage(editor, 2008, 0, 0);
            _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
            Assert.IsTrue(oldToken.IsCancellationRequested);
            _ = NativeMethods.SetWindowText(edit, "中文");
            release.TrySetResult();
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual(caret, NativeMethods.SendMessage(editor, 2008, 0, 0));
            Assert.AreEqual(string.Empty, scope.View.FindStatusForTest);
            scope.View.FindWorkBarrierForTest = null;
            _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
            NativeFindTestPump.Wait(scope.View);
            Assert.AreEqual("1/2", scope.View.FindStatusForTest, "旧查询排队的 Enter 不能应用到最终组词。");
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public void 组词期间失焦只统计最终文字且不抢正文位置()
    {
        using ViewScope scope = new("cat xx 中文 yy 中文");
        nint edit = scope.Open("cat"), editor = Child(scope.View.Handle, 100);
        int scans = scope.View.FindStatusScanCountForTest;
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        _ = NativeMethods.SetWindowText(edit, "中文");
        int end = Encoding.UTF8.GetByteCount(scope.Source);
        _ = NativeMethods.SendMessage(editor, 2160, (nuint)end, end);
        _ = NativeMethods.SetFocus(editor);
        NativeFindTestPump.Wait(scope.View);
        Assert.IsFalse(scope.View.IsFindInputComposing);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("1/2", scope.View.FindStatusForTest);
        Assert.AreEqual((nint)end, NativeMethods.SendMessage(editor, 2008, 0, 0));
        Assert.AreEqual(editor, NativeMethods.GetFocus());
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest, "晚到的结束通知不重复扫描。");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void 组词期间关闭或隐藏后晚到结束不更新查找(bool hideDocument)
    {
        using ViewScope scope = new("cat 中文 中文");
        nint edit = scope.Open("cat");
        int scans = scope.View.FindStatusScanCountForTest;
        _ = NativeMethods.SendMessage(edit, 0x010D, 0, 0);
        _ = NativeMethods.SetWindowText(edit, "中文");
        if (hideDocument) scope.View.SetVisible(false); else scope.View.HideFind();
        _ = NativeMethods.SendMessage(edit, 0x010E, 0, 0);
        Assert.AreEqual(scans, scope.View.FindStatusScanCountForTest);
        if (hideDocument) scope.View.SetVisible(true); else scope.View.ShowFind();
        NativeFindTestPump.Wait(scope.View);
        Assert.AreEqual(scans + 1, scope.View.FindStatusScanCountForTest);
        Assert.AreEqual("1/2", scope.View.FindStatusForTest);
    }
}
