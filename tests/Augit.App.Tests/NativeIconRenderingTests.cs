using System.Runtime.InteropServices;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeIconRenderingTests
{
    [TestMethod]
    [DataRow(96, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(144, true)]
    public void 提交图按色批量绘制保持全部轨道虚线和当前提交环(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        uint background = NativeTheme.Palette(dark).AccentSoft;
        NativeCommitGraphSegment[] segments = Enumerable.Range(0, 12)
            .Select(index => new NativeCommitGraphSegment(index, 0, index, 1, index)).ToArray();
        NativeCommitGraphRow row = new(0, 0, true, [.. segments, new(11, 0.5f, 12, 0.92f, 1, true)]);
        NativeCommitGraph graph = new([row], 13);
        uint[,] batched = RenderColors((dc, rectangle) =>
        {
            graph.DrawRow(dc, rectangle, 0, dark, background);
            return true;
        }, background, 230, 27);
        uint[,] separate = RenderColors((dc, rectangle) =>
        {
            foreach (NativeCommitGraphSegment segment in row.Segments)
            {
                float x1 = NativeTheme.Scale(15 + segment.FromColumn * 16), x2 = NativeTheme.Scale(15 + segment.ToColumn * 16);
                NativeGdiPlusDrawing.StrokeLine[] lines = segment.Dashed
                    ? NativeCommitGraph.BuildDashedLinesForTest(segment, x1, x2, 0, rectangle.Bottom)
                    : [new(x1, segment.FromY * rectangle.Bottom, x2, segment.ToY * rectangle.Bottom)];
                Assert.IsTrue(NativeGdiPlusDrawing.StrokeShapes(dc, NativeTheme.GitGraphColor(segment.Color, dark),
                    NativeTheme.Scale(1.5f), lines, [], []));
            }
            NativeTheme.DrawCommitGraphNode(dc, NativeTheme.Scale(15), rectangle.Bottom / 2,
                NativeTheme.GitGraphColor(0, dark), background, true);
            return true;
        }, background, 230, 27);
        CollectionAssert.AreEqual(separate.Cast<uint>().ToArray(), batched.Cast<uint>().ToArray());
        for (int column = 0; column < 12; column++)
        {
            int x = NativeTheme.Scale(15 + column * 16), y = NativeTheme.Scale(6);
            Assert.AreNotEqual(background, batched[x, y], "每条轨道必须真实绘制；抗锯齿边缘允许与背景混色。");
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 查找开关图形不受字体或字号影响(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest;
        double size = NativeTheme.UiFontSizeForTest;
        try
        {
            foreach (NativeFindOptionIcon icon in Enum.GetValues<NativeFindOptionIcon>())
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                uint[,] original = RenderColors((dc, r) => NativeTheme.DrawFindOptionIcon(dc, r, icon, 0));
                NativeTheme.ConfigureUiTypography("Consolas", 40);
                uint[,] changed = RenderColors((dc, r) => NativeTheme.DrawFindOptionIcon(dc, r, icon, 0));
                CollectionAssert.AreEqual(original.Cast<uint>().ToArray(), changed.Cast<uint>().ToArray());
                Assert.IsGreaterThan(12, original.Cast<uint>().Count(pixel => pixel != 0x00FFFFFFu));
            }
        }
        finally { NativeTheme.ConfigureUiTypography(family, size); }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void Diff模式用空心圆角框区分单双栏且空白开关不再伪装复选框(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] unified = Render((dc, r) => NativeTheme.DrawDiffModeIcon(dc, r, NativeDiffModeIcon.Unified, 0));
        bool[,] split = Render((dc, r) => NativeTheme.DrawDiffModeIcon(dc, r, NativeDiffModeIcon.SideBySide, 0));
        bool[,] whitespace = Render((dc, r) => NativeTheme.DrawDiffModeIcon(dc, r, NativeDiffModeIcon.IgnoreWhitespace, 0));
        int center = unified.GetLength(0) / 2;
        for (int y = center - NativeTheme.Scale(3); y <= center + NativeTheme.Scale(3); y++)
        {
            for (int x = center - NativeTheme.Scale(3); x <= center + NativeTheme.Scale(3); x++)
            {
                Assert.IsFalse(unified[x, y], "单栏框内部不能添加文字横线或被 GDI 画刷填满。");
            }
            Assert.IsTrue(split[center, y] || split[center - 1, y], "双栏必须有贯穿中心的分隔线。");
        }
        Assert.IsFalse(split[center - NativeTheme.Scale(3), center], "左右正文区保持空心。");
        Assert.IsFalse(split[center + NativeTheme.Scale(3), center]);
        Assert.IsTrue(whitespace[center + NativeTheme.Scale(4), center + NativeTheme.Scale(3)],
            "空白符号保留右侧竖笔，不能退回复选框。");
        Assert.IsFalse(whitespace[center - NativeTheme.Scale(4), center + NativeTheme.Scale(4)],
            "空白符号左下留空，不能添加复选框的左边框。");
        foreach (bool[,] pixels in new[] { unified, split, whitespace })
        {
            Assert.IsGreaterThan(12, pixels.Cast<bool>().Count(pixel => pixel));
            for (int y = 0; y < pixels.GetLength(1); y++)
                for (int x = 0; x < pixels.GetLength(0); x++)
                    if (pixels[x, y])
                    {
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(7), Math.Abs(x - center));
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(7), Math.Abs(y - center));
                    }
        }
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void Diff模式图形不依赖字体和选入画刷且不改变设备上下文(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest;
        double size = NativeTheme.UiFontSizeForTest;
        NativeThemePalette palette = NativeTheme.Palette(dark);
        nint brush = NativeMethods.CreateSolidBrush(0x0000FF);
        try
        {
            foreach (NativeDiffModeIcon mode in Enum.GetValues<NativeDiffModeIcon>())
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                uint[,] original = RenderColors((dc, r) => NativeTheme.DrawDiffModeIcon(dc, r, mode, palette.Muted), palette.Panel);
                NativeTheme.ConfigureUiTypography("Consolas", 25);
                uint[,] changed = RenderColors((dc, r) =>
                {
                    nint previous = NativeMethods.SelectObject(dc, brush);
                    try
                    {
                        bool result = NativeTheme.DrawDiffModeIcon(dc, r, mode, palette.Muted);
                        Assert.AreEqual(brush, NativeMethods.SelectObject(dc, previous), "绘制不能改变调用方的 GDI 画刷。");
                        return result;
                    }
                    finally
                    {
                        _ = NativeMethods.SelectObject(dc, previous);
                    }
                }, palette.Panel);
                CollectionAssert.AreEqual(original.Cast<uint>().ToArray(), changed.Cast<uint>().ToArray());
                Assert.IsGreaterThan(12, original.Cast<uint>().Count(pixel => pixel != palette.Panel));
                Assert.IsFalse(original.Cast<uint>().Contains(0x0000FFu), "图标不能被当前画刷意外填充。");
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(family, size);
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 文档模式保留四行原文独立对照框和预览圆点且不随字体变化(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            foreach (NativeDocumentModeIcon mode in Enum.GetValues<NativeDocumentModeIcon>())
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                bool[,] original = Render((dc, r) => NativeTheme.DrawDocumentModeIcon(dc, r.Right / 2, r.Bottom / 2, mode, 0));
                NativeTheme.ConfigureUiTypography("Consolas", 25);
                bool[,] changed = Render((dc, r) => NativeTheme.DrawDocumentModeIcon(dc, r.Right / 2, r.Bottom / 2, mode, 0));
                CollectionAssert.AreEqual(original.Cast<bool>().ToArray(), changed.Cast<bool>().ToArray());
                Assert.AreEqual(mode switch
                {
                    NativeDocumentModeIcon.Source => 4,
                    NativeDocumentModeIcon.Split => 5,
                    _ => 2,
                }, CountComponents(original), "原文四行必须分开；对照框独立；预览圆点不能缺失或与山形相连。");
                int center = original.GetLength(0) / 2;
                for (int y = 0; y < original.GetLength(1); y++)
                    for (int x = 0; x < original.GetLength(0); x++)
                        if (original[x, y])
                        {
                            Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(x - center));
                            Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(y - center));
                        }
                if (mode == NativeDocumentModeIcon.Split)
                    Assert.IsFalse(original[center + NativeTheme.Scale(3), center], "右侧预览框不能使用实心补丁。");
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void JSON格式化图标使用独立花括号几何且不复用图片预览(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] formatted = Render((dc, rectangle) => NativeTheme.DrawDocumentModeIcon(
            dc,
            rectangle.Right / 2,
            rectangle.Bottom / 2,
            NativeDocumentModeIcon.Formatted,
            0));
        bool[,] preview = Render((dc, rectangle) => NativeTheme.DrawDocumentModeIcon(
            dc,
            rectangle.Right / 2,
            rectangle.Bottom / 2,
            NativeDocumentModeIcon.Preview,
            0));
        int center = formatted.GetLength(0) / 2;

        Assert.AreEqual(2, CountComponents(formatted), "两侧花括号必须是两个独立图形。");
        CollectionAssert.AreNotEqual(
            formatted.Cast<bool>().ToArray(),
            preview.Cast<bool>().ToArray(),
            "JSON 格式化不能复用图片预览图标。");
        Assert.IsFalse(formatted[center, center], "结构化图标中心必须留空。");
        bool leftStroke = false;
        bool rightStroke = false;
        for (int x = 0; x < center; x++)
        {
            leftStroke |= formatted[x, center];
        }

        for (int x = center + 1; x < formatted.GetLength(0); x++)
        {
            rightStroke |= formatted[x, center];
        }

        Assert.IsTrue(leftStroke, "左花括号必须可见。");
        Assert.IsTrue(rightStroke, "右花括号必须可见。");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 文件历史设置的真实命令绘制与备用入口均使用统一齿轮(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        int command = (int)typeof(NativeGitHistoryPanel).GetField("CommandFileHistorySettings", flags)!.GetRawConstantValue()!;
        bool[,] gear = Render((dc, r) => NativeTheme.DrawSettingsIcon(dc, r, 0));
        foreach (string methodName in new[] { "DrawHeaderIcon", "DrawHeaderIconFallback" })
        {
            System.Reflection.MethodInfo method = typeof(NativeGitHistoryPanel).GetMethod(methodName, flags)!;
            bool[,] actual = Render((dc, r) =>
            {
                object? result = method.Invoke(null, [dc, command, r.Right / 2, r.Bottom / 2, 0u]);
                return result is null or true;
            });
            CollectionAssert.AreEqual(gear.Cast<bool>().ToArray(), actual.Cast<bool>().ToArray(), "设置不能画成同心圆或准星。");
        }
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void 目录和引用图形保持主题填充圆孔及真实选中背景(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        int center = NativeTheme.Scale(16);
        foreach (uint background in new[] { palette.Panel, palette.AccentSoft, palette.SelectionInactive, palette.HistorySelectionInactive })
        {
            uint[,] folder = RenderColors((dc, r) => NativeTheme.DrawFolderIcon(dc, r, dark, false, background), background);
            uint[,] root = RenderColors((dc, r) => NativeTheme.DrawFolderIcon(dc, r, dark, true, background), background);
            uint[,] branch = RenderColors((dc, r) => NativeTheme.DrawGitReferenceIcon(dc, r, dark, background, true), background);
            uint[,] reference = RenderColors((dc, r) => NativeTheme.DrawGitReferenceIcon(dc, r, dark, background, false), background);

            Assert.AreEqual(NativeTheme.FolderIconColors(dark).Fill, folder[center, center]);
            Assert.AreEqual(folder[center - NativeTheme.Scale(3), center], root[center - NativeTheme.Scale(3), center], "角标不能重画或改变目录主体。");
            Assert.AreEqual(folder[center, center], folder[center, center + NativeTheme.Scale(2)], "展开目录不能多出前盖横线。");
            Assert.AreEqual(background, root[center + NativeTheme.Scale(8), center], "角标周围必须保留真实背景。");
            Assert.AreNotEqual(folder[center + NativeTheme.Scale(4), center + NativeTheme.Scale(4)],
                root[center + NativeTheme.Scale(4), center + NativeTheme.Scale(4)], "工作区根目录应具有独立角标。");

            Assert.AreEqual(NativeTheme.GitReferenceIconColor(dark), branch[center, center]);
            Assert.AreEqual(background, reference[center, center], "提交行引用使用细轮廓，不应成为分支树的实心标签。");
            int holeX = center + (int)Math.Round(NativeTheme.Scale(2.5f));
            int holeY = center - NativeTheme.Scale(3);
            Assert.AreEqual(background, branch[holeX, holeY], "圆孔不能使用默认黑笔或白色补丁。");
            Assert.AreEqual(background, branch[center - NativeTheme.Scale(6), center - NativeTheme.Scale(5)], "标签必须朝右上方倾斜。");
            Assert.AreEqual(background, branch[center + NativeTheme.Scale(6), center + NativeTheme.Scale(5)]);
            foreach (uint[,] pixels in new[] { folder, root, branch, reference })
            {
                for (int y = 0; y < pixels.GetLength(1); y++)
                    for (int x = 0; x < pixels.GetLength(0); x++)
                        if (Math.Abs(x - center) > NativeTheme.Scale(8) || Math.Abs(y - center) > NativeTheme.Scale(8))
                            foreach (int shift in new[] { 0, 8, 16 })
                                Assert.IsLessThanOrEqualTo(1, Math.Abs((int)((background >> shift) & 255) - (int)((pixels[x, y] >> shift) & 255)),
                                    "图形不能越出 16px 网格；背景自身的抗锯齿舍入至多为一个色阶。");
            }
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 复制图标的前页保留三条彼此分离的短文字线(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] copy = Render((dc, r) => NativeTheme.DrawMenuActionIcon(dc, NativeContextMenuIcon.Copy, r, 0));
        int center = copy.GetLength(0) / 2;
        int column = center - NativeTheme.Scale(2);
        int runs = 0;
        bool previous = false;
        for (int y = center - NativeTheme.Scale(2); y <= center + NativeTheme.Scale(5); y++)
        {
            bool current = copy[column, y];
            if (current && !previous) runs++;
            previous = current;
        }
        Assert.AreEqual(3, runs, "复制图标必须保留参考中的三条文字线，不能少画或粘连。");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 所有菜单图标与勾号不随命中区形状或界面字体拉伸(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        NativeMethods.Rectangle grid = new()
        {
            Left = NativeTheme.Scale(8),
            Top = NativeTheme.Scale(8),
            Right = NativeTheme.Scale(24),
            Bottom = NativeTheme.Scale(24),
        };
        NativeMethods.Rectangle menuRow = new()
        {
            Left = NativeTheme.Scale(6),
            Top = NativeTheme.Scale(1),
            Right = NativeTheme.Scale(26),
            Bottom = NativeTheme.Scale(31),
        };
        try
        {
            foreach (NativeContextMenuIcon icon in Enum.GetValues<NativeContextMenuIcon>())
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                bool[,] small = Render((dc, _) => NativeTheme.DrawMenuActionIcon(dc, icon, grid, 0));
                NativeTheme.ConfigureUiTypography("Consolas", 25);
                bool[,] tall = Render((dc, _) => NativeTheme.DrawMenuActionIcon(dc, icon, menuRow, 0));
                CollectionAssert.AreEqual(small.Cast<bool>().ToArray(), tall.Cast<bool>().ToArray(), icon.ToString());
                if (icon == NativeContextMenuIcon.None)
                {
                    Assert.IsFalse(tall.Cast<bool>().Any(pixel => pixel), "参考菜单的空图标列不能填充替代图形");
                    continue;
                }
                Assert.IsGreaterThan(4, tall.Cast<bool>().Count(pixel => pixel), $"{icon} 不得为空");
                int center = tall.GetLength(0) / 2;
                for (int y = 0; y < tall.GetLength(1); y++)
                {
                    for (int x = 0; x < tall.GetLength(0); x++)
                    {
                        if (!tall[x, y]) continue;
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(x - center), icon.ToString());
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(y - center), icon.ToString());
                    }
                }
            }
            NativeTheme.ConfigureUiTypography("Segoe UI", 13);
            bool[,] check = Render((dc, _) => NativeTheme.DrawMenuCheckmark(dc, grid, 0));
            NativeTheme.ConfigureUiTypography("Consolas", 25);
            bool[,] changedCheck = Render((dc, _) => NativeTheme.DrawMenuCheckmark(dc, menuRow, 0));
            CollectionAssert.AreEqual(check.Cast<bool>().ToArray(), changedCheck.Cast<bool>().ToArray());
            Assert.AreEqual(1, CountComponents(check), "勾号应是连续路径");
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 导航图标的方向和边界不随Dpi或界面字体变化(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            foreach (NativeNavigationIcon icon in Enum.GetValues<NativeNavigationIcon>())
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                bool[,] original = Render(icon);
                NativeTheme.ConfigureUiTypography("Consolas", 25);
                bool[,] changed = Render(icon);
                int size = original.GetLength(0);
                int center = size / 2;
                int top = 0, bottom = 0, total = 0;
                int middleX = 0, middleCount = 0, wingX = 0, wingCount = 0;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Assert.AreEqual(original[x, y], changed[x, y], $"{icon}: 字体改变了图标像素");
                        if (!original[x, y])
                        {
                            continue;
                        }
                        total++;
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(x - center));
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(y - center));
                        if (y < center - NativeTheme.Scale(1)) top++;
                        if (y > center + NativeTheme.Scale(1)) bottom++;
                        if (Math.Abs(y - center) <= NativeTheme.Scale(1))
                        {
                            middleX += x;
                            middleCount++;
                        }
                        if (Math.Abs(y - center) >= NativeTheme.Scale(3))
                        {
                            wingX += x;
                            wingCount++;
                        }
                    }
                }
                Assert.IsGreaterThan(5, total, $"{icon}: 图标为空");
                if (icon == NativeNavigationIcon.Up) Assert.IsGreaterThan(bottom, top);
                if (icon == NativeNavigationIcon.Down) Assert.IsGreaterThan(top, bottom);
                if (icon is NativeNavigationIcon.Left or NativeNavigationIcon.Right)
                {
                    Assert.IsGreaterThan(0, middleCount);
                    Assert.IsGreaterThan(0, wingCount);
                    double tipX = middleX / (double)middleCount;
                    double endsX = wingX / (double)wingCount;
                    if (icon == NativeNavigationIcon.Left) Assert.IsLessThan(endsX, tipX);
                    else Assert.IsGreaterThan(endsX, tipX);
                }
                if (icon == NativeNavigationIcon.Search) Assert.IsFalse(original[center - NativeTheme.Scale(1), center - NativeTheme.Scale(1)]);
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 提交和分支节点保持空心且连接线不穿过圆心(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] commit = Render((dc, rectangle) => NativeTheme.DrawToolWindowIcon(dc, rectangle, NativeToolWindowIcon.Commit, 0));
        int center = commit.GetLength(0) / 2;
        Assert.IsFalse(commit[center, center]);
        Assert.IsTrue(commit[center - NativeTheme.Scale(6), center]);
        Assert.IsTrue(commit[center + NativeTheme.Scale(6), center]);
        Assert.IsFalse(commit[center, center - NativeTheme.Scale(5)]);
        Assert.AreEqual(1, CountComponents(commit));

        bool[,] branch = Render((dc, rectangle) => NativeTheme.DrawToolWindowIcon(dc, rectangle, NativeToolWindowIcon.GitHistory, 0));
        Assert.IsFalse(branch[center - NativeTheme.Scale(4), center - NativeTheme.Scale(5)]);
        Assert.IsFalse(branch[center + NativeTheme.Scale(4), center - NativeTheme.Scale(3)]);
        Assert.AreEqual(1, CountComponents(branch));
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 更多为纵向三点且设置只有连续外轮廓和内孔(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] more = Render((dc, rectangle) => NativeTheme.DrawMoreIcon(dc, rectangle.Right / 2, rectangle.Bottom / 2, 0));
        int center = more.GetLength(0) / 2;
        Assert.AreEqual(3, CountComponents(more));
        for (int x = 0; x < more.GetLength(0); x++)
        {
            for (int y = 0; y < more.GetLength(1); y++)
            {
                if (more[x, y]) Assert.IsLessThanOrEqualTo(NativeTheme.Scale(1), Math.Abs(x - center));
            }
        }
        bool[,] gear = Render((dc, rectangle) => NativeTheme.DrawSettingsIcon(dc, rectangle, 0));
        Assert.AreEqual(2, CountComponents(gear));
        Assert.IsFalse(gear[center, center]);
        bool[,] hide = Render((dc, rectangle) => NativeTheme.DrawHideIcon(dc, rectangle.Right / 2, rectangle.Bottom / 2, 0));
        Assert.IsTrue(hide[center, center]);
        Assert.IsFalse(hide[center - NativeTheme.Scale(3), center - NativeTheme.Scale(3)]);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 弧线单独绘制时只占据请求的四分之一圆且不填充中心(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] arc = Render((dc, rectangle) =>
        {
            float radius = NativeTheme.Scale(6f);
            return NativeGdiPlusDrawing.StrokeShapes(dc, 0, NativeTheme.Scale(1.5f), [], [], [],
                [new(rectangle.Right / 2f - radius, rectangle.Bottom / 2f - radius, radius * 2, radius * 2, 0, 90)]);
        });
        int center = arc.GetLength(0) / 2;
        Assert.AreEqual(1, CountComponents(arc));
        Assert.IsFalse(arc[center, center]);
        Assert.IsTrue(arc[center + NativeTheme.Scale(4), center + NativeTheme.Scale(4)]);
        Assert.IsFalse(arc[center - NativeTheme.Scale(4), center + NativeTheme.Scale(4)]);
        Assert.IsFalse(arc[center + NativeTheme.Scale(4), center - NativeTheme.Scale(4)]);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void Changes按文件类型绘制不同图形且不超出图标区(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        List<bool[,]> rendered = [];
        foreach (string path in new[] { "docs/spec.md", "src/Window.cs", "assets/image.png", "file.txt", "Augit.slnx", ".gitignore" })
        {
            bool[,] pixels = Render((dc, rectangle) => NativeGitPanel.DrawChangedFileIcon(
                dc, rectangle.Right / 2, rectangle.Bottom / 2, path, 0));
            int center = pixels.GetLength(0) / 2;
            int count = 0;
            for (int y = 0; y < pixels.GetLength(1); y++)
            {
                for (int x = 0; x < pixels.GetLength(0); x++)
                {
                    if (!pixels[x, y]) continue;
                    count++;
                    Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(x - center));
                    Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(y - center));
                }
            }
            Assert.IsGreaterThan(5, count, $"{path}: 图标不能为空");
            foreach (bool[,] previous in rendered)
            {
                Assert.IsFalse(previous.Cast<bool>().SequenceEqual(pixels.Cast<bool>()), $"{path}: 不同类型不能统一画为空白纸页");
            }
            rendered.Add(pixels);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 普通文件绘制水平文字线且图片保留四边外框(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] document = Render((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, "LICENSE.txt", 0));
        bool[,] picture = Render((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, "image.png", 0));
        int center = document.GetLength(0) / 2;
        for (int x = center - NativeTheme.Scale(3); x <= center + NativeTheme.Scale(3); x++)
        {
            Assert.IsTrue(document[x, center - NativeTheme.Scale(2)], "普通文件首条文字线必须水平连续");
        }
        Assert.IsTrue(picture[center - NativeTheme.Scale(6), center]);
        Assert.IsTrue(picture[center + NativeTheme.Scale(6), center]);
        Assert.IsTrue(picture[center, center - NativeTheme.Scale(6)]);
        Assert.IsTrue(picture[center, center + NativeTheme.Scale(6)]);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 筛选下拉为无竖杆折线且菜单搜索保留空心镜圈(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] chevron = Render((dc, rectangle) => NativeTheme.DrawChevronIcon(dc, rectangle, true, 0));
        int center = chevron.GetLength(0) / 2;
        Assert.AreEqual(1, CountComponents(chevron));
        Assert.IsTrue(chevron[center, center + NativeTheme.Scale(2)]);
        Assert.IsFalse(chevron[center, center - NativeTheme.Scale(2)], "下拉折线中间不得出现导航箭头的竖杆");

        bool[,] search = Render((dc, rectangle) =>
        {
            NativeContextMenu.DrawIcon(dc, NativeContextMenuIcon.Search, rectangle, 0);
            return true;
        });
        Assert.AreEqual(1, CountComponents(search));
        Assert.IsFalse(search[center - NativeTheme.Scale(1), center - NativeTheme.Scale(1)]);
        Assert.IsTrue(search[center - NativeTheme.Scale(6), center - NativeTheme.Scale(1)], "搜索图标必须绘制镜圈左侧");
        Assert.IsTrue(search[center - NativeTheme.Scale(1), center - NativeTheme.Scale(6)], "搜索图标必须绘制镜圈顶部");
        Assert.IsTrue(search[center + NativeTheme.Scale(5), center + NativeTheme.Scale(5)], "搜索图标必须绘制手柄");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 普通提交为实心节点且HEAD保留外环与中心点(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] ordinary = Render((dc, rectangle) =>
        {
            NativeTheme.DrawCommitGraphNode(dc, rectangle.Right / 2, rectangle.Bottom / 2, 0, 0xFFFFFF, head: false);
            return true;
        });
        bool[,] head = Render((dc, rectangle) =>
        {
            NativeTheme.DrawCommitGraphNode(dc, rectangle.Right / 2, rectangle.Bottom / 2, 0, 0xFFFFFF, head: true);
            return true;
        });
        int center = ordinary.GetLength(0) / 2;
        Assert.IsTrue(ordinary[center, center]);
        Assert.IsTrue(head[center, center]);
        Assert.AreEqual(1, CountComponents(ordinary));
        Assert.AreEqual(2, CountComponents(head));
        Assert.IsFalse(head[center + NativeTheme.Scale(3), center]);
    }

    private static int CountComponents(bool[,] pixels)
    {
        HashSet<(int X, int Y)> visited = [];
        int count = 0;
        for (int y = 0; y < pixels.GetLength(1); y++)
        {
            for (int x = 0; x < pixels.GetLength(0); x++)
            {
                if (!pixels[x, y] || !visited.Add((x, y))) continue;
                count++;
                Queue<(int X, int Y)> pending = new();
                pending.Enqueue((x, y));
                while (pending.TryDequeue(out (int X, int Y) point))
                {
                    for (int row = Math.Max(0, point.Y - 1); row <= Math.Min(pixels.GetLength(1) - 1, point.Y + 1); row++)
                    {
                        for (int column = Math.Max(0, point.X - 1); column <= Math.Min(pixels.GetLength(0) - 1, point.X + 1); column++)
                        {
                            if (pixels[column, row] && visited.Add((column, row))) pending.Enqueue((column, row));
                        }
                    }
                }
            }
        }
        return count;
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void Markdown为短实心字形且CSharp保留开放圆弧(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] markdown = Render((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, "README.md", 0u));
        bool[,] csharp = Render((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, "MainWindow.cs", 0u));
        int center = markdown.GetLength(0) / 2;
        Assert.AreEqual(2, CountComponents(markdown), "M 与下箭头必须独立");
        Assert.IsTrue(markdown[center - NativeTheme.Scale(6), center + NativeTheme.Scale(2)], "M 左竖笔必须实心");
        Assert.IsTrue(markdown[center - NativeTheme.Scale(6) - 1, center + NativeTheme.Scale(2)], "M 不能退回细线");
        for (int y = 0; y < markdown.GetLength(1); y++)
        {
            for (int x = 0; x < markdown.GetLength(0); x++)
            {
                if (markdown[x, y]) Assert.IsLessThanOrEqualTo(NativeTheme.Scale(5), Math.Abs(y - center));
            }
        }
        bool leftArc = false;
        for (int y = center - NativeTheme.Scale(3); y <= center + NativeTheme.Scale(3); y++)
        {
            for (int x = center - NativeTheme.Scale(8); x <= center - NativeTheme.Scale(4); x++)
            {
                leftArc |= csharp[x, y];
            }
        }
        Assert.IsTrue(leftArc, "C 的左圆弧必须可见");
        Assert.IsFalse(csharp[center - NativeTheme.Scale(3), center], "C 内部必须开放");
        Assert.IsFalse(csharp[center, center], "C 的右侧开口不能封闭");
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void 类型图标使用主题类型色且修改界面字体不改变像素(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string previousFamily = NativeTheme.UiFontFamilyForTest;
        double previousSize = NativeTheme.UiFontSizeForTest;
        try
        {
            foreach (string name in new[] { "README.md", "MainWindow.cs", "preview.png", "index.html", "settings.json" })
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                uint[,] original = RenderColors((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, name, dark));
                NativeTheme.ConfigureUiTypography("Consolas", 25);
                uint[,] changed = RenderColors((dc, rectangle) => NativeTheme.DrawFileTypeIcon(dc, rectangle, name, dark));
                CollectionAssert.AreEqual(original.Cast<uint>().ToArray(), changed.Cast<uint>().ToArray(), name);
                uint color = NativeTheme.FileTypeIconColor(name, dark);
                int closest = original.Cast<uint>().Min(pixel =>
                    Math.Abs((int)(pixel & 255) - (int)(color & 255))
                    + Math.Abs((int)((pixel >> 8) & 255) - (int)((color >> 8) & 255))
                    + Math.Abs((int)((pixel >> 16) & 255) - (int)((color >> 16) & 255)));
                // 斜线在 96 DPI 下可能没有完全覆盖一个像素，允许边缘与白底混合。
                Assert.IsLessThanOrEqualTo(110, closest, $"{name}: 图形必须使用类型色");
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(previousFamily, previousSize);
        }
    }

    [TestMethod]
    public void 文件类型色不依赖Git状态且缺省使用中性灰()
    {
        Assert.AreEqual(0xF07435u, NativeTheme.FileTypeIconColor("README.MD", false));
        Assert.AreEqual(0x5C9A36u, NativeTheme.FileTypeIconColor("MainWindow.cs", false));
        Assert.AreEqual(NativeTheme.Palette(false).Muted, NativeTheme.FileTypeIconColor("LICENSE", false));
        Assert.AreEqual(NativeTheme.FileTypeIconColor("README.md", true), NativeTheme.FileTypeIconColor("other.markdown", true));
        Assert.IsFalse(NativeGdiPlusDrawing.FillPolygon(0, 0, []));
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 比较箭头上下错位且方向与参考一致(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] pixels = Render((dc, rectangle) => NativeTheme.DrawCompareIcon(
            dc, rectangle.Right / 2, rectangle.Bottom / 2, 0));
        int center = pixels.GetLength(0) / 2;
        Assert.IsTrue(pixels[center + NativeTheme.Scale(5), center - NativeTheme.Scale(4)]);
        Assert.IsTrue(pixels[center - NativeTheme.Scale(5), center + NativeTheme.Scale(4)]);
        Assert.IsFalse(pixels[center - NativeTheme.Scale(5), center - NativeTheme.Scale(4)]);
        Assert.IsFalse(pixels[center + NativeTheme.Scale(5), center + NativeTheme.Scale(4)]);
        Assert.AreEqual(2, CountComponents(pixels), "上下箭头不连接");
        Assert.IsFalse(pixels[center, center]);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 回滚保留曲线尾部且定位中心与眼睛瞳孔保持空心(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] rollback = Render((dc, r) => NativeTheme.DrawRollbackIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        bool[,] locate = Render((dc, r) => NativeTheme.DrawLocateIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        bool[,] preview = Render((dc, r) => NativeTheme.DrawPreviewIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        bool[,] collapse = Render((dc, r) => NativeTheme.DrawCollapseIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        int center = rollback.GetLength(0) / 2;
        Assert.IsTrue(rollback[center + NativeTheme.Scale(6), center + NativeTheme.Scale(1)]);
        Assert.IsTrue(rollback[center, center + NativeTheme.Scale(5)], "回滚尾部必须回到左侧");
        Assert.AreEqual(1, CountComponents(rollback));
        Assert.IsFalse(locate[center, center], "定位十字不能穿过中心");
        Assert.IsFalse(preview[center, center], "预览瞳孔必须空心");
        Assert.IsTrue(preview[center - NativeTheme.Scale(7), center]);
        Assert.AreEqual(2, CountComponents(collapse));
        Assert.IsFalse(collapse[center, center]);
        Assert.IsTrue(collapse[center, center - NativeTheme.Scale(3)]);
        Assert.IsTrue(collapse[center, center + NativeTheme.Scale(3)]);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 菜单与工具栏的同名图标使用相同像素并保持网格边界(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        foreach (NativeContextMenuIcon icon in new[] { NativeContextMenuIcon.Refresh, NativeContextMenuIcon.Compare,
            NativeContextMenuIcon.Reset, NativeContextMenuIcon.Revert, NativeContextMenuIcon.History,
            NativeContextMenuIcon.Locate, NativeContextMenuIcon.Terminal, NativeContextMenuIcon.Settings })
        {
            bool[,] menu = Render((dc, r) => { NativeContextMenu.DrawIcon(dc, icon, r, 0); return true; });
            bool[,] toolbar = Render((dc, r) => icon switch
            {
                NativeContextMenuIcon.Refresh => NativeTheme.DrawRefreshIcon(dc, r.Right / 2, r.Bottom / 2, 0),
                NativeContextMenuIcon.Compare => NativeTheme.DrawCompareIcon(dc, r.Right / 2, r.Bottom / 2, 0),
                NativeContextMenuIcon.Reset or NativeContextMenuIcon.Revert => NativeTheme.DrawRollbackIcon(dc, r.Right / 2, r.Bottom / 2, 0),
                NativeContextMenuIcon.History => NativeTheme.DrawHistoryIcon(dc, r, 0),
                NativeContextMenuIcon.Locate => NativeTheme.DrawLocateIcon(dc, r.Right / 2, r.Bottom / 2, 0),
                NativeContextMenuIcon.Settings => NativeTheme.DrawSettingsIcon(dc, r, 0),
                _ => NativeTheme.DrawToolWindowIcon(dc, r, NativeToolWindowIcon.Terminal, 0),
            });
            CollectionAssert.AreEqual(toolbar.Cast<bool>().ToArray(), menu.Cast<bool>().ToArray(), icon.ToString());
            int center = menu.GetLength(0) / 2;
            for (int y = 0; y < menu.GetLength(1); y++)
            {
                for (int x = 0; x < menu.GetLength(0); x++)
                {
                    if (!menu[x, y]) continue;
                    Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(x - center), icon.ToString());
                    Assert.IsLessThanOrEqualTo(NativeTheme.Scale(8), Math.Abs(y - center), icon.ToString());
                }
            }
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 重命名克隆和冲突图标保持独立几何与字体无关(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        string family = NativeTheme.UiFontFamilyForTest;
        double size = NativeTheme.UiFontSizeForTest;
        try
        {
            foreach (NativeContextMenuIcon icon in new[]
            {
                NativeContextMenuIcon.Rename,
                NativeContextMenuIcon.Clone,
                NativeContextMenuIcon.Conflict,
            })
            {
                NativeTheme.ConfigureUiTypography("Segoe UI", 13);
                bool[,] first = Render((dc, rectangle) => NativeTheme.DrawMenuActionIcon(dc, icon, rectangle, 0));
                NativeTheme.ConfigureUiTypography("Consolas", 40);
                bool[,] second = Render((dc, rectangle) => NativeTheme.DrawMenuActionIcon(dc, icon, rectangle, 0));
                CollectionAssert.AreEqual(first.Cast<bool>().ToArray(), second.Cast<bool>().ToArray(), icon.ToString());
                Assert.IsGreaterThan(8, first.Cast<bool>().Count(pixel => pixel), icon.ToString());
            }
        }
        finally
        {
            NativeTheme.ConfigureUiTypography(family, size);
        }
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 刷新保留两段断口且托盘具有独立凹口(int dpi)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        bool[,] refresh = Render((dc, r) => NativeTheme.DrawRefreshIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        bool[,] tray = Render((dc, r) => NativeTheme.DrawTrayArrowIcon(dc, r.Right / 2, r.Bottom / 2, 0));
        int center = refresh.GetLength(0) / 2;
        Assert.AreEqual(2, CountComponents(refresh));
        Assert.IsFalse(refresh[center, center]);
        Assert.IsTrue(refresh[center, center - NativeTheme.Scale(5)]);
        Assert.IsTrue(refresh[center, center + NativeTheme.Scale(5)]);
        Assert.IsTrue(refresh[center - NativeTheme.Scale(5), center - NativeTheme.Scale(2)], "左上弧必须向内转圆，不能退化为多边形外边");
        Assert.IsTrue(refresh[center + NativeTheme.Scale(5), center + NativeTheme.Scale(2)], "右下弧必须向内转圆");
        Assert.IsTrue(tray[center, center + NativeTheme.Scale(6)]);
        Assert.IsTrue(tray[center, center + NativeTheme.Scale(3)], "托盘上沿必须有凹口");
        Assert.IsTrue(tray[center - NativeTheme.Scale(6), center]);
        Assert.IsTrue(tray[center + NativeTheme.Scale(6), center]);
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void 复选框使用实心勾选底和独立的部分选中标记(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        uint fill = dark ? palette.Accent : 0x00F07435;
        int left = NativeTheme.Scale(8), center = NativeTheme.Scale(16);
        foreach (NativeCheckboxState state in Enum.GetValues<NativeCheckboxState>())
        {
            uint[,] pixels = RenderColors((dc, r) => NativeTheme.DrawCheckbox(dc, left, center, state, dark));
            uint expectedInterior = state == NativeCheckboxState.Unchecked ? palette.Panel : fill;
            Assert.AreEqual(expectedInterior, pixels[left + NativeTheme.Scale(3), center - NativeTheme.Scale(3)], state.ToString());
            if (state == NativeCheckboxState.Mixed)
            {
                AssertWhiteCheckboxMark(pixels[left + NativeTheme.Scale(7), center], fill);
                Assert.AreEqual(fill, pixels[left + NativeTheme.Scale(7), center + NativeTheme.Scale(3)], "部分选中不能画成勾号");
            }
            if (state == NativeCheckboxState.Checked)
            {
                // 小字号抗锯齿会把折点分摊到相邻像素，检查折点邻域的白色笔画。
                uint brightest = fill;
                for (int y = center + NativeTheme.Scale(3) - 1; y <= center + NativeTheme.Scale(3) + 1; y++)
                {
                    for (int x = left + NativeTheme.Scale(6) - 1; x <= left + NativeTheme.Scale(6) + 1; x++)
                    {
                        uint pixel = pixels[x, y];
                        if ((pixel & 255) > (brightest & 255)) brightest = pixel;
                    }
                }
                AssertWhiteCheckboxMark(brightest, fill);
            }
            uint[,] disabled = RenderColors((dc, r) => NativeTheme.DrawCheckbox(dc, left, center, state, dark, enabled: false));
            Assert.AreEqual(palette.PanelMuted, disabled[left + NativeTheme.Scale(3), center - NativeTheme.Scale(3)]);
            Assert.IsFalse(disabled.Cast<uint>().Contains(fill), "禁用框不能继续显示启用蓝色");
        }
    }

    private static void AssertWhiteCheckboxMark(uint pixel, uint fill)
    {
        // 白色笔画在抗锯齿边缘应按同一覆盖率把底色的各通道推向白色。
        double coverage = ((pixel & 255) - (double)(fill & 255)) / (255 - (fill & 255));
        Assert.IsGreaterThanOrEqualTo(0.4, coverage, "标记必须明显亮于选中底色");
        foreach (int shift in new[] { 8, 16 })
        {
            double background = (fill >> shift) & 255;
            double expected = background + (255 - background) * coverage;
            Assert.IsLessThanOrEqualTo(4d, Math.Abs(((pixel >> shift) & 255) - expected), "标记必须是白色与底色的混合，不能使用蓝勾或灰勾");
        }
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void Diff分段组保持外框中性选中与独立焦点(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        NativeMethods.Rectangle group = NativeTheme.DiffModeGroupBounds(NativeTheme.Scale(100), NativeTheme.Scale(39));
        NativeMethods.Rectangle left = NativeTheme.DiffModeButtonBounds(group, sideBySide: true);
        NativeMethods.Rectangle right = NativeTheme.DiffModeButtonBounds(group, sideBySide: false);
        uint groupColor = dark ? palette.PanelMuted : 0x00F7F5F4;
        foreach (bool sideBySide in new[] { true, false })
        {
            uint[,] Draw(bool focused) => RenderColors((dc, _) =>
            {
                NativeTheme.DrawDiffModeGroup(dc, group, dark);
                bool first = NativeTheme.DrawDiffToolbarMode(new()
                {
                    DeviceContext = dc,
                    ItemRectangle = left,
                    ItemState = focused ? NativeMethods.OwnerDrawFocus : 0
                }, NativeDiffModeIcon.SideBySide, sideBySide, dark);
                bool second = NativeTheme.DrawDiffToolbarMode(new() { DeviceContext = dc, ItemRectangle = right },
                    NativeDiffModeIcon.Unified, !sideBySide, dark);
                return first && second;
            }, palette.Panel, logicalWidth: 112, logicalHeight: 40);
            uint[,] normal = Draw(false);
            Assert.AreEqual(palette.BorderStrong, normal[group.Left + NativeTheme.Scale(20), group.Top]);
            Assert.AreEqual(groupColor, normal[left.Right, left.Top + NativeTheme.Scale(10)], "按钮间隙不能变成白缝或蓝线。");
            Assert.AreEqual(sideBySide ? palette.Panel : groupColor, normal[left.Left + NativeTheme.Scale(6), left.Top + NativeTheme.Scale(8)]);
            Assert.AreEqual(sideBySide ? groupColor : palette.Panel, normal[right.Left + NativeTheme.Scale(6), right.Top + NativeTheme.Scale(8)]);
            Assert.IsFalse(normal.Cast<uint>().Contains(palette.Accent), "选中模式不能冒充键盘焦点。");
            uint[,] focused = Draw(true);
            Assert.AreEqual(palette.Accent, focused[left.Left + NativeTheme.Scale(2), left.Top + NativeTheme.Scale(8)]);
            Assert.AreEqual(normal[right.Left + NativeTheme.Scale(6), right.Top + NativeTheme.Scale(8)],
                focused[right.Left + NativeTheme.Scale(6), right.Top + NativeTheme.Scale(8)], "焦点不能污染相邻按钮。");
        }
    }

    [TestMethod]
    [DataRow(96, false)]
    [DataRow(120, false)]
    [DataRow(144, false)]
    [DataRow(96, true)]
    [DataRow(120, true)]
    [DataRow(144, true)]
    public void Diff文件信息单双栏均绘制双方身份且长引用不覆盖路径(int dpi, bool dark)
    {
        using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        for (int mode = 0; mode < 2; mode++)
        {
            bool sideBySide = mode == 1;
            NativeMethods.Rectangle bounds = new() { Right = NativeTheme.Scale(360), Bottom = NativeDiffFileHeader.Height(sideBySide) };
            NativeDiffFileHeaderLayout layout = NativeDiffFileHeader.Calculate(bounds, sideBySide, NativeTheme.Scale(400));
            Assert.IsTrue(layout.Source.Right < layout.Path.Left && layout.Path.Right <= bounds.Right);
            Assert.IsTrue(sideBySide ? layout.Target.Left > layout.Path.Right : layout.Target.Top >= layout.Source.Bottom);
            Assert.IsTrue(layout.Target.Bottom <= bounds.Bottom && layout.Target.Right <= bounds.Right);
            uint[,] pixels = RenderColors((dc, _) =>
            {
                NativeDiffFileHeader.Draw(dc, bounds, sideBySide,
                    "feature/很长的来源引用", "目标引用", "src/文件.cs", palette);
                return true;
            }, palette.Panel, logicalWidth: 360, logicalHeight: 55);
            int Ink(NativeMethods.Rectangle region)
            {
                int count = 0;
                for (int y = region.Top; y < region.Bottom; y++)
                    for (int x = region.Left; x < region.Right; x++)
                        if (pixels[x, y] != palette.Panel) count++;
                return count;
            }
            Assert.IsGreaterThan(12, Ink(layout.Source), "来源引用没有绘制。");
            Assert.IsGreaterThan(12, Ink(layout.Target), "目标引用没有绘制。");
            Assert.IsGreaterThan(8, Ink(layout.Path), "路径被来源引用挤掉。");
            Assert.IsGreaterThan(5, Ink(layout.SourceIcon));
            Assert.IsGreaterThan(5, Ink(layout.TargetIcon));
        }
    }

    private static bool[,] Render(NativeNavigationIcon icon)
    {
        return Render((dc, rectangle) => NativeTheme.DrawNavigationIcon(dc, rectangle, icon, 0));
    }

    private static bool[,] Render(Func<nint, NativeMethods.Rectangle, bool> draw)
    {
        uint[,] colors = RenderColors(draw);
        bool[,] pixels = new bool[colors.GetLength(0), colors.GetLength(1)];
        for (int y = 0; y < pixels.GetLength(1); y++)
        {
            for (int x = 0; x < pixels.GetLength(0); x++) pixels[x, y] = (colors[x, y] & 0xFF) < 192;
        }
        return pixels;
    }

    private static uint[,] RenderColors(Func<nint, NativeMethods.Rectangle, bool> draw, uint background = 0x00FFFFFF,
        int logicalWidth = 32, int logicalHeight = 32)
    {
        int width = NativeTheme.Scale(logicalWidth), height = NativeTheme.Scale(logicalHeight);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        nint brush = NativeMethods.CreateSolidBrush(background);
        try
        {
            NativeMethods.Rectangle rectangle = new() { Right = width, Bottom = height };
            _ = NativeMethods.FillRectangle(dc, ref rectangle, brush);
            Assert.IsTrue(draw(dc, rectangle));
            uint[,] pixels = new uint[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[x, y] = NativeMethods.GetPixel(dc, x, y);
                }
            }
            return pixels;
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(brush);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
