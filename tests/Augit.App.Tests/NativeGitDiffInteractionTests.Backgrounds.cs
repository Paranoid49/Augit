namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    private static readonly bool[] BackgroundModes = [true, false, true];
    private static readonly bool[] BackgroundThemes = [false, true, false];

    [TestMethod]
    public Task 工作区单双栏底色与视觉稿一致且主题切换保持请求和草稿() => RunScenarioAsync(async (window, panel) =>
    {
        Assert.IsTrue(await window.SelectGitFileForTestAsync("a.txt"));
        panel.SetCommitMessageForTest("fix: 主题切换保留草稿");
        int requests = window.GitDiffRequestCountForTest;
        int checkedCount = panel.SelectedFileCountForTest;
        foreach (bool sideBySide in BackgroundModes)
        {
            panel.ClickDiffModeForTest(sideBySide);
            await WaitUntilAsync(() => !panel.DiffLoadingForTest);
            foreach (bool dark in BackgroundThemes)
            {
                panel.ApplyAppearance(new() { Theme = dark ? "Dark" : "Light" });
                NativeDiffBackgroundAssertions.AssertLine(panel.DiffEditorHandleForTest, 0,
                    sideBySide ? 1 : 2, dark ? 0x00304329u : 0x00CFEEC9u);
                Assert.AreEqual(requests, window.GitDiffRequestCountForTest);
                Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
                Assert.AreEqual("fix: 主题切换保留草稿", panel.CommitMessageForTest);
            }
        }
    });
}
