namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    public Task 手工增加冲突数量位数只重新分配标题不覆盖计数或重排正文() => RunAsync(async (dialog, _) =>
    {
        Assert.IsFalse(dialog.CompactHeaderForTest);
        var initialTitle = Bounds(dialog.FileTitleHandleForTest);
        var body = Bounds(dialog.ResultHandleForTest);
        var next = Bounds(Item(dialog, 5));
        Append(dialog.ResultHandleForTest, UndoConflict);
        await WaitForConflictPresentationAsync(dialog);
        Assert.Contains("未处理冲突块：10", dialog.NoticeForTest);
        var title = Bounds(dialog.FileTitleHandleForTest);
        var window = Bounds(dialog.HandleForTest);
        Assert.IsLessThan(initialTitle.Right, title.Right);
        Assert.IsLessThanOrEqualTo(dialog.CountBoundsForTest.Left + window.Left, title.Right + NativeTheme.Scale(8));
        Assert.AreEqual(body, Bounds(dialog.ResultHandleForTest));
        Assert.AreEqual(next, Bounds(Item(dialog, 5)));
        Assert.IsTrue(dialog.ResultIsDirtyForTest);
    }, resultText: string.Concat(Enumerable.Repeat(UndoConflict, 9)));

    [TestMethod]
    [DataRow(96, "Light")]
    [DataRow(120, "Light")]
    [DataRow(144, "Light")]
    [DataRow(96, "Dark")]
    [DataRow(120, "Dark")]
    [DataRow(144, "Dark")]
    public async Task 窄窗大字号保留文件名来源身份导航和独立正文字号(int dpi, string theme)
    {
        foreach (int width in new[] { 1024, 1440 })
            foreach (int size in new[] { 13, 40 })
            {
                await RunAsync((dialog, _) =>
                {
                    var window = Bounds(dialog.HandleForTest);
                    var title = Bounds(dialog.FileTitleHandleForTest);
                    var previous = Bounds(Item(dialog, 4));
                    var next = Bounds(Item(dialog, 5));
                    var count = dialog.CountBoundsForTest;
                    Assert.AreEqual(size == 40, dialog.CompactHeaderForTest);
                    Assert.AreEqual(size == 40, dialog.SplitColumnHeadersForTest);
                    Assert.AreEqual(size == 40 ? "NativeGitPanel.cs" : "解决冲突 · NativeGitPanel.cs",
                        NativeMethods.GetWindowTextValue(dialog.FileTitleHandleForTest));
                    AssertTextFits(dialog.HandleForTest, NativeMethods.GetWindowTextValue(dialog.FileTitleHandleForTest),
                        title, NativeTheme.UiMediumFont);
                    Assert.IsLessThanOrEqualTo(Bounds(Item(dialog, 8)).Left, title.Right);
                    AssertTextFits(dialog.HandleForTest, "1 个未处理冲突", count, NativeTheme.UiFont);
                    Assert.IsLessThanOrEqualTo(previous.Left - NativeTheme.Scale(8), count.Right + window.Left);
                    Assert.IsLessThanOrEqualTo(next.Left, previous.Right + NativeTheme.Scale(8));
                    if (size == 40) Assert.IsLessThanOrEqualTo(previous.Top, title.Bottom);
                    else Assert.IsLessThanOrEqualTo(count.Left + window.Left, title.Right + NativeTheme.Scale(8));
                    string[] roles = ["当前分支", "最终结果", "合入内容"];
                    string[] details = ["main", "可编辑", "feature/ux"];
                    nint[] bodies = [dialog.YoursHandleForTest, dialog.ResultHandleForTest, dialog.TheirsHandleForTest];
                    for (int index = 0; index < 3; index++)
                    {
                        var label = Bounds(Item(dialog, 30 + index));
                        if (size == 40)
                        {
                            var line = label;
                            line.Left += NativeTheme.Scale(6);
                            line.Right -= NativeTheme.Scale(6);
                            line.Bottom = line.Top + NativeTheme.UiLineHeight;
                            AssertTextFits(dialog.HandleForTest, roles[index], line, NativeTheme.UiFont);
                            AssertTextFits(dialog.HandleForTest, details[index], line, NativeTheme.UiFont);
                            Assert.IsGreaterThanOrEqualTo(NativeTheme.UiLineHeight * 2 + NativeTheme.Scale(12), label.Bottom - label.Top);
                        }
                        var body = Bounds(bodies[index]);
                        Assert.AreEqual(label.Bottom, body.Top);
                        Assert.IsGreaterThan(NativeTheme.Scale(80), body.Bottom - body.Top, "正文仍须保留可阅读的视口。");
                        Assert.IsLessThanOrEqualTo(Bounds(Item(dialog, 1)).Top, body.Bottom);
                        int codeHeight = (int)NativeMethods.SendMessage(bodies[index], 2279, 0, 0);
                        Assert.IsLessThanOrEqualTo(NativeTheme.Scale(26), codeHeight, "界面字号不得带动等宽正文变大。");
                    }
                    Assert.AreEqual(1, dialog.ResultParseCountForTest, "排布不重新解析正文。");
                    return Task.CompletedTask;
                }, unresolved: true, dpi: dpi, theme: theme, size: size, codeSize: 13, width: width,
                    height: width == 1024 ? 640 : 900, relativePath: "NativeGitPanel.cs",
                    yoursLabel: "当前分支 · main", theirsLabel: "合入内容 · feature/ux");
            }
    }

    private static void AssertTextFits(nint owner, string text, NativeMethods.Rectangle bounds, nint font)
    {
        nint dc = NativeMethods.GetDeviceContext(owner);
        nint previous = NativeMethods.SelectObject(dc, font);
        try
        {
            NativeMethods.Rectangle measured = default;
            _ = NativeMethods.DrawText(dc, text, text.Length, ref measured,
                NativeMethods.DrawTextCalculateRectangle | NativeMethods.DrawTextSingleLine | NativeMethods.DrawTextNoPrefix);
            Assert.IsGreaterThanOrEqualTo(measured.Right, bounds.Right - bounds.Left, $"文字被裁切：{text}");
            Assert.IsGreaterThanOrEqualTo(measured.Bottom, bounds.Bottom - bounds.Top, $"字高被裁切：{text}");
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.ReleaseDeviceContext(owner, dc);
        }
    }
}
