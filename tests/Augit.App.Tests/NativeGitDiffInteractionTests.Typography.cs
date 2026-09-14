using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    private static readonly int[] CommitFontSizes = [13, 19, 40, 13];
    private static readonly int[] CommitPanelWidths = [260, 360, 520];

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 提交字号切换保持草稿勾选Diff且窄栏动作不重叠(int dpi, string theme)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunScenarioAsync(async (window, panel) =>
        {
            Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
            Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
            panel.SetCommitMessageForTest("fix: 字号切换保留草稿");
            int selected = SelectedChangeIndex(panel), count = panel.SelectedFileCountForTest;
            int requests = panel.DiffRequestCountForTest, resets = panel.ChangesListResetCountForTest;
            string diff = panel.DiffTextForTest;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo field = typeof(MainWindow).GetField("_settings", flags)!;
            ApplicationSettings original = (ApplicationSettings)field.GetValue(window)!;
            MethodInfo apply = typeof(MainWindow).GetMethod("ApplyAppearance", flags)!;
            try
            {
                foreach (int size in CommitFontSizes)
                {
                    field.SetValue(window, original with { TextFontSize = size, Theme = theme });
                    apply.Invoke(window, null);
                    foreach (int width in CommitPanelWidths)
                    {
                        _ = NativeMethods.MoveWindow(panel.Handle, NativeTheme.Scale(55), NativeTheme.Scale(45),
                            NativeTheme.Scale(width), NativeTheme.Scale(640), true);
                        int h = NativeTheme.UiLineHeight;
                        var outer = CommitControlBounds(panel.Handle);
                        var amend = CommitControlBounds(CommitChild(panel.Handle, 24));
                        var last = CommitControlBounds(CommitChild(panel.Handle, 39));
                        var quantity = CommitControlBounds(CommitChild(panel.Handle, 69));
                        var commit = CommitControlBounds(CommitChild(panel.Handle, 25));
                        var push = CommitControlBounds(CommitChild(panel.Handle, 26));
                        var settings = CommitControlBounds(CommitChild(panel.Handle, 40));
                        var feedback = CommitControlBounds(panel.CommitFeedbackHandleForTest);
                        var edit = CommitControlBounds(panel.CommitMessageHandleForTest);
                        Assert.IsTrue(amend.Right <= last.Left && last.Right <= quantity.Left, "Amend、历史入口和数量不得重叠。");
                        Assert.IsTrue(commit.Bottom <= push.Top || commit.Right <= push.Left, "提交与提交并推送不得重叠。");
                        Assert.IsLessThanOrEqualTo(settings.Left, commit.Right, "设置图标不得覆盖提交按钮。");
                        Assert.IsTrue(push.Bottom <= settings.Top || settings.Bottom <= push.Top || push.Right <= settings.Left,
                            "设置图标不得覆盖提交并推送。");
                        Assert.IsTrue(feedback.Bottom <= edit.Top && edit.Bottom <= commit.Top, "错误行、输入区和动作区不得重叠。");
                        foreach (var bounds in new[] { amend, last, quantity, feedback, edit, commit, push })
                        {
                            Assert.IsTrue(bounds.Top >= outer.Top && bounds.Bottom <= outer.Bottom && bounds.Left >= outer.Left && bounds.Right <= outer.Right);
                            Assert.IsGreaterThanOrEqualTo(h, bounds.Bottom - bounds.Top, $"{size}px 的文字区必须容纳一行文字。");
                        }
                        Assert.AreEqual(NativeTheme.Scale(30), settings.Bottom - settings.Top);
                        Assert.IsGreaterThanOrEqualTo(h + NativeTheme.Scale(6), unchecked((int)NativeMethods.SendMessage(panel.ChangesListHandleForTest, 0x01A1, 0, 0)));
                        Assert.AreEqual(selected, SelectedChangeIndex(panel));
                        Assert.AreEqual(count, panel.SelectedFileCountForTest);
                        Assert.AreEqual("fix: 字号切换保留草稿", panel.CommitMessageForTest);
                        Assert.AreEqual(diff, panel.DiffTextForTest);
                    }
                }
                Assert.AreEqual(requests, panel.DiffRequestCountForTest);
                Assert.AreEqual(resets, panel.ChangesListResetCountForTest);

                // 使用测试自己的临时仓库验证空态；收起次要说明，不能压住提交输入。
                foreach (string path in Directory.EnumerateFiles(window.WorkspaceRoot!, "*.txt")) File.Delete(path);
                window.RequestGitRefreshForTest();
                await WaitUntilAsync(() => panel.ChangedFileCount == 0 && !panel.RefreshingForTest);
                field.SetValue(window, original with { TextFontSize = 40, Theme = theme });
                apply.Invoke(window, null);
                _ = NativeMethods.MoveWindow(window.Handle, 0, 0, NativeTheme.Scale(1024), NativeTheme.Scale(640), true);
                var emptyTitle = CommitControlBounds(CommitChild(panel.Handle, 64));
                var options = CommitControlBounds(CommitChild(panel.Handle, 24));
                Assert.IsTrue(NativeMethods.IsWindowVisible(CommitChild(panel.Handle, 64)));
                Assert.IsGreaterThanOrEqualTo(emptyTitle.Bottom, options.Top, "空态不能覆盖 Amend。");
                Assert.IsFalse(NativeMethods.IsWindowVisible(CommitChild(panel.Handle, 65)), "最小窗口大字号收起次要说明。");
                Assert.IsFalse(panel.CommitActionEnabledForTest);
                var message = CommitControlBounds(panel.CommitMessageHandleForTest);
                Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight, message.Bottom - message.Top);
            }
            finally
            {
                field.SetValue(window, original);
                apply.Invoke(window, null);
            }
        });
    }

    private static NativeMethods.Rectangle CommitControlBounds(nint handle)
    {
        Assert.IsTrue(NativeMethods.GetWindowRectangle(handle, out var bounds));
        return bounds;
    }

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern nint CommitChild(nint parent, int identifier);
}
