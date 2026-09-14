using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    private static readonly bool[] DiffTypographyModes = [true, false];
    private static readonly int[] DiffTypographySizes = [40, 19, 13];
    private static readonly int[] DiffCodeTypographySizes = [40, 19, 13];

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 工作区Diff界面字号扩展文字行而模式图形保持居中(int dpi, string theme)
    {
        // DPI 必须在创建测试窗口前生效；否则正文控件已经按 96 DPI 创建，随后切换 DPI 会把
        // 等宽正文行高重新缩放，测试比较到的是创建时状态与新 DPI 状态，而不是界面字号变化。
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunScenarioAsync(async (window, panel) =>
        {
            string family = NativeTheme.UiFontFamilyForTest;
            double originalSize = NativeTheme.UiFontSizeForTest;
            try
            {
                Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
                panel.SetCommitMessageForTest("fix: Diff 字号改变保留草稿");
                int requests = panel.DiffRequestCountForTest, checkedCount = panel.SelectedFileCountForTest;
                foreach (bool split in DiffTypographyModes)
                {
                    panel.ClickDiffModeForTest(split);
                    await WaitUntilAsync(() => !panel.DiffLoadingForTest);
                    nint body = panel.DiffEditorHandleForTest;
                    nint lineHeight = NativeMethods.SendMessage(body, 2279, 0, 0);
                    _ = NativeMethods.SetFocus(panel.DiffModeToolbarHandlesForTest[0]);
                    foreach (int size in DiffTypographySizes)
                    {
                        NativeTheme.ConfigureUiTypography(family, size);
                        panel.ApplyAppearance(new ApplicationSettings { Theme = theme, TextFontSize = size, FontSize = 13 });
                        NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(panel.DiffFileHeaderHandleForTest, body, split);
                        NativeGitComparisonInteractionTests.AssertHeaderRowsFit(panel.DiffFileHeaderHandleForTest, split);
                        var summary = CommitControlBounds(CommitChild(panel.DiffHandleForTest, 66));
                        var changes = CommitControlBounds(CommitChild(panel.DiffHandleForTest, 67));
                        Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, summary.Bottom - summary.Top);
                        Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, changes.Bottom - changes.Top);
                        foreach (nint button in panel.DiffModeToolbarHandlesForTest)
                        {
                            var icon = CommitControlBounds(button);
                            Assert.AreEqual(NativeTheme.Scale(27), icon.Bottom - icon.Top, 1);
                            Assert.AreEqual((summary.Top + summary.Bottom) / 2, (icon.Top + icon.Bottom) / 2, 1);
                        }
                        Assert.AreEqual(panel.DiffModeToolbarHandlesForTest[0], NativeMethods.GetFocus());
                        Assert.AreEqual(lineHeight, NativeMethods.SendMessage(body, 2279, 0, 0));
                        Assert.AreEqual(requests, panel.DiffRequestCountForTest);
                        Assert.AreEqual(checkedCount, panel.SelectedFileCountForTest);
                        Assert.AreEqual("fix: Diff 字号改变保留草稿", panel.CommitMessageForTest);
                    }
                }
            }
            finally
            {
                NativeTheme.ConfigureUiTypography(family, originalSize);
                panel.ApplyAppearance();
            }
        });
    }

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 工作区Diff等宽字号改变后行号和正文不裁切且不重查(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunScenarioAsync(async (window, panel) =>
        {
            ControlledDiffService service = new();
            panel.SetDiffServiceForTest(service);
            Task<bool> opening = window.SelectGitFileForTestAsync("a.txt");
            string patch = "@@ -99990,100 +99990,100 @@\n-旧正文\n+新正文\n"
                + string.Join('\n', Enumerable.Range(1, 99).Select(index => $" 上下文 {index}")) + "\n";
            await WaitUntilAsync(() => service.HasPending("a.txt"));
            service.Complete("a.txt", GitDiffResult.Success(new(
                GitDiffContentStatus.Ready, "a.txt", null, 20, 20, patch)));
            Assert.IsTrue(await opening);
            Assert.IsTrue(panel.DiffUsesSideBySideForTest);
            StringAssert.Contains(panel.DiffGutterTextForTest, "100089");
            panel.SetCommitMessageForTest("fix: 等宽字号改变保留草稿");
            int requests = panel.DiffRequestCountForTest;
            int selectedCount = panel.SelectedFileCountForTest;
            string text = panel.DiffTextForTest;
            nint[] handles = panel.DiffTextHandlesForTest;
            _ = NativeMethods.SendMessage(handles[3], 2613, 40, 0);
            nint top = NativeMethods.SendMessage(handles[3], 2152, 0, 0);
            _ = NativeMethods.SetFocus(panel.DiffModeToolbarHandlesForTest[1]);
            NativeGitDiffGeometrySnapshot geometry = window.GitDiffGeometryForTest;
            int layouts = window.LayoutInvocationCountForTest;
            foreach (int size in DiffCodeTypographySizes)
            {
                panel.ApplyAppearance(new() { Theme = theme, FontSize = size, TextFontSize = 13 });
                for (int index = 0; index < handles.Length; index++)
                {
                    int padding = NativeTheme.Scale(index == 2 ? 7 : 13);
                    Assert.AreEqual(padding, (int)NativeMethods.SendMessage(handles[index], 2156, 0, 0));
                    Assert.AreEqual(padding, (int)NativeMethods.SendMessage(handles[index], 2158, 0, 0));
                }
                Assert.IsTrue(NativeMethods.GetClientRectangle(handles[2], out var gutter));
                Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(84), gutter.Right - gutter.Left);
                int end = panel.DiffGutterTextForTest.IndexOfAny(['\r', '\n']);
                Assert.IsGreaterThan(0, end);
                int lastX = (int)NativeMethods.SendMessage(handles[2], 2164, 0, end);
                Assert.IsLessThanOrEqualTo(gutter.Right, lastX + NativeTheme.Scale(7), "六位行号与右内距必须完整落在中栏内。");
                int lineHeight = (int)NativeMethods.SendMessage(handles[3], 2279, 0, 0);
                Assert.IsGreaterThanOrEqualTo(
                    (int)Math.Round(NativeTheme.Scale((float)(size * 1.7)), MidpointRounding.AwayFromZero),
                    lineHeight,
                    "等宽字号放大后正文行高不能裁切字形。");
                NativeDiffFileHeaderAssertions.AssertBodyBelowHeader(panel.DiffFileHeaderHandleForTest, handles[3], true);
                Assert.AreEqual(requests, panel.DiffRequestCountForTest);
                Assert.AreEqual(selectedCount, panel.SelectedFileCountForTest);
                Assert.AreEqual(text, panel.DiffTextForTest);
                Assert.AreEqual(top, NativeMethods.SendMessage(handles[3], 2152, 0, 0));
                Assert.AreEqual(panel.DiffModeToolbarHandlesForTest[1], NativeMethods.GetFocus());
                Assert.AreEqual(geometry, window.GitDiffGeometryForTest);
                Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
                Assert.AreEqual("fix: 等宽字号改变保留草稿", panel.CommitMessageForTest);
            }
            panel.ClickDiffModeForTest(false);
            await WaitUntilAsync(() => !panel.DiffLoadingForTest);
            Assert.AreEqual(NativeTheme.Scale(13), (int)NativeMethods.SendMessage(handles[0], 2164, 0, 0));
            Assert.AreEqual(requests, panel.DiffRequestCountForTest);
        });
    }
}
