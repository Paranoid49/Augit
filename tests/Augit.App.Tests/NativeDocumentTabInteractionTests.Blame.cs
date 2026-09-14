using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeDocumentTabInteractionTests
{
    [TestMethod]
    [DataRow("切换文件")]
    [DataRow("关闭标签")]
    [DataRow("打开比较")]
    [DataRow("比较后返回")]
    public Task Blame等待正文读取时切走不能将归属写入其他文件(string action) => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "a.txt"));
        TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = barrier.Task;
        GitBlameLine line = new(1, new string('a', 40), "作者", "a@example.invalid", DateTimeOffset.UnixEpoch, "提交", "b.txt", "内容");
        Task pending = window.ShowBlameForTestAsync(Path.Combine(workspace, "b.txt"), [line]);
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            window.DocumentReadBarrierForTest = null;
            if (action == "切换文件") await window.OpenDocumentForTestAsync(Path.Combine(workspace, "c.txt"));
            else if (action == "关闭标签") window.CloseActiveTabForTest();
            else
            {
                window.ShowHistoryComparisonForTest(Ready());
                await WaitUntilAsync(() => !window.ComparisonViewForTest!.LoadingForTest);
                if (action == "比较后返回") window.CloseActiveTabForTest();
            }
            string? path = window.ActiveDocumentPathForTest;
            nint focus = NativeMethods.GetFocus();
            int layouts = window.LayoutInvocationCountForTest;
            window.SetStatusForTest("保留新操作");
            barrier.SetResult();
            await pending;
            if (action == "打开比较")
            {
                // 后台读取可以填充缓存；验收前台比较与正文可见性，不能把缓存路径当成前台路径。
                Assert.IsFalse(window.ActiveDocumentVisibleForTest);
                Assert.IsFalse(window.EmptyDocumentVisibleForTest);
            }
            else if (action == "比较后返回")
            {
                await WaitUntilAsync(() => window.ActiveDocumentVisibleForTest);
                Assert.AreEqual(Path.Combine(workspace, "b.txt"), window.ActiveDocumentPathForTest);
            }
            else Assert.AreEqual(path, window.ActiveDocumentPathForTest);
            if (action != "比较后返回") Assert.AreEqual(focus, NativeMethods.GetFocus());
            Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
            Assert.IsFalse(window.ActiveDocumentIsShowingBlameForTest);
            Assert.AreEqual("保留新操作", window.StatusTextForTest);
            Assert.AreEqual(action == "打开比较", window.ReferenceComparisonVisibleForTest);
        }
        finally { barrier.TrySetResult(); await pending; }
    });
}
