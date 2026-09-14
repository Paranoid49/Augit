using System.Reflection;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeStatusBarTests
{
    internal static void AssertFields(MainWindow window, params string[] expected) => CollectionAssert.AreEqual(expected, window.StatusFieldsForTest);

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public Task 状态栏随文本图片二进制无文档切换且不继承错误格式() => RunAsync(async (window, workspace) =>
    {
        Assert.AreEqual(workspace, window.StatusPathForTest);
        Assert.IsEmpty(window.StatusFieldsForTest);
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "crlf.txt"));
        NativeStatusBarTests.AssertFields(window, "UTF-8", "CRLF", "只读");
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.png"));
        NativeStatusBarTests.AssertFields(window, "只读");
        Assert.AreEqual(Path.Combine(workspace, "sample.png"), window.StatusPathForTest);
        Assert.DoesNotContain("UTF-8", NativeMethods.GetWindowTextValue(window.StatusBarHandleForTest));
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "animation.gif"));
        NativeStatusBarTests.AssertFields(window, "只读");
        Assert.AreEqual(Path.Combine(workspace, "animation.gif"), window.StatusPathForTest);
        while (window.OpenDocumentCount > 0) window.CloseActiveTabForTest();
        Assert.AreEqual(workspace, window.StatusPathForTest);
        Assert.IsEmpty(window.StatusFieldsForTest);
    });

    [TestMethod]
    public Task 状态栏使用磁盘换行格式且JSON显示切换不改格式信息() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "sample.json"));
        Assert.IsTrue(window.ActiveDocumentViewForTest!.IsShowingAlternative);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "CRLF", "只读");
        _ = NativeMethods.SendMessage(window.ActiveDocumentViewForTest.Handle, NativeMethods.WindowMessageCommand, 1, 0);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "CRLF", "只读");
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "mixed.txt"));
        NativeStatusBarTests.AssertFields(window, "UTF-8", "混合换行", "只读");
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "none.txt"));
        NativeStatusBarTests.AssertFields(window, "UTF-8", "无换行", "只读");
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "invalid.txt"));
        NativeStatusBarTests.AssertFields(window, "只读");
        StringAssert.Contains(window.StatusTextForTest, "不是有效的 UTF-8");
    });

    [TestMethod]
    public Task 刷新活动文本更新格式而后台刷新不改变状态栏或新提示() => RunAsync(async (window, workspace) =>
    {
        string first = Path.Combine(workspace, "crlf.txt");
        await window.OpenDocumentForTestAsync(first);
        NativeDocumentView view = window.ActiveDocumentViewForTest!;
        window.ShowActiveDocumentFindForTest("保留输入");
        nint focus = NativeMethods.GetFocus();
        int layouts = window.LayoutInvocationCountForTest;
        window.SetStatusForTest(UiText.PathCopied);
        DocumentReadResult update = new(DocumentReadStatus.TextReady, first, first,
            new(DocumentKind.Text, "UTF-8 文本"), 3, "一\r二\r", null, null, string.Empty)
        { LineEndings = DocumentLineEndings.Cr };
        await view.ReloadAsync(update);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "CR", "只读");
        Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        Assert.AreEqual(focus, NativeMethods.GetFocus());
        Assert.AreEqual(layouts, window.LayoutInvocationCountForTest);
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "none.txt"));
        window.SetStatusForTest(UiText.PathCopied);
        int refreshes = window.StatusBarUpdateCountForTest;
        await view.ReloadAsync(update with { LineEndings = DocumentLineEndings.Mixed });
        NativeStatusBarTests.AssertFields(window, "UTF-8", "无换行", "只读");
        Assert.AreEqual(refreshes, window.StatusBarUpdateCountForTest);
        window.SetStatusForTest(UiText.PathCopied);
        Assert.AreEqual(refreshes, window.StatusBarUpdateCountForTest, "重复相同状态不重写控件或悬停文本。");
    });

    [TestMethod]
    public Task 慢读取显示请求路径且后台晚到不覆盖当前格式() => RunAsync(async (window, workspace) =>
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.DocumentReadBarrierForTest = gate.Task;
        Task pending = window.OpenDocumentForTestAsync(Path.Combine(workspace, "crlf.txt"));
        try
        {
            Assert.AreEqual(Path.Combine(workspace, "crlf.txt"), window.StatusPathForTest);
            NativeStatusBarTests.AssertFields(window, "只读");
            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "mixed.txt"));
            window.SetStatusForTest(UiText.PathCopied);
            gate.SetResult();
            await pending;
            Assert.AreEqual(Path.Combine(workspace, "mixed.txt"), window.StatusPathForTest);
            NativeStatusBarTests.AssertFields(window, "UTF-8", "混合换行", "只读");
            Assert.AreEqual(UiText.PathCopied, window.StatusTextForTest);
        }
        finally { gate.TrySetResult(); await pending; }
    });

    [TestMethod]
    public Task 历史比较显示比较路径并在关闭后恢复普通文档() => RunAsync(async (window, workspace) =>
    {
        string original = Path.Combine(workspace, "crlf.txt");
        await window.OpenDocumentForTestAsync(original);
        GitComparisonDocument document = new(GitDiffContentStatus.Ready, "HEAD^", "HEAD", "src/change.cs",
            "diff --git a/src/change.cs b/src/change.cs\n--- a/src/change.cs\n+++ b/src/change.cs\n@@ -1 +1 @@\n-a\n+b\n");
        window.ShowHistoryComparisonForTest(GitComparisonResult.Success(document));
        Assert.AreEqual(Path.GetFullPath(Path.Combine(workspace, "src/change.cs")), window.StatusPathForTest);
        NativeStatusBarTests.AssertFields(window, "只读");
        window.CloseActiveTabForTest();
        Assert.AreEqual(original, window.StatusPathForTest);
        NativeStatusBarTests.AssertFields(window, "UTF-8", "CRLF", "只读");
    });

    [TestMethod]
    public Task 切换工作区清除上一个文件的路径和格式() => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "crlf.txt"));
        string next = Path.Combine(workspace, "next-workspace");
        Directory.CreateDirectory(next);
        Assert.IsTrue(await window.OpenWorkspaceAsync(next));
        Assert.AreEqual(next, window.StatusPathForTest);
        Assert.IsEmpty(window.StatusFieldsForTest);
    });

    public static IEnumerable<object[]> GeometryCases()
    {
        foreach (int dpi in new[] { 96, 120, 144 }) foreach (int size in new[] { 13, 40 }) foreach (string theme in new[] { "Light", "Dark" })
            yield return [dpi, size, theme];
    }

    [TestMethod]
    [DynamicData(nameof(GeometryCases))]
    public Task 大字号Dpi与取消按钮保留格式和路径区域(int dpi, int size, string theme) => RunAsync(async (window, workspace) =>
    {
        await window.OpenDocumentForTestAsync(Path.Combine(workspace, "mixed.txt"));
        Assert.IsTrue(NativeMethods.GetClientRectangle(window.StatusBarHandleForTest, out var bounds));
        int width = (int)typeof(MainWindow).GetField("_statusFormatWidth", Fields)!.GetValue(window)!;
        int[] fields = (int[])typeof(MainWindow).GetField("_statusFieldWidths", Fields)!.GetValue(window)!;
        Assert.AreEqual(3, fields.Count(value => value > 0));
        Assert.AreEqual(fields.Sum() + 3 * NativeTheme.Scale(12), width);
        Assert.IsGreaterThan(NativeTheme.Scale(200), bounds.Right - width, "最小窗口必须保留路径空间。");
        using CancellationTokenSource operation = new();
        typeof(MainWindow).GetField("_branchOperationCancellation", Fields)!.SetValue(window, operation);
        try
        {
            window.SetStatusForTest("分支操作进行中");
            nint cancel = (nint)typeof(MainWindow).GetField("_cancelBranchButton", Fields)!.GetValue(window)!;
            Assert.IsTrue(NativeMethods.GetWindowRectangle(cancel, out var button));
            Assert.IsTrue(NativeMethods.GetWindowRectangle(window.StatusBarHandleForTest, out var status));
            Assert.IsLessThanOrEqualTo(status.Right - width, button.Right, "取消按钮不能覆盖格式字段。");
            Assert.IsGreaterThanOrEqualTo(status.Top, button.Top);
            Assert.IsLessThanOrEqualTo(status.Bottom, button.Bottom);
        }
        finally { typeof(MainWindow).GetField("_branchOperationCancellation", Fields)!.SetValue(window, null); }
    }, dpi, size, theme);

    private static async Task RunAsync(Func<MainWindow, string, Task> scenario, int dpi = 96, int size = 13, string theme = "Light")
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "crlf.txt"), "一\r\n二\r\n");
        await File.WriteAllTextAsync(Path.Combine(workspace, "mixed.txt"), "一\r\n二\n");
        await File.WriteAllTextAsync(Path.Combine(workspace, "none.txt"), "无换行");
        await File.WriteAllTextAsync(Path.Combine(workspace, "sample.json"), "{\r\n\"a\": 1\r\n}");
        await File.WriteAllBytesAsync(Path.Combine(workspace, "invalid.txt"), [0xc3, 0x28]);
        await File.WriteAllBytesAsync(Path.Combine(workspace, "animation.gif"), "GIF89a"u8.ToArray());
        File.Copy(NativeImagePreviewTests.SamplePath(), Path.Combine(workspace, "sample.png"));
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? active = null;
        Thread thread = new(() =>
        {
            string font = NativeTheme.UiFontFamilyForTest;
            double previousSize = NativeTheme.UiFontSizeForTest;
            Exception? failure = null;
            try
            {
                using IDisposable scale = NativeTheme.PushVisualAuditDpiOverride(dpi);
                using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")),
                    new() { Theme = theme, TextFontSize = size, Window = new() { Width = 1024, Height = 640 } });
                Volatile.Write(ref active, window);
                window.Show();
                window.Post(() => _ = VerifyAsync());
                _ = MainWindow.RunMessageLoop();
                async Task VerifyAsync()
                {
                    try { Assert.IsTrue(await window.OpenWorkspaceAsync(workspace)); await scenario(window, workspace); }
                    catch (Exception exception) { failure = exception; }
                    finally { window.Close(); }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                Volatile.Write(ref active, null);
                NativeTheme.ConfigureUiTypography(font, previousSize);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally
        {
            MainWindow? remaining = Volatile.Read(ref active);
            remaining?.Post(remaining.Close);
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "状态栏测试线程未退出。");
        }
    }
}
