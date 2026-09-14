using System.Text;

namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    private static readonly string[] AlignmentContexts = ["前文", "中间甲", "中间乙", "后文"];
    [TestMethod]
    [DataRow("Light", 96, false)]
    [DataRow("Dark", 120, false)]
    [DataRow("Dark", 144, true)]
    public Task 多处长短冲突与空侧在同屏逐块对齐且展示留白不进入接受保存(string theme, int dpi, bool background)
    {
        const string first = "<<<<<<< HEAD\n左一\n左二\n=======\n右一\n>>>>>>> feature\n";
        const string second = "<<<<<<< HEAD\n左三\n=======\n右二\n右三\n>>>>>>> feature\n";
        const string empty = "<<<<<<< HEAD\n=======\n新增一\n新增二\n>>>>>>> feature\n";
        string tail = "后文\n" + (background ? new string('x', 110_000) : string.Empty);
        string yours = "前文\n左一\n左二\n中间甲\n左三\n中间乙\n" + tail;
        string theirs = "前文\n右一\n中间甲\n右二\n右三\n中间乙\n新增一\n新增二\n" + tail;
        string result = "前文\n" + first + "中间甲\n" + second + "中间乙\n" + empty + tail;
        return RunAsync(async (dialog, service) =>
        {
            await WaitForConflictPresentationAsync(dialog);
            nint[] editors = [dialog.YoursHandleForTest, dialog.ResultHandleForTest, dialog.TheirsHandleForTest];
            string[] texts = [yours, result, theirs];
            for (int side = 0; side < 3; side++) _ = NativeMethods.SendMessage(editors[side], 2613, 0, 0);
            await Task.Delay(60);
            int previousY = -1;
            foreach (string context in AlignmentContexts)
            {
                int y = TextY(editors[0], texts[0], context);
                Assert.IsGreaterThan(previousY, y, "不同正文行必须得到递增的屏幕纵坐标。");
                previousY = y;
                Assert.IsTrue(NativeMethods.GetClientRectangle(editors[0], out var bounds));
                Assert.IsLessThan(bounds.Bottom, y, "校准行必须确实处于当前可见正文内。");
                Assert.AreEqual(y, TextY(editors[1], texts[1], context), context);
                Assert.AreEqual(y, TextY(editors[2], texts[2], context), context);
            }
            Assert.AreEqual(TextY(editors[0], yours, "左一"), TextY(editors[2], theirs, "右一"));
            Assert.AreEqual(TextY(editors[0], yours, "左三"), TextY(editors[2], theirs, "右二"));
            Assert.AreEqual(result, dialog.ResultTextForTest);
            Assert.IsFalse(dialog.ResultIsDirtyForTest);
            int parses = dialog.ResultParseCountForTest;
            await Task.Delay(180);
            Assert.AreEqual(parses, dialog.ResultParseCountForTest, "留白装饰不能触发下一轮正文解析。");
            for (int index = 0; index < 3; index++)
            {
                ClickConflictAction(dialog, 3);
                await WaitForConflictPresentationAsync(dialog);
            }
            string expected = "前文\n左一\n左二\n右一\n中间甲\n左三\n右二\n右三\n中间乙\n新增一\n新增二\n" + tail;
            Assert.AreEqual(expected, dialog.ResultTextForTest);
            Task save = dialog.SaveForTestAsync();
            Assert.AreEqual(expected, service.Saves.Single().ResultText);
            service.Pending.SetResult(Failure());
            await save;
            for (int index = 0; index < 3; index++)
            {
                _ = NativeMethods.SendMessage(editors[1], 2176, 0, 0);
                await WaitForConflictPresentationAsync(dialog);
            }
            Assert.AreEqual(result, dialog.ResultTextForTest);
            Assert.IsFalse(dialog.ResultIsDirtyForTest, "展示留白不应成为独立撤销步骤或修改保存点。");
        }, dpi: dpi, theme: theme, size: 13, codeSize: 10, width: 1440, height: 900,
            resultText: result, yoursText: yours, theirsText: theirs);
    }

    [TestMethod]
    public Task 文件首行即冲突时标记不占首行且公共尾行仍对齐() => RunAsync(async (dialog, service) =>
    {
        await WaitForConflictPresentationAsync(dialog);
        const string yours = "左一\n左二\n尾行\n";
        const string theirs = "右一\n尾行\n";
        string result = dialog.ResultTextForTest!;
        Assert.IsFalse(dialog.ResultLineVisibleForTest(0));
        Assert.AreEqual(0, TextY(dialog.ResultHandleForTest, result, "左一"));
        int tail = TextY(dialog.YoursHandleForTest, yours, "尾行");
        Assert.AreEqual(tail, TextY(dialog.ResultHandleForTest, result, "尾行"));
        Assert.AreEqual(tail, TextY(dialog.TheirsHandleForTest, theirs, "尾行"));
        Assert.IsFalse(dialog.ResultIsDirtyForTest);
        Assert.IsEmpty(service.Saves);
    }, resultText: "<<<<<<< HEAD\n左一\n左二\n=======\n右一\n>>>>>>> feature\n尾行\n",
        yoursText: "左一\n左二\n尾行\n", theirsText: "右一\n尾行\n");

    private static int TextY(nint editor, string text, string line)
    {
        int index = text.IndexOf(line + "\n", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index);
        int position = Encoding.UTF8.GetByteCount(text.AsSpan(0, index));
        return (int)NativeMethods.SendMessage(editor, 2165, 0, position); // SCI_POINTYFROMPOSITION。
    }
}
