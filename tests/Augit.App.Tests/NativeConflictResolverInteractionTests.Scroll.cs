namespace Augit.App.Tests;

public sealed partial class NativeConflictResolverInteractionTests
{
    [TestMethod]
    [DataRow("Light", 96, false)]
    [DataRow("Dark", 120, false)]
    [DataRow("Dark", 144, true)]
    public Task 三栏滚轮和导航按真实屏幕首行对齐且接受撤销后仍有效(string theme, int dpi, bool background)
    {
        string prefix = string.Concat(Enumerable.Range(0, 10).Select(i => $"前文 {i}\n"));
        string gap = string.Concat(Enumerable.Range(0, 60).Select(i => $"公共内容 {i}\n"));
        string tail = string.Concat(Enumerable.Range(0, background ? 1_200 : 80).Select(i => $"后文 {i} {new string('x', 100)}\n"));
        const string first = "<<<<<<< HEAD\n左一\n左二\n=======\n右一\n>>>>>>> feature\n";
        const string second = "<<<<<<< HEAD\n左三\n=======\n右二\n右三\n>>>>>>> feature\n";
        string yours = prefix + "左一\n左二\n" + gap + "左三\n" + tail;
        string result = prefix + first + gap + second + tail;
        string theirs = prefix + "右一\n" + gap + "右二\n右三\n" + tail;
        return RunAsync(async (dialog, service) =>
        {
            await WaitForConflictPresentationAsync(dialog);
            nint[] editors = [dialog.YoursHandleForTest, dialog.ResultHandleForTest, dialog.TheirsHandleForTest];
            string[] texts = [yours, result, theirs];
            foreach (nint editor in editors)
            {
                Assert.AreEqual(NativeTheme.Scale(12), (int)NativeMethods.SendMessage(editor, 2156, 0, 0));
                Assert.AreEqual(NativeTheme.Scale(12), (int)NativeMethods.SendMessage(editor, 2158, 0, 0));
            }
            Assert.IsFalse(dialog.ResultLineVisibleForTest(10));
            Assert.IsFalse(dialog.ResultLineVisibleForTest(13));
            Assert.IsFalse(dialog.ResultLineVisibleForTest(15));
            for (int side = 0; side < 3; side++)
            {
                int line = Array.IndexOf(texts[side].Split('\n'), "公共内容 20");
                int display = (int)NativeMethods.SendMessage(editors[side], 2220, (nuint)line, 0);
                _ = NativeMethods.SendMessage(editors[side], 2613, (nuint)display, 0);
                await Task.Delay(60);
                AssertScreenTop(editors, texts, "公共内容 20");
                _ = NativeMethods.SendMessage(editors[side], NativeMethods.WindowMessageMouseWheel,
                    unchecked((nuint)(-120 << 16)), 0);
                await Task.Delay(60);
                string actual = ScreenTop(editors[side], texts[side]);
                Assert.AreNotEqual("公共内容 20", actual, "长正文的实际滚轮必须移动来源栏。");
                Assert.StartsWith("公共内容 ", actual);
                AssertScreenTop(editors, texts, actual);
                var before = dialog.FirstVisibleLinesForTest;
                int visible = (int)NativeMethods.SendMessage(editors[side], 2152, 0, 0);
                int documentLine = (int)NativeMethods.SendMessage(editors[side], 2221, (nuint)visible, 0);
                nint position = NativeMethods.SendMessage(editors[side], 2167, (nuint)documentLine, 0);
                _ = NativeMethods.SendMessage(editors[side], 2026, (nuint)position, 0);
                _ = NativeMethods.SendMessage(editors[side], 2141, (nuint)position, 0); // 修改光标和锚点，不要求滚动定位。
                await Task.Delay(40);
                Assert.AreEqual(before, dialog.FirstVisibleLinesForTest, "不滚动的选区变化不能驱动其他栏。");
            }

            ClickConflictAction(dialog, 5);
            await Task.Delay(60);
            AssertScreenTop(editors, texts, "公共内容 58");
            ClickConflictAction(dialog, 1);
            await WaitForConflictPresentationAsync(dialog);
            Assert.DoesNotContain("左三\n=======", dialog.ResultTextForTest!);
            _ = NativeMethods.SendMessage(dialog.ResultHandleForTest, 2176, 0, 0);
            await WaitForConflictPresentationAsync(dialog);
            Assert.AreEqual(result, dialog.ResultTextForTest);
            ClickConflictAction(dialog, 5);
            await Task.Delay(60);
            AssertScreenTop(editors, texts, "公共内容 58");
            Assert.IsEmpty(service.Saves);
        }, dpi: dpi, theme: theme, size: 13, resultText: result, yoursText: yours, theirsText: theirs);
    }

    private static string ScreenTop(nint editor, string text)
    {
        int visible = (int)NativeMethods.SendMessage(editor, 2152, 0, 0);
        int document = (int)NativeMethods.SendMessage(editor, 2221, (nuint)visible, 0);
        return text.Split('\n')[document];
    }

    private static void AssertScreenTop(nint[] editors, string[] texts, string expected)
    {
        for (int side = 0; side < editors.Length; side++)
            Assert.AreEqual(expected, ScreenTop(editors[side], texts[side]), $"第 {side + 1} 栏的屏幕首行错位。");
    }

    [TestMethod]
    public void 虚拟上下文间隙从上一冲突末尾映射而不是倒退到下一冲突()
    {
        const string yours = "前\n左一\n左二\n左三\n短上下文\n左四\n后\n";
        const string result = "前\n<<<<<<< HEAD\n左一\n左二\n左三\n=======\n右一\n>>>>>>> feature\n"
            + "上下文一\n上下文二\n上下文三\n<<<<<<< HEAD\n左四\n=======\n右二\n>>>>>>> feature\n后\n";
        const string theirs = "前\n右一\n上下文一\n上下文二\n上下文三\n右二\n后\n";
        // 中央显示行 5 是第一处冲突之后的首行，左侧应落在自己的短上下文。
        var mapped = NativeConflictResolverDialog.MapConflictScrollLineForTest(yours, result, theirs, 1, 5);
        Assert.AreEqual(4, mapped.YoursLine);
        Assert.AreEqual(5, mapped.ResultLine);
        Assert.AreEqual(2, mapped.TheirsLine);
    }
}
