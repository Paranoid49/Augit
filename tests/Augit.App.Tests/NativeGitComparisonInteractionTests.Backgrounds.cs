using Augit.Core.Git;

namespace Augit.App.Tests;

public sealed partial class NativeGitComparisonInteractionTests
{
    public TestContext TestContext { get; set; } = null!;
    private const string BackgroundPatch = "@@ -1,4 +1,5 @@\n context\n-old value\n+new value\n+\n tail\n end\n";
    private static readonly bool[] BackgroundThemes = [false, true, false];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 比较两种布局的增删整行底色覆盖空行且随主题原位更新(bool sideBySide) => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(GitComparisonResult.Success(new(GitDiffContentStatus.Ready,
            "HEAD~1", "HEAD", "变更与空行.txt", BackgroundPatch)));
        view.ClickModeForTest(sideBySide);
        await WaitUntilAsync(() => !view.LoadingForTest);
        int renders = view.RenderCountForTest;
        nint[] handles = view.TextHandlesForTest;
        nint focused = handles[sideBySide ? 1 : 0];
        _ = NativeMethods.SetFocus(focused);
        foreach (bool dark in BackgroundThemes)
        {
            view.ApplyAppearance(new() { Theme = dark ? "Dark" : "Light" });
            uint removed = dark ? 0x002D2D4Bu : 0x00D7D7F7u;
            uint added = dark ? 0x00304329u : 0x00CFEEC9u;
            uint panel = NativeTheme.Palette(dark).Panel;
            if (sideBySide)
            {
                NativeDiffBackgroundAssertions.AssertLine(handles[1], 1, 1, removed, TestContext);
                NativeDiffBackgroundAssertions.AssertLine(handles[3], 1, 1, added);
                NativeDiffBackgroundAssertions.AssertLine(handles[1], 2, 0, panel);
                NativeDiffBackgroundAssertions.AssertLine(handles[3], 2, 1, added);
                NativeDiffBackgroundAssertions.AssertLine(handles[3], 3, 0, panel);
            }
            else
            {
                NativeDiffBackgroundAssertions.AssertLine(handles[0], 1, 1, removed, TestContext);
                NativeDiffBackgroundAssertions.AssertLine(handles[0], 2, 2, added);
                NativeDiffBackgroundAssertions.AssertLine(handles[0], 3, 2, added);
                NativeDiffBackgroundAssertions.AssertLine(handles[0], 4, 0, panel);
            }
            Assert.AreEqual(focused, NativeMethods.GetFocus());
            Assert.AreEqual(renders, view.RenderCountForTest);
            Assert.IsEmpty(reload.Calls);
            Assert.IsTrue(view.IsReadOnly);
        }
        view.SetResult(GitComparisonResult.Success(new(GitDiffContentStatus.Ready,
            "HEAD~1", "HEAD", "纯上下文.txt", "@@ -1 +1 @@\n unchanged\n")));
        await WaitUntilAsync(() => !view.LoadingForTest);
        NativeDiffBackgroundAssertions.AssertLine(handles[sideBySide ? 3 : 0], 0, 0, NativeTheme.Palette(false).Panel);
        view.Clear();
        foreach (nint handle in handles)
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(handle, 2046, 0, 0));
    }, preciseDpi: true);

    [TestMethod]
    public Task 比较失败清除所有旧底色且重试只显示新结果的变更() => RunAsync(async (window, view, reload) =>
    {
        view.SetResult(GitComparisonResult.Success(new(GitDiffContentStatus.Ready,
            "HEAD~1", "HEAD", "旧比较.txt", BackgroundPatch)));
        await WaitUntilAsync(() => !view.LoadingForTest);
        view.ClickIgnoreWhitespaceForTest();
        reload.Calls[0].Completion.SetResult(Failure());
        await WaitUntilAsync(() => !view.LoadingForTest);
        foreach (nint handle in view.TextHandlesForTest)
            Assert.AreEqual((nint)0, NativeMethods.SendMessage(handle, 2046, 0, 0));
        view.SetResult(Ready("新比较.txt"));
        await WaitUntilAsync(() => !view.LoadingForTest);
        Assert.AreEqual((nint)0, NativeMethods.SendMessage(view.TextHandlesForTest[1], 2046, 0, 0));
        Assert.AreEqual((nint)1, NativeMethods.SendMessage(view.TextHandlesForTest[3], 2046, 0, 0));
    });
}
