using System.Reflection;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

public sealed partial class NativeGitDiffInteractionTests
{
    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public async Task Changes悬停仅重绘命中行且选中优先(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        await RunScenarioAsync(async (window, panel) =>
        {
            Assert.IsTrue(await window.EnterGitFileForTestAsync("a.txt"));
            Assert.IsTrue(window.ClickGitFileCheckboxForTest("b.txt"));
            panel.SetCommitMessageForTest("fix: 悬停不改变提交上下文");
            panel.ApplyAppearance(new ApplicationSettings { Theme = dark ? "Dark" : "Light" });
            nint list = panel.ChangesListHandleForTest;
            _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
            var before = ChangesHoverContext(window, panel);
            int index = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt");
            NativeMethods.Rectangle row = ChangesHoverRow(list, index);
            NativeMethods.Rectangle selected = ChangesHoverRow(list, SelectedChangeIndex(panel));
            NativeThemePalette palette = NativeTheme.Palette(dark);
            _ = NativeMethods.UpdateWindow(list);

            MoveChangesPointer(list, row);
            Assert.AreEqual(index, panel.HoveredChangeIndexForTest);
            Assert.IsTrue(ChangesUpdateRectangle(list, out var update, false));
            Assert.AreEqual(row.Top, update.Top, "悬停不能使整栏失效。");
            Assert.AreEqual(row.Bottom, update.Bottom);
            Assert.AreEqual(palette.Hover, CaptureChangesBackground(list, row));
            Assert.AreEqual(palette.SelectionInactive, CaptureChangesBackground(list, selected));
            _ = NativeMethods.UpdateWindow(list);
            for (int move = 0; move < 200; move++) MoveChangesPointer(list, row, move % 3);
            Assert.IsFalse(ChangesUpdateRectangle(list, out _, false), "同一行内移动不重复失效。");
            Assert.AreEqual(before, ChangesHoverContext(window, panel));

            _ = NativeMethods.SetFocus(list);
            MoveChangesPointer(list, selected);
            Assert.AreEqual(palette.AccentSoft, CaptureChangesBackground(list, selected));
            Assert.AreEqual(palette.Panel, CaptureChangesBackground(list, row), "旧悬停行恢复普通背景。");
            _ = NativeMethods.SetFocus(panel.CommitMessageHandleForTest);
            Assert.AreEqual(palette.SelectionInactive, CaptureChangesBackground(list, selected), "悬停不能覆盖失焦选中态。");
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseLeave, 0, 0);
            AssertChangesHoverCleared(panel);
            Assert.AreEqual(before, ChangesHoverContext(window, panel));
        });
    }

    [TestMethod]
    public Task Changes悬停不首次打开Diff且空白负坐标和折叠重新命中() => RunScenarioAsync((window, panel) =>
    {
        nint list = panel.ChangesListHandleForTest;
        int index = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt");
        NativeMethods.Rectangle row = ChangesHoverRow(list, index);
        MoveChangesPointer(list, row);
        Assert.AreEqual(index, panel.HoveredChangeIndexForTest);
        Assert.AreEqual(0, panel.DiffRequestCountForTest);
        Assert.AreEqual(0, window.WorkspaceGitDiffTabCountForTest);
        Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));
        Assert.AreEqual(-1, panel.HoveredChangeIndexForTest, "折叠后的列表空白不算最后一个分组行。");
        Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));
        Assert.AreEqual(index, panel.HoveredChangeIndexForTest, "展开后按原指针位置重新命中。");
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0, unchecked((nint)(-1)));
        Assert.AreEqual(-1, panel.HoveredChangeIndexForTest);
        NativeMethods.Rectangle last = ChangesHoverRow(list, panel.VisibleChangeEntryCountForTest - 1);
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0, ChangesPoint(20, last.Bottom + 2));
        Assert.AreEqual(-1, panel.HoveredChangeIndexForTest);
        int group = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles);
        MoveChangesPointer(list, ChangesHoverRow(list, group));
        Assert.AreEqual(group, panel.HoveredChangeIndexForTest, "分组行也提供悬停反馈。");
        Assert.AreEqual(0, window.WorkspaceGitDiffTabCountForTest);
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task Changes列表快照增删滚动和行高变化后悬停重新命中() => RunScenarioAsync((window, panel) =>
    {
        nint list = panel.ChangesListHandleForTest;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo statusField = typeof(NativeGitPanel).GetField("_status", flags)!;
        MethodInfo populate = typeof(NativeGitPanel).GetMethod("PopulateChanges", flags)!;
        GitStatusSnapshot original = (GitStatusSnapshot)statusField.GetValue(panel)!;
        // 同步提供已读快照，验证实际填充路径；不依赖桌面指针或 Git 查询的完成时机。
        void ApplySnapshot(IReadOnlyList<GitChangedFile> files)
        {
            statusField.SetValue(panel, original with { Files = files });
            populate.Invoke(panel, [files]);
        }
        try
        {
            int index = panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt");
            NativeMethods.Rectangle row = ChangesHoverRow(list, index);
            MoveChangesPointer(list, row);
            GitChangedFile[] removed = original.Files.Where(file => file.RelativePath != "b.txt").ToArray();
            ApplySnapshot(removed);
            Assert.AreEqual(panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "c.txt"), panel.HoveredChangeIndexForTest);
            ApplySnapshot(original.Files);
            Assert.AreEqual(index, panel.HoveredChangeIndexForTest);
            ApplySnapshot(original.Files);
            Assert.AreEqual(index, panel.HoveredChangeIndexForTest, "原位快照更新保留真实命中。");

            GitChangedFile[] many = [.. original.Files, .. Enumerable.Range(0, 80).Select(number =>
                new GitChangedFile($"x{number:D2}.txt", null, GitChangeGroup.UnversionedFiles, GitChangeKind.Untracked, false, true))];
            ApplySnapshot(many);
            _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetTopIndex, 5, 0);
            Assert.AreEqual(index + 5, panel.HoveredChangeIndexForTest);
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageVerticalScroll, 1, 0);
            Assert.AreEqual(index + ChangesTopIndex(panel), panel.HoveredChangeIndexForTest);
            _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseWheel, unchecked((nuint)(-120 << 16)), 0);
            Assert.AreEqual(index + ChangesTopIndex(panel), panel.HoveredChangeIndexForTest);
            _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetTopIndex, 0, 0);
            int height = row.Bottom - row.Top;
            _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetItemHeight, 0, height * 2);
            Assert.AreEqual(((row.Top + row.Bottom) / 2) / (height * 2), panel.HoveredChangeIndexForTest);
            _ = NativeMethods.SendMessage(list, NativeMethods.ListBoxSetItemHeight, 0, height);
            Assert.AreEqual(index, panel.HoveredChangeIndexForTest);
            Assert.AreEqual(0, panel.DiffRequestCountForTest);
        }
        finally
        {
            ApplySnapshot(original.Files);
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    [DataRow("面板隐藏")]
    [DataRow("列表隐藏")]
    [DataRow("禁用")]
    public Task Changes悬停隐藏禁用后清理且重新显示不恢复旧状态(string action) => RunScenarioAsync((window, panel) =>
    {
        nint list = panel.ChangesListHandleForTest;
        NativeMethods.Rectangle row = ChangesHoverRow(list, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt"));
        void SetAvailable(bool available)
        {
            if (action == "面板隐藏") panel.SetVisible(available, false);
            else if (action == "列表隐藏") _ = NativeMethods.ShowWindow(list, available ? NativeMethods.ShowNormal : NativeMethods.ShowHide);
            else _ = NativeMethods.EnableWindow(list, available);
        }
        MoveChangesPointer(list, row);
        Assert.IsTrue(panel.ChangesHoverTrackingForTest);
        try
        {
            SetAvailable(false);
            AssertChangesHoverCleared(panel);
            MoveChangesPointer(list, row);
            AssertChangesHoverCleared(panel);
            SetAvailable(true);
            AssertChangesHoverCleared(panel);
            MoveChangesPointer(list, row);
            Assert.IsTrue(panel.ChangesHoverTrackingForTest);
        }
        finally { SetAvailable(true); }
        return Task.CompletedTask;
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Changes销毁或释放后清理悬停与旧消息(bool dispose) => RunScenarioAsync((window, panel) =>
    {
        nint list = panel.ChangesListHandleForTest;
        NativeMethods.Rectangle row = ChangesHoverRow(list, panel.ChangeEntryIndexForTest(GitChangeGroup.UnversionedFiles, "b.txt"));
        MoveChangesPointer(list, row);
        Assert.IsTrue(panel.ChangesHoverTrackingForTest);
        if (dispose) panel.Dispose();
        else Assert.IsTrue(NativeMethods.DestroyWindow(list));
        AssertChangesHoverCleared(panel);
        MoveChangesPointer(list, row);
        AssertChangesHoverCleared(panel);
        return Task.CompletedTask;
    });

    private static object ChangesHoverContext(MainWindow window, NativeGitPanel panel) => new
    {
        Selection = SelectedChangeIndex(panel),
        Top = ChangesTopIndex(panel),
        Focus = NativeMethods.GetFocus(),
        panel.SelectedFileCountForTest,
        panel.CommitMessageForTest,
        panel.DiffRequestCountForTest,
        panel.DiffTextForTest,
        panel.ChangesPopulateCountForTest,
        PanelLayouts = panel.LayoutInvocationCountForTest,
        MainLayouts = window.LayoutInvocationCountForTest,
        window.GitDiffGeometryForTest,
        window.WorkspaceGitDiffTabCountForTest,
        window.LoadedDocumentCountForTest,
    };

    private static void AssertChangesHoverCleared(NativeGitPanel panel)
    {
        Assert.AreEqual(-1, panel.HoveredChangeIndexForTest);
        Assert.IsFalse(panel.ChangesHoverTrackingForTest);
    }

    private static NativeMethods.Rectangle ChangesHoverRow(nint list, int index)
    {
        NativeMethods.Rectangle row = default;
        Assert.AreNotEqual((nint)(-1), NativeMethods.SendMessage(list, NativeMethods.ListBoxGetItemRectangle, unchecked((nuint)index), ref row));
        return row;
    }

    private static nint ChangesPoint(int x, int y) => (nint)((y << 16) | (x & 0xffff));

    private static void MoveChangesPointer(nint list, NativeMethods.Rectangle row, int offset = 0) =>
        _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageMouseMove, 0,
            ChangesPoint(row.Right - NativeTheme.Scale(18) - offset, (row.Top + row.Bottom) / 2));

    private static uint CaptureChangesBackground(nint list, NativeMethods.Rectangle row)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(list, out var client));
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new() { Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(), Width = client.Right, Height = -client.Bottom, Planes = 1, BitCount = 32 },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            Assert.AreNotEqual((nint)0, bitmap);
            // 使用真实 owner-draw 客户区绘制，避免截图受桌面遮挡影响。
            _ = NativeMethods.SendMessage(list, 0x0318, unchecked((nuint)dc), 0x000C);
            return NativeMethods.GetPixel(dc, row.Right - NativeTheme.Scale(18), (row.Top + row.Bottom) / 2);
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangesUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
