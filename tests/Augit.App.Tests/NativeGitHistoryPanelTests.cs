using Augit.Core.Git;

namespace Augit.App.Tests;

[TestClass]
public sealed class NativeGitHistoryPanelTests
{
    [TestMethod]
    [DataRow(270, 1)]
    [DataRow(250, 12)]
    [DataRow(800, 12)]
    public void 多轨历史保持完整图形和可辨认文字且只在需要时增加滚动范围(int viewport, int columns)
    {
        int graphWidth = NativeTheme.Scale(29 + (columns - 1) * 16);
        int extent = NativeGitHistoryPanel.CalculateHistoryRowExtent(NativeTheme.Scale(viewport), graphWidth,
            NativeTheme.Scale(48), NativeTheme.Scale(32));
        NativeHistoryRowTextLayout layout = NativeGitHistoryPanel.GetHistoryRowTextLayoutForTest(
            extent - graphWidth + NativeTheme.Scale(29), 128, NativeTheme.Scale(48), NativeTheme.Scale(110), NativeTheme.Scale(32));
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(120), layout.SubjectWidth);
        Assert.AreEqual(NativeTheme.Scale(48), layout.AuthorWidth);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(32), layout.DateWidth);
        if (columns == 1 || viewport == 800) Assert.AreEqual(NativeTheme.Scale(viewport), extent);
        else Assert.IsGreaterThan(NativeTheme.Scale(viewport), extent);
        Assert.AreEqual(extent, graphWidth + NativeTheme.Scale(8) + layout.SubjectWidth
            + layout.AuthorWidth + layout.DateWidth + layout.ReferenceWidth
            + NativeTheme.Scale(layout.ReferenceWidth > 0 ? 24 : 16));
    }

    [TestMethod]
    public void Git历史只在边界真实变化时应用SetBounds()
    {
        (int X, int Y, int Width, int Height) current = (40, 500, 1100, 300);

        Assert.IsTrue(NativeGitHistoryPanel.ShouldApplyBoundsForTest(false, default, current));
        Assert.IsFalse(NativeGitHistoryPanel.ShouldApplyBoundsForTest(true, current, current));
        Assert.IsTrue(NativeGitHistoryPanel.ShouldApplyBoundsForTest(true, current, (40, 501, 1100, 300)));
        Assert.IsTrue(NativeGitHistoryPanel.ShouldApplyBoundsForTest(true, current, (40, 500, 1100, 301)));
    }

    [TestMethod]
    public void 文本或哈希输入自动选择对应筛选字段()
    {
        GitHistoryFilter text = NativeGitHistoryPanel.UpdateTextOrHashFilterForTest(new(), "修复刷新");
        GitHistoryFilter hash = NativeGitHistoryPanel.UpdateTextOrHashFilterForTest(text, "a1b2c3d");

        Assert.AreEqual("修复刷新", text.Message);
        Assert.IsNull(text.Hash);
        Assert.IsNull(hash.Message);
        Assert.AreEqual("a1b2c3d", hash.Hash);
    }

    [TestMethod]
    public void 历史筛选条件可以组合并单独清除()
    {
        GitHistoryFilter filter = new();
        filter = NativeGitHistoryPanel.UpdateFilterForTest(filter, 0, "fix")!;
        filter = NativeGitHistoryPanel.UpdateFilterForTest(filter, 2, "张三")!;
        filter = NativeGitHistoryPanel.UpdateFilterForTest(filter, 5, "main")!;
        filter = NativeGitHistoryPanel.UpdateFilterForTest(filter, 6, "src\\A.cs")!;

        Assert.AreEqual("fix", filter.Message);
        Assert.AreEqual("张三", filter.Author);
        Assert.AreEqual("main", filter.Branch);
        Assert.AreEqual("src/A.cs", filter.FilePath);

        filter = NativeGitHistoryPanel.UpdateFilterForTest(filter, 2, string.Empty)!;
        Assert.IsNull(filter.Author);
        Assert.AreEqual("fix", filter.Message);
        Assert.AreEqual("main", filter.Branch);
        Assert.AreEqual("src/A.cs", filter.FilePath);
    }

    [TestMethod]
    public void 未选择提交时详情提示按Idea空状态居中()
    {
        Assert.IsTrue(NativeGitHistoryPanel.ShouldCenterMetadataForTest(UiText.SelectCommit));
        Assert.IsFalse(NativeGitHistoryPanel.ShouldCenterMetadataForTest("提交 abc123"));
    }

    [TestMethod]
    public void 历史行元信息同时显示引用作者和时间()
    {
        GitHistoryEntry entry = new(
            string.Empty,
            new string('a', 40),
            new string('a', 7),
            [],
            "张三",
            "zhangsan@example.com",
            DateTimeOffset.Now,
            "feat: 示例提交",
            [new GitReferenceInfo(GitReferenceKind.LocalBranch, "main", IsHead: true)]);

        string metadata = NativeGitHistoryPanel.FormatHistoryMetaForTest(entry);

        StringAssert.Contains(metadata, "main");
        StringAssert.Contains(metadata, "张三");
        StringAssert.Contains(
            metadata,
            entry.AuthorDate.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public void 历史快照相同且顺序不变时不应触发列表重建()
    {
        GitHistoryEntry entry = new(
            "*",
            new string('a', 40),
            new string('a', 7),
            [],
            "张三",
            "zhangsan@example.com",
            DateTimeOffset.Parse(
                "2026-08-31T00:00:00+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            "feat: 示例提交",
            [new GitReferenceInfo(GitReferenceKind.LocalBranch, "main", IsHead: true)]);

        Assert.IsTrue(NativeGitHistoryPanel.HistoryEntriesEqualForTest([entry], [entry]));
        Assert.IsFalse(
            NativeGitHistoryPanel.HistoryEntriesEqualForTest(
                [entry],
                [entry with { Subject = "fix: 另一条提交" }]));
    }

    [TestMethod]
    public void 历史列表增量更新只替换连续变化区间()
    {
        GitHistoryEntry first = CreateHistoryEntry("a", "第一条");
        GitHistoryEntry second = CreateHistoryEntry("b", "第二条");
        GitHistoryEntry replacement = CreateHistoryEntry("c", "替换条");
        GitHistoryEntry last = CreateHistoryEntry("d", "最后一条");

        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.HistoryListDeltaForTest(
                [first, second, last],
                [first, replacement, last]);

        Assert.IsTrue(canApply);
        Assert.AreEqual(1, prefix);
        Assert.AreEqual(1, removedCount);
        Assert.AreEqual(1, addedCount);
    }

    [TestMethod]
    public void 历史列表顶部新增和尾部追加不替换既有提交()
    {
        GitHistoryEntry first = CreateHistoryEntry("a", "第一条");
        GitHistoryEntry second = CreateHistoryEntry("b", "第二条");
        GitHistoryEntry head = CreateHistoryEntry("c", "顶部新增");
        GitHistoryEntry tail = CreateHistoryEntry("d", "尾部追加");

        (bool headCanApply, int headPrefix, int headRemoved, int headAdded) =
            NativeGitHistoryPanel.HistoryListDeltaForTest(
                [first, second],
                [head, first, second]);
        (bool tailCanApply, int tailPrefix, int tailRemoved, int tailAdded) =
            NativeGitHistoryPanel.HistoryListDeltaForTest(
                [first, second],
                [first, second, tail]);

        Assert.IsTrue(headCanApply);
        Assert.AreEqual((0, 0, 1), (headPrefix, headRemoved, headAdded));
        Assert.IsTrue(tailCanApply);
        Assert.AreEqual((2, 0, 1), (tailPrefix, tailRemoved, tailAdded));
    }

    [TestMethod]
    public void 历史列表空结果回退为完整重建()
    {
        GitHistoryEntry entry = CreateHistoryEntry("a", "第一条");

        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.HistoryListDeltaForTest([entry], []);

        Assert.IsFalse(canApply);
        Assert.AreEqual(0, prefix);
        Assert.AreEqual(0, removedCount);
        Assert.AreEqual(0, addedCount);
    }

    [TestMethod]
    public void 历史顶部新增提交时按原顶部哈希保持视口()
    {
        GitHistoryEntry added = CreateHistoryEntry("n", "新提交");
        GitHistoryEntry firstVisible = CreateHistoryEntry("a", "原顶部");
        GitHistoryEntry following = CreateHistoryEntry("b", "下一条");

        Assert.AreEqual(
            1,
            NativeGitHistoryPanel.ResolveHistoryTopIndexForTest(
                [added, firstVisible, following],
                previousTopIndex: 1,
                firstVisible.FullHash));
        Assert.AreEqual(
            0,
            NativeGitHistoryPanel.ResolveHistoryTopIndexForTest(
                [added, firstVisible, following],
                previousTopIndex: 0,
                previousTopHash: null));
    }

    private static GitHistoryEntry CreateHistoryEntry(string hashPrefix, string subject)
    {
        string hash = hashPrefix.PadRight(40, '0');
        return new(
            "*",
            hash,
            hash[..7],
            [],
            "张三",
            "zhangsan@example.com",
            DateTimeOffset.Parse(
                "2026-08-31T00:00:00+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            subject,
            []);
    }

    [TestMethod]
    public void 相同提交详情加载中重复选择时应复用请求()
    {
        string hash = new('a', 40);

        Assert.IsTrue(
            NativeGitHistoryPanel.ShouldReuseCommitDetailsRequestForTest(
                hash,
                hash.ToUpperInvariant(),
                hash,
                detailsLoadedHash: null));
    }

    [TestMethod]
    public void 详情加载中切换到其他提交时不得复用旧请求()
    {
        string loadingHash = new('a', 40);
        string selectedHash = new('b', 40);

        Assert.IsFalse(
            NativeGitHistoryPanel.ShouldReuseCommitDetailsRequestForTest(
                loadingHash,
                selectedHash,
                loadingHash,
                detailsLoadedHash: null));
    }

    [TestMethod]
    public void 已加载的相同提交详情应继续复用()
    {
        string hash = new('a', 40);

        Assert.IsTrue(
            NativeGitHistoryPanel.ShouldReuseCommitDetailsRequestForTest(
                hash,
                hash,
                detailsLoadingHash: null,
                detailsLoadedHash: hash));
    }

    [TestMethod]
    public void 已加载其他提交详情时不得复用当前提交请求()
    {
        string selectedHash = new('a', 40);
        string loadedHash = new('b', 40);

        Assert.IsFalse(
            NativeGitHistoryPanel.ShouldReuseCommitDetailsRequestForTest(
                selectedHash,
                selectedHash,
                detailsLoadingHash: null,
                detailsLoadedHash: loadedHash));
    }

    [TestMethod]
    public void 提交切换后按相同路径恢复变化文件选择和顶部锚点()
    {
        GitCommitChangedFile[] files =
        [
            new(GitChangeKind.Added, "docs/first.md", null),
            new(GitChangeKind.Modified, "docs/second.md", null),
            new(GitChangeKind.Modified, "src/App.cs", null),
        ];

        (int selectedIndex, int topIndex) = NativeGitHistoryPanel.ResolveFileTreeViewForTest(
            files,
            selectedPath: "docs/second.md",
            selectedGroupKey: "docs",
            previousSelectedIndex: 3,
            topAnchor: "F:docs/first.md",
            previousTopIndex: 2);

        Assert.AreEqual(3, selectedIndex);
        Assert.AreEqual(2, topIndex);
    }

    [TestMethod]
    public void 已选变化文件消失后选择同目录最近邻()
    {
        GitCommitChangedFile[] files =
        [
            new(GitChangeKind.Modified, "docs/after.md", null),
            new(GitChangeKind.Modified, "docs/next.md", null),
            new(GitChangeKind.Modified, "src/App.cs", null),
        ];

        (int selectedIndex, _) = NativeGitHistoryPanel.ResolveFileTreeViewForTest(
            files,
            selectedPath: "docs/missing.md",
            selectedGroupKey: "docs",
            previousSelectedIndex: 2,
            topAnchor: null,
            previousTopIndex: 0);

        Assert.AreEqual(2, selectedIndex);
    }

    [TestMethod]
    public void 引用快照只在引用属性变化时判定为不同()
    {
        GitReferenceSnapshot snapshot = new(
            [new GitBranchInfo("main", "refs/heads/main", false, true, null, "a", "提交")],
            [new GitTagInfo("v1", "a", false, null)]);

        Assert.IsTrue(NativeGitHistoryPanel.ReferenceSnapshotsEqualForTest(snapshot, snapshot));
        Assert.IsFalse(
            NativeGitHistoryPanel.ReferenceSnapshotsEqualForTest(
                snapshot,
                snapshot with
                {
                    Branches = [snapshot.Branches[0] with { CommitHash = "b" }],
                }));
    }

    [TestMethod]
    public void Git历史引用树首次加载默认高亮当前分支并保留用户选择()
    {
        (string Text, bool Group)[] rows =
        [
            ("HEAD（当前分支）", false),
            ("本地", true),
            ("main", false),
            ("远程", true),
            ("origin/main", false),
        ];

        Assert.AreEqual(
            2,
            NativeGitHistoryPanel.FindBranchSelectionIndexForTest(rows, null, "main"));
        Assert.AreEqual(
            4,
            NativeGitHistoryPanel.FindBranchSelectionIndexForTest(rows, "origin/main", "main"));
        Assert.AreEqual(
            -1,
            NativeGitHistoryPanel.FindBranchSelectionIndexForTest(rows, "missing", null));
    }

    [TestMethod]
    public void 引用树折叠分组只删除该分组叶子()
    {
        (string Text, int Indent, bool Group, bool Accent)[] expanded =
        [
            ("HEAD（当前分支）", 0, false, false),
            ("本地", 0, true, false),
            ("develop", 1, false, false),
            ("main", 1, false, true),
            ("远程", 0, true, false),
            ("origin/main", 1, false, false),
        ];
        (string Text, int Indent, bool Group, bool Accent)[] collapsed =
        [
            ("HEAD（当前分支）", 0, false, false),
            ("本地", 0, true, false),
            ("远程", 0, true, false),
            ("origin/main", 1, false, false),
        ];

        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.BranchListDeltaForTest(expanded, collapsed);

        Assert.IsTrue(canApply);
        Assert.AreEqual((2, 2, 0), (prefix, removedCount, addedCount));
    }

    [TestMethod]
    public void 引用树搜索无结果时只增量删除结果行()
    {
        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.BranchListDeltaForTest(
                [("HEAD（当前分支）", 0, false, false), ("本地", 0, true, false), ("main", 1, false, true)],
                []);

        Assert.IsTrue(canApply);
        Assert.AreEqual((0, 3, 0), (prefix, removedCount, addedCount));
    }

    [TestMethod]
    public void 清空引用搜索只增量插入原有结果行()
    {
        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.BranchListDeltaForTest(
                [],
                [("HEAD（当前分支）", 0, false, false), ("本地", 0, true, false), ("main", 1, false, true)]);

        Assert.IsTrue(canApply);
        Assert.AreEqual((0, 0, 3), (prefix, removedCount, addedCount));
    }

    [TestMethod]
    public void 引用树当前分支变化只替换受影响行区间()
    {
        (string Text, int Indent, bool Group, bool Accent)[] before =
        [
            ("HEAD（当前分支）", 0, false, false),
            ("本地", 0, true, false),
            ("develop", 1, false, false),
            ("main", 1, false, true),
        ];
        (string Text, int Indent, bool Group, bool Accent)[] after =
        [
            ("HEAD（当前分支）", 0, false, false),
            ("本地", 0, true, false),
            ("develop", 1, false, true),
            ("main", 1, false, false),
        ];

        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.BranchListDeltaForTest(before, after);

        Assert.IsTrue(canApply);
        Assert.AreEqual((2, 2, 2), (prefix, removedCount, addedCount));
    }

    [TestMethod]
    public void 历史普通行保持面板底色且只高亮选中行()
    {
        (uint lightPanel, uint lightSelection) = NativeGitHistoryPanel.HistoryRowColorsForTest(dark: false);
        (uint darkPanel, uint darkSelection) = NativeGitHistoryPanel.HistoryRowColorsForTest(dark: true);
        (uint lightInactivePanel, uint lightInactiveSelection) = NativeGitHistoryPanel.HistoryRowColorsForTest(dark: false, hasFocus: false);
        (uint darkInactivePanel, uint darkInactiveSelection) = NativeGitHistoryPanel.HistoryRowColorsForTest(dark: true, hasFocus: false);

        Assert.AreEqual(Rgb(255, 255, 255), lightPanel);
        Assert.AreNotEqual(lightPanel, lightSelection);
        Assert.AreEqual(Rgb(30, 31, 34), darkPanel);
        Assert.AreNotEqual(darkPanel, darkSelection);
        Assert.AreEqual(lightPanel, lightInactivePanel);
        Assert.AreEqual(darkPanel, darkInactivePanel);
        Assert.AreEqual(Rgb(233, 234, 236), lightInactiveSelection);
        Assert.AreEqual(Rgb(67, 69, 74), darkInactiveSelection);
    }

    [TestMethod]
    public void 历史详情文件列表只在内容溢出时显示滚动条()
    {
        int rowHeight = NativeTheme.Scale(24);

        Assert.IsFalse(NativeGitHistoryPanel.ShouldShowFilesListScrollBarForTest(0, rowHeight * 4));
        Assert.IsFalse(NativeGitHistoryPanel.ShouldShowFilesListScrollBarForTest(4, rowHeight * 4));
        Assert.IsTrue(NativeGitHistoryPanel.ShouldShowFilesListScrollBarForTest(5, rowHeight * 4));
    }

    [TestMethod]
    public void 历史变化文件中英文目录都保留叶子文件()
    {
        IReadOnlyList<string> labels = NativeGitHistoryPanel.BuildFileTreeLabelsForTest(
        [
            new(GitChangeKind.Modified, "README.md", null),
            new(GitChangeKind.Added, "src/窗口/MainWindow.cs", null),
        ]);

        Assert.IsTrue(labels.SequenceEqual(
        [
            "2 个文件",
            "src 1 个文件",
            "窗口 1 个文件",
            "MainWindow.cs",
            "README.md",
        ]));
    }

    [TestMethod]
    public void 历史变化文件折叠目录只删除该目录叶子()
    {
        GitCommitChangedFile[] files =
        [
            new(GitChangeKind.Modified, "README.md", null),
            new(GitChangeKind.Modified, "docs/architecture.md", null),
            new(GitChangeKind.Added, "docs/product-spec.md", null),
        ];

        (bool canApply, int prefix, int removedCount, int addedCount) =
            NativeGitHistoryPanel.FileTreeDeltaForTest(
                files,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["docs"], StringComparer.OrdinalIgnoreCase));

        Assert.IsTrue(canApply);
        Assert.AreEqual((2, 2, 0), (prefix, removedCount, addedCount));
    }

    [TestMethod]
    public void Git历史保留视觉稿要求的左侧竖向工具栏宽度()
    {
        Assert.AreEqual(NativeTheme.Scale(38), NativeGitHistoryPanel.SideToolbarWidthForTest);
        Assert.IsGreaterThan(
            NativeTheme.Scale(1f),
            NativeGitHistoryPanel.SideToolbarIconStrokeWidthForTest);

        var tops = NativeGitHistoryPanel.CalculateSideToolbarButtonTopsForTest();
        Assert.AreEqual(NativeTheme.Scale(38) + NativeTheme.Scale(4), tops.Back);
        Assert.AreEqual(NativeTheme.Scale(38) + NativeTheme.Scale(49), tops.CreateReference);
        Assert.AreEqual(NativeTheme.Scale(30), tops.DeleteReference - tops.CreateReference);
        Assert.AreEqual(NativeTheme.Scale(30), tops.LocateHead - tops.Compare);
    }

    [TestMethod]
    public void Git历史三栏比例匹配视觉稿()
    {
        (int branchWidth, int logWidth, int detailsWidth) =
            NativeGitHistoryPanel.GetColumnWidthsForTest(NativeTheme.Scale(861));

        Assert.AreEqual(NativeTheme.Scale(230), branchWidth);
        Assert.AreEqual(NativeTheme.Scale(351), logWidth);
        Assert.AreEqual(NativeTheme.Scale(280), detailsWidth);
        Assert.AreEqual(NativeTheme.Scale(861), branchWidth + logWidth + detailsWidth);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DoNotParallelize]
    public void Git历史短工具栏为溢出入口保留完整命中区(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        foreach (int logicalHeight in new[] { 180, 190, 200, 230, 260, 305 })
        {
            int height = NativeTheme.Scale(logicalHeight);
            var layout = NativeGitHistoryPanel.CalculateSideToolbarLayout(height);
            if (logicalHeight == 305)
            {
                Assert.AreEqual(7, layout.VisibleCount);
                Assert.AreEqual(-1, layout.OverflowTop);
            }
            else
            {
                Assert.IsLessThan(7, layout.VisibleCount);
                Assert.IsGreaterThan(0, layout.VisibleCount);
                Assert.IsLessThanOrEqualTo(height - NativeTheme.Scale(4), layout.OverflowTop + NativeTheme.Scale(28));
            }
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Git历史即使只剩返回按钮也不把返回入口收入溢出菜单()
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(96);
        var tops = NativeGitHistoryPanel.CalculateSideToolbarButtonTopsForTest();
        // 取刚好能容纳返回按钮和溢出箭头的边界，验证箭头替换第二个动作。
        int height = tops.CreateReference + NativeTheme.Scale(28) + NativeTheme.Scale(4);

        var layout = NativeGitHistoryPanel.CalculateSideToolbarLayout(height);

        Assert.AreEqual(1, layout.VisibleCount);
        Assert.AreEqual(tops.CreateReference, layout.OverflowTop);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DoNotParallelize]
    public void Git历史极短工具栏不会绘制越界箭头(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        var tops = NativeGitHistoryPanel.CalculateSideToolbarButtonTopsForTest();
        for (int height = 0; height <= NativeTheme.Scale(305); height++)
        {
            var layout = NativeGitHistoryPanel.CalculateSideToolbarLayout(height);
            if (layout.VisibleCount > 0)
                Assert.IsLessThanOrEqualTo(height - NativeTheme.Scale(4), tops.Back + NativeTheme.Scale(28));
            if (layout.OverflowTop < 0) continue;
            Assert.IsGreaterThanOrEqualTo(tops.Back + NativeTheme.Scale(30), layout.OverflowTop);
            Assert.IsLessThanOrEqualTo(height - NativeTheme.Scale(4), layout.OverflowTop + NativeTheme.Scale(28));
        }
    }

    [TestMethod]
    public void Git历史紧凑三栏不会越过可用宽度()
    {
        int availableWidth = NativeTheme.Scale(623);

        (int branchWidth, int logWidth, int detailsWidth) =
            NativeGitHistoryPanel.GetColumnWidthsForTest(availableWidth);

        Assert.AreEqual(availableWidth, branchWidth + logWidth + detailsWidth);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(160), branchWidth);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(240), logWidth);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(190), detailsWidth);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DoNotParallelize]
    public void Git历史窄栏保留可读搜索框且收纳溢出的筛选项(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        foreach (int logicalWidth in new[] { 240, 244, 300, 351, 400, 500, 800 })
        {
            int width = NativeTheme.Scale(logicalWidth);
            NativeHistoryFilterLayout layout = NativeGitHistoryPanel.GetFilterControlLayoutForTest(width, NativeTheme.Scale(26));
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(110), layout.SearchWidth, "搜索框不能缩成不可输入的细条");
            Assert.IsLessThanOrEqualTo(width, layout.UtilitiesLeft + NativeTheme.Scale(60 + 8));
            Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(26 + 24), layout.ButtonWidth);
            int filtersRight = NativeTheme.Scale(8) + layout.SearchWidth + layout.Gap
                + layout.VisibleFilterCount * (layout.ButtonWidth + layout.Gap);
            if (layout.VisibleFilterCount < 4)
            {
                Assert.AreEqual(filtersRight, layout.OverflowLeft);
                filtersRight += NativeTheme.Scale(28) + layout.Gap;
            }
            Assert.IsLessThanOrEqualTo(layout.UtilitiesLeft, filtersRight);
            if (logicalWidth == 240) Assert.AreEqual(0, layout.VisibleFilterCount);
            if (logicalWidth >= 500) Assert.AreEqual(4, layout.VisibleFilterCount);
        }
    }

    [TestMethod]
    public void Git历史最小窗口保留作者和短日期并优先收起引用()
    {
        int rowWidth = NativeTheme.Scale(250);
        NativeHistoryRowTextLayout layout = NativeGitHistoryPanel.GetHistoryRowTextLayoutForTest(
            rowWidth, NativeTheme.Scale(128), NativeTheme.Scale(40), NativeTheme.Scale(110), NativeTheme.Scale(32));
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(120), layout.SubjectWidth);
        Assert.AreEqual(0, layout.ReferenceWidth);
        Assert.AreEqual(NativeTheme.Scale(40), layout.AuthorWidth);
        Assert.AreEqual(NativeTheme.Scale(32), layout.DateWidth);
        Assert.IsTrue(layout.CompactDate);
        Assert.IsLessThanOrEqualTo(rowWidth,
            NativeTheme.Scale(29 + 8 + 8 + 8) + layout.SubjectWidth + layout.AuthorWidth + layout.DateWidth);
    }

    [TestMethod]
    public void Git历史长引用不能挤掉作者和完整日期()
    {
        int rowWidth = NativeTheme.Scale(500);
        NativeHistoryRowTextLayout layout = NativeGitHistoryPanel.GetHistoryRowTextLayoutForTest(
            rowWidth, NativeTheme.Scale(800), NativeTheme.Scale(42), NativeTheme.Scale(110), NativeTheme.Scale(32));
        Assert.IsFalse(layout.CompactDate);
        Assert.AreEqual(NativeTheme.Scale(42), layout.AuthorWidth);
        Assert.AreEqual(NativeTheme.Scale(110), layout.DateWidth);
        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(120), layout.SubjectWidth);
        Assert.IsLessThan(NativeTheme.Scale(800), layout.ReferenceWidth);
        Assert.AreEqual(rowWidth,
            NativeTheme.Scale(29 + 8 + 8 * 3) + layout.SubjectWidth + layout.ReferenceWidth + layout.AuthorWidth + layout.DateWidth);
    }

    [TestMethod]
    public void Git历史短元数据将多余空间留给提交标题()
    {
        NativeHistoryRowTextLayout shortMetadata = NativeGitHistoryPanel.GetHistoryRowTextLayoutForTest(500, 0, 20, 110, 32);
        NativeHistoryRowTextLayout longMetadata = NativeGitHistoryPanel.GetHistoryRowTextLayoutForTest(500, 128, 96, 110, 32);
        Assert.IsGreaterThan(longMetadata.SubjectWidth, shortMetadata.SubjectWidth);
        Assert.AreEqual(20, shortMetadata.AuthorWidth);
        Assert.AreEqual(0, shortMetadata.ReferenceWidth);
    }

    [TestMethod]
    public void Git历史元数据包含同一提交的全部引用()
    {
        GitHistoryEntry entry = new(
            "*",
            "commit",
            "commit",
            [],
            "作者",
            "author@example.com",
            DateTimeOffset.UnixEpoch,
            "标题",
            [
                new(GitReferenceKind.LocalBranch, "main", IsHead: true),
                new(GitReferenceKind.Tag, "v1.0"),
            ]);

        StringAssert.Contains(NativeGitHistoryPanel.FormatHistoryMetaForTest(entry), "main · v1.0");
    }

    [TestMethod]
    public void 文件历史列按实际字宽扩展并为标题保留最小宽度()
    {
        int viewport = NativeTheme.Scale(240);
        int extent = NativeGitHistoryPanel.CalculateFileHistoryRowExtent(
            viewport,
            NativeTheme.Scale(180),
            NativeTheme.Scale(120));

        Assert.AreEqual(
            NativeTheme.Scale(440),
            extent,
            "作者、日期和标题列必须共同决定文件历史的横向内容范围。");
        Assert.AreEqual(
            extent,
            NativeGitHistoryPanel.CalculateFileHistoryRowExtent(extent, 0, 0),
            "视口已经足够宽时不能因重新度量把已有横向范围缩短。");
    }

    [TestMethod]
    public void Git历史筛选入口只在对应筛选生效时高亮()
    {
        Assert.IsTrue(NativeGitHistoryPanel.IsFilterKindActiveForTest(30, 2));
        Assert.IsFalse(NativeGitHistoryPanel.IsFilterKindActiveForTest(30, 3));
        Assert.IsTrue(NativeGitHistoryPanel.IsFilterKindActiveForTest(31, 4));
        Assert.IsTrue(NativeGitHistoryPanel.IsFilterKindActiveForTest(32, 6));
        Assert.IsFalse(NativeGitHistoryPanel.IsFilterKindActiveForTest(28, 5));
    }

    [TestMethod]
    public void Git历史右栏按变化文件操作栏和提交详情自上而下排列()
    {
        int height = NativeTheme.Scale(305);

        (int filesTop, int filesHeight, int actionsTop, int detailsTop, int detailsHeight) =
            NativeGitHistoryPanel.CalculateDetailsLayoutForTest(height);

        Assert.IsGreaterThanOrEqualTo(NativeTheme.Scale(26), filesHeight);
        Assert.AreEqual(filesTop + filesHeight, actionsTop);
        Assert.AreEqual(actionsTop + NativeTheme.Scale(24), detailsTop);
        Assert.AreEqual(height - NativeTheme.Scale(1), detailsTop + detailsHeight);
        Assert.IsGreaterThan(0, detailsHeight);
    }

    [TestMethod]
    public void Git历史变化文件按目录分组并保留叶子文件行()
    {
        IReadOnlyList<string> labels = NativeGitHistoryPanel.BuildFileTreeLabelsForTest(
        [
            new(GitChangeKind.Modified, "docs/architecture.md", null),
            new(GitChangeKind.Modified, "docs/roadmap.md", null),
            new(GitChangeKind.Added, "src/Augit/App.cs", null),
            new(GitChangeKind.Renamed, "README.md", "README.old.md"),
        ]);

        List<string> expected =
        [
            "4 个文件",
            "docs 2 个文件",
            "architecture.md",
            "roadmap.md",
            "src 1 个文件",
            "Augit 1 个文件",
            "App.cs",
            "README.md ← README.old.md",
        ];

        CollectionAssert.AreEqual(expected, labels.ToList());
    }

    [TestMethod]
    public void Git历史空变化文件仍保留根层级空态()
    {
        List<string> expected = ["0 个文件"];

        CollectionAssert.AreEqual(
            expected,
            NativeGitHistoryPanel.BuildFileTreeLabelsForTest([]).ToList());
    }

    [TestMethod]
    public void Git历史紧凑右栏移除额外操作行并把空间留给提交详情()
    {
        int height = NativeTheme.Scale(264);

        (int filesTop, int filesHeight, int actionsTop, int detailsTop, int detailsHeight) =
            NativeGitHistoryPanel.CalculateCompactDetailsLayoutForTest(height);

        Assert.AreEqual(filesTop + filesHeight, actionsTop);
        Assert.AreEqual(actionsTop, detailsTop);
        Assert.AreEqual(height - NativeTheme.Scale(1), detailsTop + detailsHeight);
    }

    [TestMethod]
    public void Git历史提交详情按标题元数据引用正文分层()
    {
        int top = NativeTheme.Scale(8);
        int bottom = NativeTheme.Scale(120);

        var layout = NativeGitHistoryPanel.CalculateCommitDetailsTextLayoutForTest(
            top,
            bottom,
            hasReferences: true);

        Assert.AreEqual(top, layout.TitleTop);
        Assert.AreEqual(top + NativeTheme.Scale(40), layout.MetaTop);
        Assert.AreEqual(layout.MetaTop + NativeTheme.Scale(20), layout.ReferencesTop);
        Assert.AreEqual(layout.ReferencesTop + NativeTheme.Scale(28), layout.BodyTop);
        Assert.AreEqual(bottom - layout.BodyTop, layout.BodyHeight);
        Assert.IsGreaterThan(0, layout.BodyHeight);
    }

    [TestMethod]
    public void Git历史无引用时正文紧跟元数据()
    {
        int top = NativeTheme.Scale(8);
        int bottom = NativeTheme.Scale(120);

        var layout = NativeGitHistoryPanel.CalculateCommitDetailsTextLayoutForTest(
            top,
            bottom,
            hasReferences: false);

        Assert.AreEqual(layout.ReferencesTop + NativeTheme.Scale(8), layout.BodyTop);
        Assert.AreEqual(bottom - layout.BodyTop, layout.BodyHeight);
    }

    [TestMethod]
    public void Git历史右键菜单覆盖视觉稿中的提交动作()
    {
        List<string> expected =
        [
            "复制提交哈希",
            "Cherry-pick",
            "与工作区比较",
            "Reset 当前分支到此处…",
            "Revert Commit",
            "新建分支…",
            "新建标签…",
        ];

        CollectionAssert.AreEqual(expected, NativeGitHistoryPanel.HistoryContextMenuLabelsForTest.ToArray());
    }

    private static uint Rgb(byte red, byte green, byte blue)
    {
        return (uint)(red | green << 8 | blue << 16);
    }
}
