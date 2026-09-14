using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeGitPanelRenderingTests
{
    [TestMethod]
    public void 分组复选状态区分全选部分和空组且排除其他分组()
    {
        GitChangedFile[] files =
        [
            new("a.txt", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true),
            new("b.txt", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true),
            new("new.txt", null, GitChangeGroup.UnversionedFiles, GitChangeKind.Untracked, false, true),
        ];
        GitFileSelection selection = new();
        selection.Reconcile(files);
        Assert.AreEqual(NativeCheckboxState.Checked, NativeGitPanel.ResolveGroupCheckState(files, selection, GitChangeGroup.Changes));
        Assert.AreEqual(NativeCheckboxState.Unchecked, NativeGitPanel.ResolveGroupCheckState(files, selection, GitChangeGroup.UnversionedFiles));
        selection.SetSelected("b.txt", false);
        Assert.AreEqual(NativeCheckboxState.Mixed, NativeGitPanel.ResolveGroupCheckState(files, selection, GitChangeGroup.Changes));
        selection.SetSelected("new.txt", true);
        Assert.AreEqual(NativeCheckboxState.Mixed, NativeGitPanel.ResolveGroupCheckState(files, selection, GitChangeGroup.Changes));
        selection.SetSelected("a.txt", false);
        Assert.AreEqual(NativeCheckboxState.Unchecked, NativeGitPanel.ResolveGroupCheckState(files, selection, GitChangeGroup.Changes));
        Assert.AreEqual(NativeCheckboxState.Unchecked, NativeGitPanel.ResolveGroupCheckState([], selection, GitChangeGroup.Changes));
    }

    [TestMethod]
    public void Changes右键菜单与视觉稿动作一致()
    {
        List<string> expected =
        [
            "显示 Diff",
            "回滚…",
            "文件历史",
            "Blame",
            "复制路径",
            "在资源管理器中定位",
        ];

        CollectionAssert.AreEqual(expected, NativeGitPanel.ChangesContextMenuLabelsForTest.ToArray());
    }

    [TestMethod]
    public void Diff工具栏摘要显示文件位置和差异统计()
    {
        Assert.AreEqual("1/10 个文件", NativeGitPanel.FormatDiffFileSummaryForTest(1, 10));
        Assert.AreEqual("10/10 个文件", NativeGitPanel.FormatDiffFileSummaryForTest(10, 10));
        Assert.AreEqual(string.Empty, NativeGitPanel.FormatDiffFileSummaryForTest(0, 10));
        Assert.AreEqual("2 处差异，0 个已包含", NativeGitPanel.FormatDiffChangeSummaryForTest(2));
        Assert.AreEqual("2 处差异，2 个已包含", NativeGitPanel.FormatDiffChangeSummaryForTest(2, 8));
        Assert.AreEqual(
            UiText.CalculatingDiff,
            NativeGitPanel.FormatDiffChangeSummaryForTest(0, isLoading: true));
    }

    [TestMethod]
    public void 提交信息占位行与输入区共用底色并保持次要文字层级()
    {
        (uint lightBackground, uint lightText) = NativeGitPanel.CommitMessagePlaceholderColorsForTest(dark: false);
        (uint darkBackground, uint darkText) = NativeGitPanel.CommitMessagePlaceholderColorsForTest(dark: true);

        Assert.AreEqual(Rgb(255, 255, 255), lightBackground);
        Assert.AreEqual(Rgb(30, 31, 34), darkBackground);
        Assert.AreEqual(Rgb(100, 104, 112), lightText);
        Assert.AreEqual(Rgb(157, 161, 170), darkText);
    }

    [TestMethod]
    public void 提交工具窗为普通和禁用输入控件统一提供主题底色()
    {
        Assert.IsTrue(NativeGitPanel.IsControlColorMessageForTest(NativeMethods.WindowMessageControlColorEdit));
        Assert.IsTrue(NativeGitPanel.IsControlColorMessageForTest(NativeMethods.WindowMessageControlColorListBox));
        Assert.IsTrue(NativeGitPanel.IsControlColorMessageForTest(NativeMethods.WindowMessageControlColorButton));
        Assert.IsTrue(NativeGitPanel.IsControlColorMessageForTest(NativeMethods.WindowMessageControlColorStatic));
        Assert.IsFalse(NativeGitPanel.IsControlColorMessageForTest(NativeMethods.WindowMessagePaint));
    }

    [TestMethod]
    public void 提交并推送动作以省略号标识后续Push流程()
    {
        StringAssert.EndsWith(UiText.CommitAndPush, "…");
    }

    [TestMethod]
    public void 提交主操作尺寸与视觉稿保持紧凑()
    {
        // 视觉稿中提交按钮约 53 像素、提交并推送约 102 像素，
        // 两者均为 30 像素高；不得扩展成整栏宽度。
        (int commitWidth, int commitPushWidth, int height) = NativeGitPanel.CommitActionDimensionsForTest;
        Assert.AreEqual(NativeTheme.Scale(53), commitWidth);
        Assert.AreEqual(NativeTheme.Scale(102), commitPushWidth);
        Assert.AreEqual(NativeTheme.Scale(30), height);
    }

    [TestMethod]
    public void 提交区改动计数只在存在改动时显示并保持视觉稿格式()
    {
        Assert.AreEqual(string.Empty, NativeGitPanel.FormatCommitChangeCountForTest(0));
        Assert.AreEqual("1 modified", NativeGitPanel.FormatCommitChangeCountForTest(1));
        Assert.AreEqual("35 modified", NativeGitPanel.FormatCommitChangeCountForTest(35));
    }

    [TestMethod]
    public void Changes行高和提交区比例与视觉稿一致()
    {
        NativeCommitLayoutMetrics metrics = NativeGitPanel.CommitLayoutMetricsForTest;

        Assert.AreEqual(NativeTheme.Scale(27), NativeGitPanel.ChangesRowHeightForTest);
        Assert.AreEqual(39, metrics.HeaderHeight);
        Assert.AreEqual(36, metrics.ToolbarHeight);
        Assert.AreEqual(2, metrics.ToolbarBottomGap);
        Assert.AreEqual(155, metrics.MinimumChangesHeight);
        Assert.AreEqual(190, metrics.MinimumCommitHeight);
        Assert.AreEqual(0.42d, metrics.CommitHeightRatio);

        int panelHeight = 667;
        int commitTop = NativeGitPanel.CalculateCommitTopForTest(panelHeight);
        int changesHeight = commitTop
            - NativeTheme.Scale(metrics.HeaderHeight + metrics.ToolbarHeight + metrics.ToolbarBottomGap)
            - NativeTheme.Scale(4);
        Assert.IsGreaterThanOrEqualTo(metrics.MinimumChangesHeight, changesHeight);
        Assert.IsGreaterThanOrEqualTo(metrics.MinimumCommitHeight, panelHeight - commitTop);
        Assert.IsInRange(402, 404, commitTop);
    }

    [TestMethod]
    public void Changes文件行只有复选框状态图标和文字三列()
    {
        NativeChangeListRowLayout group = NativeGitPanel.ChangeListRowLayoutForTest(isGroup: true);
        NativeChangeListRowLayout file = NativeGitPanel.ChangeListRowLayoutForTest(isGroup: false);

        Assert.AreEqual(NativeTheme.Scale(7), file.RowLeft);
        Assert.AreEqual(NativeTheme.Scale(7), file.RowRightInset);
        Assert.AreEqual(NativeTheme.Scale(14), file.CheckboxLeft);
        Assert.AreEqual(NativeTheme.Scale(44), file.FileIconCenter);
        Assert.AreEqual(NativeTheme.Scale(60), file.TextLeft);
        Assert.AreEqual(NativeTheme.Scale(15), file.CheckboxSize);

        Assert.AreEqual(NativeTheme.Scale(18), group.ChevronLeft);
        Assert.AreEqual(NativeTheme.Scale(37), group.CheckboxLeft);
        Assert.AreEqual(file.TextLeft, group.TextLeft);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DoNotParallelize]
    public void Changes按实际字宽优先显示文件名并保留目录间距(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeChangeListTextColumns regular = NativeGitPanel.ChangeListTextColumnsForTest(
            220, 140, 100);
        NativeChangeListTextColumns shortDirectory = NativeGitPanel.ChangeListTextColumnsForTest(
            220, 300, 28);
        NativeChangeListTextColumns shortName = NativeGitPanel.ChangeListTextColumnsForTest(
            220, 40, 300);

        Assert.AreEqual(140, regular.FileNameWidth, "能完整显示的文件名不能被固定比例提前截断");
        Assert.AreEqual(8, regular.Gap);
        Assert.AreEqual(72, regular.DirectoryWidth);
        Assert.AreEqual(184, shortDirectory.FileNameWidth, "短目录应把多余宽度留给长文件名");
        Assert.AreEqual(28, shortDirectory.DirectoryWidth);
        Assert.AreEqual(40, shortName.FileNameWidth);
        Assert.AreEqual(172, shortName.DirectoryWidth, "目录应紧随实际文件名，而不是留下固定空列");
    }

    [TestMethod]
    [DataRow(0, 100)]
    [DataRow(1, 100)]
    [DataRow(80, 100)]
    [DataRow(219, 0)]
    public void Changes极窄行与根目录文件把可用宽度留给文件名(int available, int directory)
    {
        NativeChangeListTextColumns columns = NativeGitPanel.ChangeListTextColumnsForTest(available, 140, directory);

        Assert.AreEqual(available, columns.FileNameWidth);
        Assert.AreEqual(0, columns.Gap);
        Assert.AreEqual(0, columns.DirectoryWidth);
    }

    [TestMethod]
    public void 单栏Diff隐藏Git补丁元数据并按实际行号收紧列宽()
    {
        const string patch = """
            diff --git a/docs/spec.md b/docs/spec.md
            index 1111111..2222222 100644
            --- a/docs/spec.md
            +++ b/docs/spec.md
            @@ -147,2 +147,2 @@
             保留内容
            -旧内容
            +新内容
            """;

        IReadOnlyList<GitDiffLine> lines = GitUnifiedDiffParser.Parse(patch);
        string rendered = NativeGitPanel.BuildUnifiedTextForTest(lines);

        Assert.DoesNotContain("diff --git", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("index ", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("@@", rendered, StringComparison.Ordinal);
        Assert.Contains("147 147", rendered, StringComparison.Ordinal);
        Assert.Contains("148     - 旧内容", rendered, StringComparison.Ordinal);
        Assert.Contains("    148 + 新内容", rendered, StringComparison.Ordinal);
        Assert.AreEqual(3, NativeGitPanel.CalculateLineNumberColumnWidthForTest(lines));
        var styles = NativeGitPanel.BuildUnifiedStyleRangesForTest(lines);
        Assert.HasCount(3, styles.LineNumbers);
        Assert.HasCount(1, styles.RemovedMarkers);
        Assert.HasCount(1, styles.AddedMarkers);
        Assert.AreEqual('-', rendered[styles.RemovedMarkers[0].Start]);
        Assert.AreEqual('+', rendered[styles.AddedMarkers[0].Start]);
    }

    [TestMethod]
    public void 行号超过三位时自动扩展而不是固定六位()
    {
        GitDiffLine[] lines =
        [
            new(GitDiffLineKind.Context, 9999, 10000, "内容"),
        ];

        Assert.AreEqual(5, NativeGitPanel.CalculateLineNumberColumnWidthForTest(lines));
        Assert.StartsWith(" 9999 10000", NativeGitPanel.BuildUnifiedTextForTest(lines), StringComparison.Ordinal);
    }

    [TestMethod]
    public void 双栏Diff将行号集中到中央栏而不是混入正文()
    {
        const string patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1,2 +1,2 @@
             保留
            -旧内容
            +新内容
            """;

        var rendered = NativeGitPanel.BuildSideBySideTextForTest(patch);

        Assert.Contains("旧内容", rendered.OldText, StringComparison.Ordinal);
        Assert.Contains("新内容", rendered.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain("旧内容", rendered.GutterText, StringComparison.Ordinal);
        Assert.Contains("  1      1", rendered.GutterText, StringComparison.Ordinal);
        Assert.Contains("  2      ", rendered.GutterText, StringComparison.Ordinal);
        Assert.Contains("      2", rendered.GutterText, StringComparison.Ordinal);
        Assert.DoesNotContain("旧内容", rendered.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain("新内容", rendered.OldText, StringComparison.Ordinal);
    }

    [TestMethod]
    public void 双栏Diff为删除新增和修改行保留整行背景范围()
    {
        const string patch = """
            @@ -1,3 +1,3 @@
             保留
            -旧内容
            +新内容
            """;

        var backgrounds = NativeGitPanel.BuildSideBySideLineBackgroundsForTest(patch);

        Assert.HasCount(1, backgrounds.OldLineBackgrounds);
        Assert.HasCount(1, backgrounds.NewLineBackgrounds);
        Assert.IsGreaterThan(0, backgrounds.OldLineBackgrounds[0].Length);
        Assert.IsGreaterThan(0, backgrounds.NewLineBackgrounds[0].Length);
    }

    [TestMethod]
    public void Diff局部加载保留双栏骨架而不是空白正文()
    {
        NativeDiffLoadingSkeletonLayout layout = NativeGitPanel.DiffLoadingSkeletonLayoutForTest(1200, 700);

        Assert.AreEqual(NativeTheme.Scale(84), layout.GutterWidth);
        Assert.AreEqual(1200, layout.LeftWidth + layout.GutterWidth + layout.RightWidth);
        Assert.AreEqual(NativeTheme.Scale(62), layout.FirstLineTop);
        Assert.AreEqual(NativeTheme.Scale(20), layout.LineHeight);
        Assert.AreEqual(12, layout.LineCount);
        Assert.IsGreaterThan(0, layout.LeftWidth);
        Assert.IsGreaterThan(0, layout.RightWidth);

        NativeDiffLoadingSkeletonLayout compact = NativeGitPanel.DiffLoadingSkeletonLayoutForTest(220, 80);
        Assert.AreEqual(220, compact.LeftWidth + compact.GutterWidth + compact.RightWidth);
        Assert.AreEqual(0, compact.LineCount);
    }

    [TestMethod]
    public void 无变化Git刷新可以复用Changes列表()
    {
        GitChangedFile[] previous =
        [
            new("src/A.cs", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true),
            new("docs/new.md", null, GitChangeGroup.UnversionedFiles, GitChangeKind.Untracked, false, true),
        ];
        GitChangedFile[] same =
        [
            new("SRC/a.cs", null, GitChangeGroup.Changes, GitChangeKind.Modified, false, true),
            new("DOCS/new.md", null, GitChangeGroup.UnversionedFiles, GitChangeKind.Untracked, false, true),
        ];
        GitChangedFile[] changed =
        [
            previous[0] with { HasStagedChanges = true },
            previous[1],
        ];

        Assert.IsTrue(NativeGitPanel.StatusFilesEquivalentForTest(previous, same));
        Assert.IsFalse(NativeGitPanel.StatusFilesEquivalentForTest(previous, changed));
        Assert.IsFalse(NativeGitPanel.StatusFilesEquivalentForTest(null, same));
    }

    [TestMethod]
    public void 状态变化且路径结构不变时Changes列表可以原位更新()
    {
        GitChangedFile original = new(
            "src/A.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitChangedFile staged = original with { HasStagedChanges = true };
        GitChangedFile added = new(
            "src/B.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Added,
            true,
            false);

        Assert.IsTrue(NativeGitPanel.CanUpdateChangesInPlaceForTest([original], [staged]));
        Assert.IsFalse(NativeGitPanel.CanUpdateChangesInPlaceForTest([original], [original, added]));
        GitChangedFile movedGroup = original with { Group = GitChangeGroup.UnversionedFiles };
        Assert.IsFalse(NativeGitPanel.CanUpdateChangesInPlaceForTest([original], [movedGroup]));
    }

    [TestMethod]
    public void Changes列表增删只替换连续区间并保留两端()
    {
        GitChangedFile first = new(
            "first.txt",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitChangedFile second = first with { RelativePath = "second.txt" };
        GitChangedFile third = first with { RelativePath = "third.txt" };
        GitChangedFile inserted = first with { RelativePath = "inserted.txt" };

        (bool canApply, int prefix, int removed, int added) = NativeGitPanel.ChangesListDeltaForTest(
            [first, second, third],
            [first, inserted, third]);

        Assert.IsTrue(canApply);
        Assert.AreEqual(2, prefix);
        Assert.AreEqual(1, removed);
        Assert.AreEqual(1, added);
    }

    [TestMethod]
    public void Changes列表从空或变为空时仍按分组行执行真实增量()
    {
        GitChangedFile file = new(
            "file.txt",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);

        Assert.IsTrue(NativeGitPanel.ChangesListDeltaForTest([], [file]).CanApply);
        Assert.IsTrue(NativeGitPanel.ChangesListDeltaForTest([file], []).CanApply);
    }

    [TestMethod]
    public void Git状态快照未变化时保留状态并忽略重新读取()
    {
        GitChangedFile file = new(
            "src/A.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitStatusSnapshot previous = new("main", false, [file], "abc");
        GitStatusSnapshot same = new("main", false, [file with { RelativePath = "SRC/a.cs" }], "abc");
        GitStatusSnapshot branchChanged = previous with { CurrentBranch = "feature" };

        Assert.IsTrue(NativeGitPanel.StatusSnapshotsEquivalentForTest(previous, same));
        Assert.IsFalse(NativeGitPanel.StatusSnapshotsEquivalentForTest(previous, branchChanged));
        Assert.IsFalse(NativeGitPanel.StatusSnapshotsEquivalentForTest(null, same));
    }

    [TestMethod]
    public void Git快照未变化时不刷新主窗口Chrome()
    {
        GitChangedFile file = new(
            "src/A.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitStatusSnapshot previous = new("main", false, [file], "abc");
        GitStatusSnapshot same = previous with { Files = [file with { RelativePath = "SRC/a.cs" }] };
        GitStatusSnapshot changed = previous with { CurrentBranch = "feature" };

        Assert.IsFalse(NativeGitPanel.ShouldRefreshChromeForTest(previous, same));
        Assert.IsTrue(NativeGitPanel.ShouldRefreshChromeForTest(previous, changed));
    }

    [TestMethod]
    public void 只有工作树且没有文件时显示提交空态()
    {
        Assert.IsTrue(NativeGitPanel.ShouldShowEmptyChangesStateForTest(GitRepositoryKind.WorkingTree, 0));
        Assert.IsFalse(NativeGitPanel.ShouldShowEmptyChangesStateForTest(GitRepositoryKind.WorkingTree, 1));
        Assert.IsFalse(NativeGitPanel.ShouldShowEmptyChangesStateForTest(GitRepositoryKind.PlainDirectory, 0));
        Assert.IsFalse(NativeGitPanel.ShouldShowEmptyChangesStateForTest(null, 0));
    }

    [TestMethod]
    public void 只有当前文件状态或内容变化才重载当前Diff()
    {
        GitChangedFile current = new(
            "src/A.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitChangedFile unrelated = new(
            "src/B.cs",
            null,
            GitChangeGroup.Changes,
            GitChangeKind.Modified,
            false,
            true);
        GitChangedFile changedCurrent = current with { HasStagedChanges = true };

        Assert.IsFalse(NativeGitPanel.ShouldReloadActiveDiffForTest(
            [current],
            [current, unrelated],
            current.RelativePath,
            [unrelated.RelativePath],
            force: false));
        Assert.IsTrue(NativeGitPanel.ShouldReloadActiveDiffForTest(
            [current],
            [current, unrelated],
            current.RelativePath,
            [current.RelativePath],
            force: false));
        Assert.IsTrue(NativeGitPanel.ShouldReloadActiveDiffForTest(
            [current],
            [changedCurrent],
            current.RelativePath,
            [],
            force: false));
        Assert.IsTrue(NativeGitPanel.ShouldReloadActiveDiffForTest(
            [current],
            [current],
            current.RelativePath,
            [],
            force: true));
    }

    [TestMethod]
    public void Git刷新只有必要时才重新进入Diff展示入口()
    {
        Assert.IsFalse(NativeGitPanel.ShouldShowSelectedDiffAfterRefreshForTest(
            initialLoad: true,
            previousSelectedPath: null,
            currentSelectedPath: "src/A.cs",
            reloadActiveDiff: false));
        Assert.IsTrue(NativeGitPanel.ShouldShowSelectedDiffAfterRefreshForTest(
            initialLoad: false,
            previousSelectedPath: "src/A.cs",
            currentSelectedPath: "src/A.cs",
            reloadActiveDiff: true));
        Assert.IsTrue(NativeGitPanel.ShouldShowSelectedDiffAfterRefreshForTest(
            initialLoad: false,
            previousSelectedPath: "src/A.cs",
            currentSelectedPath: "src/B.cs",
            reloadActiveDiff: false));
        Assert.IsFalse(NativeGitPanel.ShouldShowSelectedDiffAfterRefreshForTest(
            initialLoad: false,
            previousSelectedPath: "src/A.cs",
            currentSelectedPath: "SRC/a.cs",
            reloadActiveDiff: false));
    }

    [TestMethod]
    public void 相同Diff请求去重但忽略空白变化会生成新请求()
    {
        Assert.IsTrue(NativeGitPanel.IsSameDiffRequestForTest("src/A.cs", false, "SRC/a.cs", false));
        Assert.IsFalse(NativeGitPanel.IsSameDiffRequestForTest("src/A.cs", false, "src/A.cs", true));
        Assert.IsFalse(NativeGitPanel.IsSameDiffRequestForTest("src/A.cs", false, "src/B.cs", false));
    }

    [TestMethod]
    public void 项目树只接收真实变化的Git状态快照()
    {
        GitStatusSnapshot first = new("main", false, [], "abc123");
        GitStatusSnapshot equivalent = new("main", false, [], "abc123");
        GitStatusSnapshot changed = new("feature", false, [], "def456");

        Assert.IsTrue(MainWindow.ShouldApplyTreeGitStatusForTest(null, null, "C:\\repo", first));
        Assert.IsFalse(MainWindow.ShouldApplyTreeGitStatusForTest("C:\\repo", first, "c:\\REPO", equivalent));
        Assert.IsTrue(MainWindow.ShouldApplyTreeGitStatusForTest("C:\\repo", first, "C:\\repo", changed));
        Assert.IsTrue(MainWindow.ShouldApplyTreeGitStatusForTest("C:\\repo", first, "D:\\repo", equivalent));
        Assert.IsTrue(MainWindow.ShouldApplyTreeGitStatusForTest("C:\\repo", first, "C:\\repo", null));
        Assert.IsFalse(MainWindow.ShouldApplyTreeGitStatusForTest("C:\\repo", null, "C:\\repo", null));
    }

    [TestMethod]
    public void Diff加载提示遵守防闪烁阈值()
    {
        Assert.AreEqual(150, NativeGitPanel.DiffLoadingThresholdMillisecondsForTest);
    }

    [TestMethod]
    public void Diff结果只允许当前请求版本应用()
    {
        Assert.IsTrue(NativeGitPanel.CanApplyDiffResultForTest(4, 4, disposed: false));
        Assert.IsFalse(NativeGitPanel.CanApplyDiffResultForTest(3, 4, disposed: false));
        Assert.IsFalse(NativeGitPanel.CanApplyDiffResultForTest(4, 4, disposed: true));
    }

    [TestMethod]
    public void Diff前后文件导航跳过分组行且不循环越界()
    {
        bool[] entries = [false, true, false, true, true];

        Assert.AreEqual(3, NativeGitPanel.FindAdjacentFileIndexForTest(entries.Length, 1, 1, index => entries[index]));
        Assert.AreEqual(1, NativeGitPanel.FindAdjacentFileIndexForTest(entries.Length, 3, -1, index => entries[index]));
        Assert.AreEqual(-1, NativeGitPanel.FindAdjacentFileIndexForTest(entries.Length, 4, 1, index => entries[index]));
        Assert.AreEqual(-1, NativeGitPanel.FindAdjacentFileIndexForTest(entries.Length, 0, -1, index => entries[index]));
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}
