namespace Augit.App.Tests;

using Augit.Core.Documents;
using Augit.Core.Git;

[TestClass]
public sealed class NativeDocumentViewTests
{
    [TestMethod]
    public async Task 重新加载文件后切回标签使用最新读取状态()
    {
        using TemporaryDirectory temporary = new();
        string path = temporary.GetPath("sample.json");
        DocumentReadResult invalid = new(DocumentReadStatus.TextReady, path, path,
            new(DocumentKind.Json, "JSON"), 1, "{", null, null, string.Empty);
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300,
            0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using NativeDocumentView view = new(owner, Path.GetDirectoryName(path)!, invalid, new(), _ => { }, (_, _, _) => { });
            Assert.AreEqual(UiText.JsonError(1, 2), view.ReadStatusText);
            await view.ReloadAsync(invalid with { Text = "{}", FileSize = 2 });
            Assert.AreEqual(DocumentReadStatus.TextReady, view.Status);
            Assert.AreEqual("JSON", view.ReadStatusText, "修复后的文件不能继续报告旧的 JSON 错误。");
            await view.ReloadAsync(invalid with
            {
                Status = DocumentReadStatus.Missing,
                Text = null,
                Message = "文件已被删除、重命名或替换。",
            });
            Assert.AreEqual(DocumentReadStatus.Missing, view.Status);
            Assert.AreEqual("文件已被删除、重命名或替换。", view.ReadStatusText);
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
        }
    }

    [TestMethod]
    public void 原生文本使用DirectWrite且保留像素字号和只读状态()
    {
        nint owner = NativeMethods.CreateWindow(
            0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300,
            0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using ScintillaControl editor = new(owner, 1);
            editor.ApplyAppearance("Cascadia Mono", 13, false);
            editor.SetTextContent("只读正文 ABC 123");
            Assert.AreEqual((nint)1, NativeMethods.SendMessage(editor.Handle, 2631, 0, 0));
            Assert.AreEqual((nint)975, NativeMethods.SendMessage(editor.Handle, 2062, 32, 0));
            Assert.IsTrue(editor.IsReadOnly);
            Assert.AreEqual("只读正文 ABC 123", editor.GetTextContent());
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
        }
    }

    [TestMethod]
    public void 文档视图只在边界真实变化时应用SetBounds()
    {
        (int X, int Y, int Width, int Height) current = (10, 20, 800, 600);

        Assert.IsTrue(NativeDocumentView.ShouldApplyBoundsForTest(false, default, current));
        Assert.IsFalse(NativeDocumentView.ShouldApplyBoundsForTest(true, current, current));
        Assert.IsTrue(NativeDocumentView.ShouldApplyBoundsForTest(true, current, (11, 20, 800, 600)));
        Assert.IsTrue(NativeDocumentView.ShouldApplyBoundsForTest(true, current, (10, 20, 801, 600)));
    }

    [TestMethod]
    public void 图片适应区域保留三十二像素内边距且不放大小图()
    {
        int viewWidth = NativeTheme.Scale(1000);
        int viewHeight = NativeTheme.Scale(700);
        double scale = NativeImageView.CalculateFitScaleForTest(viewWidth, viewHeight, 1920, 1200);
        double expected = Math.Min(
            (viewWidth - NativeTheme.Scale(64)) / 1920d,
            (viewHeight - NativeTheme.Scale(64)) / 1200d);

        Assert.AreEqual(expected, scale, 0.0001d);
        Assert.AreEqual(1d, NativeImageView.CalculateFitScaleForTest(viewWidth, viewHeight, 320, 200));
    }

    [TestMethod]
    public void 图片缩放使用稳定步进并限制在十到八百百分比()
    {
        Assert.AreEqual(0.75d, NativeImageView.CalculateNextZoomForTest(0.63d, zoomIn: true));
        Assert.AreEqual(0.50d, NativeImageView.CalculateNextZoomForTest(0.63d, zoomIn: false));
        Assert.AreEqual(8d, NativeImageView.CalculateNextZoomForTest(8d, zoomIn: true));
        Assert.AreEqual(0.10d, NativeImageView.CalculateNextZoomForTest(0.10d, zoomIn: false));
    }

    [TestMethod]
    public void 图片按当前比例在预览区域中央绘制()
    {
        (int x, int y, int width, int height) = NativeImageView.CalculateImageBoundsForTest(
            1200,
            800,
            640,
            360,
            1.25d);

        Assert.AreEqual((200, 175, 800, 450), (x, y, width, height));
    }

    [TestMethod]
    public void 不可预览信息页按视觉稿保持居中层级和单一动作位置()
    {
        int width = NativeTheme.Scale(1200);
        int height = NativeTheme.Scale(720);
        NativeDocumentInfoLayout layout = NativeDocumentInfoView.CalculateLayoutForTest(width, height);

        Assert.AreEqual(width / 2, (layout.Icon.Left + layout.Icon.Right) / 2);
        Assert.AreEqual(width / 2, (layout.Button.Left + layout.Button.Right) / 2);
        Assert.IsLessThanOrEqualTo(layout.Button.Top, layout.Message.Bottom);
        Assert.IsLessThanOrEqualTo(height, layout.Button.Bottom);
        Assert.IsGreaterThan(0, layout.Title.Right - layout.Title.Left);
    }

    [TestMethod]
    [DoNotParallelize]
    public void 不可预览信息页大字号长路径按内容扩展且不覆盖主要动作()
    {
        string family = NativeTheme.UiFontFamilyForTest;
        double size = NativeTheme.UiFontSizeForTest;
        try
        {
            NativeTheme.ConfigureUiTypography("Microsoft YaHei UI", 40);
            int width = NativeTheme.Scale(1024);
            int height = NativeTheme.Scale(640);
            NativeDocumentInfoLayout layout = NativeDocumentInfoView.CalculateLayoutForTest(
                width,
                height,
                new string('C', 180),
                "这是一个很长的不可预览原因说明，用于验证大字号下路径和说明能够换行，并且不会覆盖主要动作。 ");

            Assert.IsLessThanOrEqualTo(layout.Message.Top, layout.Path.Bottom);
            Assert.IsLessThanOrEqualTo(layout.Button.Top, layout.Message.Bottom);
            Assert.IsLessThanOrEqualTo(height, layout.Button.Bottom);
            Assert.IsGreaterThan(NativeTheme.Scale(38), layout.Path.Bottom - layout.Path.Top);
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(family, size);
        }
    }

    [TestMethod]
    public void Markdown对照模式按视觉稿使用等宽两栏()
    {
        int width = NativeTheme.Scale(1440);

        int sourceWidth = NativeDocumentView.CalculateMarkdownSplitSourceWidthForTest(width);

        int availableWidth = width - NativeDocumentView.MarkdownSplitterWidthForTest;
        Assert.AreEqual((int)Math.Round(availableWidth * 0.50d), sourceWidth);
        Assert.AreEqual(availableWidth - sourceWidth, sourceWidth);
    }

    [TestMethod]
    public void Markdown对照分隔位置受两侧最小宽度约束()
    {
        int width = NativeTheme.Scale(1000);

        int left = NativeDocumentView.CalculateMarkdownSplitSourceWidthForTest(width, 0.05d);
        int right = NativeDocumentView.CalculateMarkdownSplitSourceWidthForTest(width, 0.95d);

        Assert.AreEqual(NativeTheme.Scale(240), left);
        Assert.AreEqual(width - NativeDocumentView.MarkdownSplitterWidthForTest - NativeTheme.Scale(240), right);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    [DoNotParallelize]
    public void 文档模式工具栏采用PyCharm紧凑步长且不累计Dpi舍入误差(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        Assert.AreEqual(NativeTheme.Scale(36), NativeDocumentView.ToolbarHeightForTest);
        Assert.AreEqual(
            NativeTheme.Scale(26) * 3,
            NativeDocumentView.ModeSegmentWidthForTest(3));
        Assert.AreEqual(
            NativeTheme.Scale(26) * 2,
            NativeDocumentView.ModeSegmentWidthForTest(2));
    }

    [TestMethod]
    public void JSON格式化按钮使用结构化图标且Markdown仍使用预览图标()
    {
        Assert.AreEqual(
            NativeDocumentModeIcon.Formatted,
            NativeDocumentView.ResolveDocumentModeIconForTest(DocumentKind.Json, alternativeButton: true));
        Assert.AreEqual(
            NativeDocumentModeIcon.Preview,
            NativeDocumentView.ResolveDocumentModeIconForTest(DocumentKind.Markdown, alternativeButton: true));
        Assert.AreEqual(
            NativeDocumentModeIcon.Source,
            NativeDocumentView.ResolveDocumentModeIconForTest(DocumentKind.Json, alternativeButton: false));
    }

    [TestMethod]
    public void 当前文件查找条固定在正文顶部并占满宽度()
    {
        int width = NativeTheme.Scale(900);
        int contentTop = NativeDocumentView.ToolbarHeightForTest;

        (int x, int y, int overlayWidth, int height, int editWidth, int statusWidth) =
            NativeDocumentView.CalculateFindOverlayLayoutForTest(width, contentTop);

        Assert.AreEqual(width, overlayWidth);
        Assert.AreEqual(0, x);
        Assert.AreEqual(contentTop, y);
        Assert.AreEqual(NativeDocumentView.FindOverlayHeightForTest, height);
        Assert.AreEqual(width - NativeTheme.Scale(7) * 2 - NativeTheme.Scale(28) * 6
            - NativeTheme.Scale(3) * 7 - statusWidth, editWidth);
        Assert.AreEqual(NativeTheme.Scale(48), statusWidth);
    }

    [TestMethod]
    public void 当前文件查找条在窄正文中优先收缩输入框和结果区()
    {
        int width = NativeTheme.Scale(350);

        (int x, _, int overlayWidth, _, int editWidth, int statusWidth) =
            NativeDocumentView.CalculateFindOverlayLayoutForTest(
                width,
                NativeDocumentView.ToolbarHeightForTest);

        Assert.AreEqual(0, x);
        Assert.AreEqual(width, overlayWidth);
        Assert.AreEqual(width - NativeTheme.Scale(7) * 2 - NativeTheme.Scale(28) * 6
            - NativeTheme.Scale(3) * 7 - statusWidth, editWidth);
        Assert.AreEqual(NativeTheme.Scale(48), statusWidth);
    }

    [TestMethod]
    public void 当前文件查找条在极窄正文中同步收缩按钮避免越界()
    {
        int normalWidth = NativeTheme.Scale(900);
        int narrowWidth = NativeTheme.Scale(180);
        int veryNarrowWidth = NativeTheme.Scale(140);

        Assert.AreEqual(NativeTheme.Scale(28),
            NativeDocumentView.FindOverlayButtonSizeForTest(normalWidth));
        Assert.IsLessThan(
            NativeDocumentView.FindOverlayButtonSizeForTest(normalWidth),
            NativeDocumentView.FindOverlayButtonSizeForTest(narrowWidth));
        Assert.IsGreaterThanOrEqualTo(
            NativeTheme.Scale(16),
            NativeDocumentView.FindOverlayButtonSizeForTest(veryNarrowWidth));

        (int _, int _, int overlayWidth, int _, int editWidth, int statusWidth) =
            NativeDocumentView.CalculateFindOverlayLayoutForTest(
                veryNarrowWidth,
                NativeDocumentView.ToolbarHeightForTest);
        Assert.AreEqual(veryNarrowWidth, overlayWidth);
        Assert.IsGreaterThanOrEqualTo(0, editWidth);
        Assert.IsGreaterThanOrEqualTo(0, statusWidth);
    }

    [TestMethod]
    public void Markdown模式按钮在深色主题下显式使用主题底色()
    {
        (uint unselectedBackground, uint unselectedIcon) =
            NativeDocumentView.ModeToolbarButtonColorsForTest(
                dark: true,
                selected: false,
                disabled: false,
                pressed: false);
        (uint selectedBackground, uint selectedIcon) =
            NativeDocumentView.ModeToolbarButtonColorsForTest(
                dark: true,
                selected: true,
                disabled: false,
                pressed: false);
        (uint disabledBackground, uint disabledIcon) =
            NativeDocumentView.ModeToolbarButtonColorsForTest(
                dark: true,
                selected: false,
                disabled: true,
                pressed: false);

        Assert.AreEqual(NativeTheme.Palette(dark: true).Panel, unselectedBackground);
        Assert.AreEqual(NativeTheme.DocumentModeIconColor(dark: true), unselectedIcon);
        Assert.AreEqual(NativeTheme.DocumentModeSelectedBackground(dark: true), selectedBackground);
        Assert.AreEqual(NativeTheme.DocumentModeIconColor(dark: true), selectedIcon);
        Assert.AreEqual(NativeTheme.Palette(dark: true).Faint, disabledIcon);
        Assert.AreEqual(unselectedBackground, disabledBackground);
    }

    [TestMethod]
    public async Task 模式分段背景由父窗口绘制且不再创建重叠子窗口()
    {
        string sourcePath = FindRepositoryFile("src", "Augit.App", "NativeDocumentView.cs");
        string source = await File.ReadAllTextAsync(sourcePath);
        Assert.DoesNotContain("_modeSegment = CreateChild(", source);
        Assert.DoesNotContain("ModeSegmentIdentifier", source);
        StringAssert.Contains(
            source,
            "DrawModeSegmentBackground(deviceContext, _modeSegmentRectangle);");

        int layoutStart = source.IndexOf("private void Layout()", StringComparison.Ordinal);
        int layoutEnd = source.IndexOf(
            "private bool IsMarkdownSplitMode()",
            layoutStart,
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, layoutStart);
        Assert.IsGreaterThan(layoutStart, layoutEnd);

        string layout = source[layoutStart..layoutEnd];
        StringAssert.Contains(layout, "_modeSegmentRectangle = new NativeMethods.Rectangle");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 行号栏随等宽字号和行数更新且不受界面字号影响(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        nint owner = NativeMethods.CreateWindow(0, NativeMethods.StaticClass, string.Empty,
            NativeMethods.WindowStylePopup, 0, 0, 400, 300, 0, 0, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            using ScintillaControl editor = new(owner, 1);
            editor.ApplyAppearance("Consolas", 13, false);
            editor.SetTextContent("只读 ABC 123");
            int Width(nuint margin = 0) => checked((int)NativeMethods.SendMessage(editor.Handle, 2243, margin, 0));
            int initial = Width();
            NativeTheme.ConfigureUiTypography(previousFamily, 30);
            editor.ApplyAppearance("Consolas", 13, false);
            Assert.AreEqual(initial, Width(), "界面字号不能改变行号栏");
            editor.ApplyAppearance("Consolas", 26, false);
            Assert.IsGreaterThan(initial, Width(), "等宽字号生效时必须立即重新度量行号");
            int larger = Width();
            editor.SetTextContent(new string('\n', 9999));
            Assert.IsGreaterThan(larger, Width(), "五位行号必须完整显示");
            int longDocument = Width();
            editor.SetBlame([]);
            Assert.AreEqual(longDocument, Width(1));
            editor.SetLineNumbersVisible(false);
            editor.ApplyAppearance("Consolas", 13, true);
            Assert.AreEqual(0, Width(1), "修改字体不能重新显示冲突等场景隐藏的行号");
            editor.SetLineNumbersVisible(true);
            Assert.IsGreaterThan(initial, Width(1));
            Assert.IsLessThan(longDocument, Width(1));
            Assert.IsTrue(editor.IsReadOnly);
        }
        finally
        {
            _ = NativeMethods.DestroyWindow(owner);
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    public void 文本字号按像素语义换算为Scintilla磅值()
    {
        Assert.AreEqual(1050, ScintillaControl.ConvertPixelsToFontHundredthsForTest(14));
        Assert.AreEqual(675, ScintillaControl.ConvertPixelsToFontHundredthsForTest(9));
        Assert.AreEqual(3000, ScintillaControl.ConvertPixelsToFontHundredthsForTest(40));
        Assert.AreEqual(
            600,
            ScintillaControl.ConvertPixelsToFontHundredthsForTest(14, 96d / 168d));
    }

    [TestMethod]
    public void WebView2缺失提示使用微软Https下载入口()
    {
        Uri uri = RuntimeDependencyPrompt.WebView2DownloadUri;

        Assert.AreEqual(Uri.UriSchemeHttps, uri.Scheme);
        Assert.AreEqual("developer.microsoft.com", uri.Host);
    }

    [TestMethod]
    [DataRow("²cat", "cat", true, 0)]
    [DataRow("Ⅳcat", "cat", true, 0)]
    [DataRow("cat²", "cat", true, 0)]
    [DataRow("cat_", "cat", true, 0)]
    [DataRow("cat.", "cat", true, 1)]
    [DataRow("K", "k", false, 0)]
    [DataRow("ſ", "s", false, 0)]
    [DataRow("a- - -", "- -", true, 1)]
    [DataRow("a- - -b", "- -", true, 0)]
    [DataRow("é É", "é", false, 2)]
    public void 普通文本数量和双向定位使用一致的大小写及全字规则(string source, string query, bool wholeWord, int count)
    {
        Assert.AreEqual(count, NativeDocumentView.CountFindMatches(source, query, false, wholeWord, false));
        foreach (bool backwards in new[] { false, true })
        {
            var match = NativeDocumentView.FindMatch(source, query, backwards ? source.Length : 0,
                backwards, false, wholeWord, false);
            Assert.AreEqual(count != 0, match.HasValue);
        }
    }

    [TestMethod]
    public void 普通文本双向查找从当前边界继续并支持取消()
    {
        const string source = "cat xx cat yy cat";
        Assert.AreEqual((7, 3), NativeDocumentView.FindMatch(source, "cat", 3, false, true, false, false));
        Assert.AreEqual((0, 3), NativeDocumentView.FindMatch(source, "cat", 7, true, true, false, false));
        Assert.AreEqual((14, 3), NativeDocumentView.FindMatch(source, "cat", 0, true, true, false, false));
        Assert.ThrowsExactly<OperationCanceledException>(() => NativeDocumentView.FindMatch(source, "cat", 0,
            false, false, false, false, new CancellationToken(canceled: true)));
    }

    [TestMethod]
    public void 全字拒绝的重叠候选不遮挡有效结果()
    {
        foreach (bool backwards in new[] { false, true })
            Assert.AreEqual((3, 3), NativeDocumentView.FindMatch("a- - -", "- -", backwards ? 6 : 0,
                backwards, true, true, false));
    }

    [TestMethod]
    public void 当前文件查找支持全字匹配和循环定位()
    {
        (int Start, int Length)? first = NativeDocumentView.FindMatch(
            "cat scatter cat",
            "cat",
            0,
            backwards: false,
            matchCase: true,
            wholeWord: true,
            regularExpression: false);
        (int Start, int Length)? wrapped = NativeDocumentView.FindMatch(
            "cat scatter cat",
            "cat",
            15,
            backwards: false,
            matchCase: true,
            wholeWord: true,
            regularExpression: false);

        Assert.AreEqual((0, 3), first);
        Assert.AreEqual((0, 3), wrapped);
    }

    [TestMethod]
    public void 当前文件查找支持反向正则表达式()
    {
        (int Start, int Length)? match = NativeDocumentView.FindMatch(
            "a1 a2 a3",
            "a\\d",
            8,
            backwards: true,
            matchCase: true,
            wholeWord: false,
            regularExpression: true);

        Assert.AreEqual((6, 2), match);
    }

    [TestMethod]
    public void 当前文件查找结果数量遵循大小写全字和正则开关()
    {
        const string source = "Git git GitHub Git";

        Assert.AreEqual(4, NativeDocumentView.CountFindMatches(
            source,
            "Git",
            matchCase: false,
            wholeWord: false,
            regularExpression: false));
        Assert.AreEqual(3, NativeDocumentView.CountFindMatches(
            source,
            "Git",
            matchCase: false,
            wholeWord: true,
            regularExpression: false));
        Assert.AreEqual(2, NativeDocumentView.CountFindMatches(
            source,
            "Git",
            matchCase: true,
            wholeWord: true,
            regularExpression: false));
        Assert.AreEqual(4, NativeDocumentView.CountFindMatches(
            source,
            "G.t",
            matchCase: false,
            wholeWord: false,
            regularExpression: true));
    }

    [TestMethod]
    public void Markdown预览只允许初始页和About内部导航()
    {
        Assert.IsTrue(MarkdownWebViewHost.ShouldAllowNavigation("about:blank", initialNavigation: true));
        Assert.IsTrue(MarkdownWebViewHost.ShouldAllowNavigation("about:blank#标题", initialNavigation: false));
        Assert.IsFalse(MarkdownWebViewHost.ShouldAllowNavigation("https://example.com", initialNavigation: false));
        Assert.IsFalse(MarkdownWebViewHost.ShouldAllowNavigation("file:///C:/outside.txt", initialNavigation: false));
    }

    [TestMethod]
    public void 三栏冲突解决器使用宽版模态布局()
    {
        (int width, int height, int header, int footer) =
            NativeConflictResolverDialog.LogicalLayoutForTest;
        (int contentHeader, int columnHeader, int contentFooter) =
            NativeConflictResolverDialog.LogicalContentLayoutForTest;
        (int left, int middle, int right) =
            NativeConflictResolverDialog.CalculateColumnWidthsForTest(1000);

        Assert.AreEqual((1040, 467, 45, 53), (width, height, header, footer));
        Assert.AreEqual((42, 36, 48), (contentHeader, columnHeader, contentFooter));
        Assert.IsLessThanOrEqualTo(1, Math.Abs(left + NativeTheme.Scale(1) - right), "左右轨道宽度包含各自的分隔线，取整误差不超过一像素。");
        Assert.IsGreaterThan(left, middle);
        Assert.AreEqual(1000, left + middle + right);
        Assert.AreEqual(944, NativeConflictResolverDialog.CalculateDialogWidthForTest(1024));
        Assert.AreEqual(1040, NativeConflictResolverDialog.CalculateDialogWidthForTest(1180));
    }

    [TestMethod]
    public void 三栏冲突块使用各自文本中的真实行号对齐()
    {
        const string yours = """
            head
            left a
            left b
            middle
            second left
            tail
            """;
        const string result = """
            head
            <<<<<<< HEAD
            left a
            left b
            =======
            right a
            >>>>>>> feature
            middle
            <<<<<<< HEAD
            second left
            =======
            second right a
            second right b
            >>>>>>> feature
            tail
            """;
        const string theirs = """
            head
            right a
            middle
            second right a
            second right b
            tail
            """;

        string resultWithCrLf = result.Replace("\n", "\r\n", StringComparison.Ordinal);
        IReadOnlyList<(int YoursLine, int ResultLine, int TheirsLine)> blocks =
            NativeConflictResolverDialog.CalculateBlockLineAlignmentForTest(yours, resultWithCrLf, theirs);

        Assert.HasCount(2, blocks);
        Assert.AreEqual((1, 1, 1), blocks[0]);
        Assert.AreEqual((4, 8, 3), blocks[1]);
    }

    [TestMethod]
    public void 三栏冲突块CRLF匹配返回原文字符范围()
    {
        const string yours = "前\r\n左侧\r\n后\r\n";
        const string result = "前\r\n<<<<<<< HEAD\r\n左侧\r\n=======\r\n右侧\r\n>>>>>>> feature\r\n后\r\n";
        const string theirs = "前\r\n右侧\r\n后\r\n";

        IReadOnlyList<(int YoursStart, int YoursLength, int TheirsStart, int TheirsLength)> ranges =
            NativeConflictResolverDialog.CalculateBlockRangesForTest(yours, result, theirs);

        Assert.HasCount(1, ranges);
        Assert.AreEqual((3, 4, 3, 4), ranges[0]);
        Assert.AreEqual("左侧\r\n", yours.Substring(ranges[0].YoursStart, ranges[0].YoursLength));
        Assert.AreEqual("右侧\r\n", theirs.Substring(ranges[0].TheirsStart, ranges[0].TheirsLength));
    }

    [TestMethod]
    public void 三栏冲突解决器标题只显示文件名且编辑区隐藏行号()
    {
        Assert.AreEqual(
            "NativeGitPanel.cs",
            NativeConflictResolverDialog.DisplayFileNameForTest("src/Augit.App/NativeGitPanel.cs"));
        Assert.AreEqual(
            "NativeGitPanel.cs",
            NativeConflictResolverDialog.DisplayFileNameForTest("src\\Augit.App\\NativeGitPanel.cs"));
    }

    [TestMethod]
    public void 冲突背景范围覆盖完整文本行()
    {
        const string source = "header\nleft one\nleft two\ntail\n";

        List<int> lines = ScintillaControl.CalculateCoveredLinesForTest(
            source,
            [(7, 18)]);
        int[] expectedLines = [1, 2];

        CollectionAssert.AreEqual(expectedLines, lines);
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到测试所需的仓库文件：{Path.Combine(relativePath)}");
    }
}
