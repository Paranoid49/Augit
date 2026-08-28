using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Win32SmokeTests
{
    [TestMethod]
    public async Task 主窗口可在Sta线程启动并正常退出()
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> stages = new();
        Thread thread = new(() => RunWindowSmoke(temporary.GetPath("settings.json"), completion, stages));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception;
        try
        {
            exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (TimeoutException)
        {
            Assert.Fail(string.Join(" -> ", stages));
            return;
        }
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public void 启动路径优先使用命令行目录()
    {
        string commandLinePath = Path.GetFullPath("command-line");
        string restoredPath = Path.GetFullPath("restored");

        string? actual = Program.ResolveRequestedWorkspace([commandLinePath], restoredPath);

        Assert.AreEqual(commandLinePath, actual);
    }

    [TestMethod]
    public async Task 原生窗口可打开工作区和只读文本标签()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string documentPath = Path.Combine(workspace, "说明.txt");
        await File.WriteAllTextAsync(documentPath, "第一行\n第二行");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunWorkspaceSmoke(temporary.GetPath("settings.json"), workspace, documentPath, completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task Git改动面板按需加载并显示未跟踪文件()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await File.WriteAllTextAsync(Path.Combine(workspace, "untracked.txt"), "内容\n");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunGitPanelSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(12));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 一万行Diff加载期间窗口保持响应()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await RunGitAsync(runtime, workspace, "config", "user.name", "Augit Tests");
        await RunGitAsync(runtime, workspace, "config", "user.email", "augit-tests@example.invalid");
        string relativePath = "large-diff.txt";
        string fullPath = Path.Combine(workspace, relativePath);
        StringBuilder content = new();
        for (int index = 0; index < 10_000; index++)
        {
            _ = content.Append("修改前 ").Append(index).Append('\n');
        }

        await File.WriteAllTextAsync(fullPath, content.ToString());
        await RunGitAsync(runtime, workspace, "add", "--", relativePath);
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: large diff baseline");
        content.Clear();
        for (int index = 0; index < 10_000; index++)
        {
            _ = content.Append("修改后 ").Append(index).Append('\n');
        }

        await File.WriteAllTextAsync(fullPath, content.ToString());
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunLargeDiffSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            relativePath,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 阶段三历史管理弹窗文件历史和Rollback完成原生界面接线()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await RunGitAsync(runtime, workspace, "config", "user.name", "Augit Tests");
        await RunGitAsync(runtime, workspace, "config", "user.email", "augit-tests@example.invalid");
        string trackedPath = Path.Combine(workspace, "阶段三.txt");
        await File.WriteAllTextAsync(trackedPath, "基线\n");
        await RunGitAsync(runtime, workspace, "add", "--", "阶段三.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: root history");
        for (int index = 0; index < 100; index++)
        {
            await RunGitAsync(runtime, workspace, "commit", "--allow-empty", "-m", $"test: history {index}");
        }

        await File.WriteAllTextAsync(trackedPath, "待回退\n");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunStageThreeSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            trackedPath,
            runtime,
            initialized.Repository!,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(35));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
        Assert.AreEqual("基线\n", (await File.ReadAllTextAsync(trackedPath))
            .Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task 阶段四操作窗口和三栏解决器保持唯一可编辑结果区()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(null);
        Assert.IsTrue(runtime.IsAvailable, runtime.UnavailableReason);
        GitRepositoryOperationResult initialized = await new GitRepositoryService(runtime).InitializeAsync(workspace);
        Assert.IsTrue(initialized.IsSuccess, initialized.ErrorMessage);
        await RunGitAsync(runtime, workspace, "config", "user.name", "Augit Tests");
        await RunGitAsync(runtime, workspace, "config", "user.email", "augit-tests@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.txt"), "base\n");
        await RunGitAsync(runtime, workspace, "add", "--", "conflict.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: base");
        string main = await RunGitOutputAsync(runtime, workspace, "branch", "--show-current");
        await RunGitAsync(runtime, workspace, "checkout", "-b", "incoming");
        await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.txt"), "incoming\n");
        await RunGitAsync(runtime, workspace, "add", "--", "conflict.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: incoming");
        await RunGitAsync(runtime, workspace, "checkout", main.Trim());
        await File.WriteAllTextAsync(Path.Combine(workspace, "conflict.txt"), "current\n");
        await RunGitAsync(runtime, workspace, "add", "--", "conflict.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: current");
        int mergeExitCode = await RunGitForExitCodeAsync(runtime, workspace, "merge", "incoming");
        Assert.AreNotEqual(0, mergeExitCode);
        GitOperationService operationService = new(runtime);
        GitConflictService conflictService = new(runtime, operationService);
        GitConflictLoadResult loaded = await conflictService.LoadAsync(initialized.Repository!, "conflict.txt");
        Assert.IsTrue(loaded.IsSuccess, loaded.ErrorMessage);
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunStageFourSmoke(
            temporary.GetPath("settings.json"),
            initialized.Repository!,
            runtime,
            loaded.Document!,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 原生窗口可按需打开Markdown和Wic图片预览()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string markdownPath = Path.Combine(workspace, "README.md");
        string imagePath = Path.Combine(workspace, "pixel.png");
        await File.WriteAllTextAsync(markdownPath, "# 标题\n\n只读预览");
        await File.WriteAllBytesAsync(
            imagePath,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunPreviewSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            markdownPath,
            imagePath,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 内置终端按需创建并在关闭后释放Shell与WebView2()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunTerminalSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 原生文档区覆盖阶段一文件分类和摘要边界()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string validJson = Path.Combine(workspace, "valid.json");
        string invalidJson = Path.Combine(workspace, "invalid.json");
        string largeText = Path.Combine(workspace, "large.txt");
        string bmp = Path.Combine(workspace, "pixel.bmp");
        string gif = Path.Combine(workspace, "animation.gif");
        string webp = Path.Combine(workspace, "image.webp");
        string binary = Path.Combine(workspace, "file.pdf");
        string invalidUtf8 = Path.Combine(workspace, "invalid-utf8.txt");
        await File.WriteAllTextAsync(validJson, "{\"b\":1,\"a\":2}");
        await File.WriteAllTextAsync(invalidJson, "{\"a\":1,}");
        await CreateLargeTextAsync(largeText);
        await File.WriteAllBytesAsync(bmp, CreateOnePixelBmp());
        await File.WriteAllBytesAsync(gif, "GIF89a"u8.ToArray());
        await File.WriteAllBytesAsync(webp, "RIFF\0\0\0\0WEBP"u8.ToArray());
        await File.WriteAllBytesAsync(binary, "%PDF-1.7"u8.ToArray());
        await File.WriteAllBytesAsync(invalidUtf8, [0xC3, 0x28]);

        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunClassificationSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            validJson,
            invalidJson,
            largeText,
            bmp,
            gif,
            webp,
            binary,
            invalidUtf8,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task Jpeg通过文档边界并由Wic解码()
    {
        using TemporaryDirectory temporary = new();
        string imagePath = temporary.GetPath("pixel.jpg");
        await File.WriteAllBytesAsync(
            imagePath,
            Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////" +
                "2wBDAf//////////////////////////////////////////////////////////////////////////////////////" +
                "wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/" +
                "9oADAMBAAIQAxAAAAF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABBQJ//8QAFBEBAAAAAAAAAAAAAAAA" +
                "AAAAAP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAA" +
                "AAAAAAP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPyF//9oADAMBAAIAAwAAABD/xA" +
                "AUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EB//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EB//xA" +
                "AUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EB//2Q=="));

        DocumentReadResult result = await ReadOnlyDocumentService.ReadAsync(temporary.FullPath, imagePath);
        using WicBitmap bitmap = WicBitmapLoader.Load(imagePath);

        Assert.AreEqual(DocumentReadStatus.ImageReady, result.Status);
        Assert.AreEqual(DocumentKind.Jpeg, result.Classification.Kind);
        Assert.AreEqual(1, bitmap.Width);
        Assert.AreEqual(1, bitmap.Height);
    }

    private static void RunWindowSmoke(
        string settingsPath,
        TaskCompletionSource<Exception?> completion,
        ConcurrentQueue<string> stages)
    {
        try
        {
            stages.Enqueue("创建窗口");
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            stages.Enqueue("显示窗口");
            window.Show();
            if (window.Handle == 0 || !NativeMethods.IsWindow(window.Handle) || !window.IsVisible)
            {
                throw new InvalidOperationException("原生 Win32 主窗口未正确显示。");
            }
            stages.Enqueue("关闭窗口");
            window.Close();
            if (window.Handle != 0)
            {
                throw new InvalidOperationException("原生 Win32 主窗口未正确释放。");
            }
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunWorkspaceSmoke(
        string settingsPath,
        string workspace,
        string documentPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = OpenAndCloseAsync(window, workspace, documentPath, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunGitPanelSmoke(
        string settingsPath,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyGitPanelAndCloseAsync(window, workspace, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunLargeDiffSmoke(
        string settingsPath,
        string workspace,
        string relativePath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyLargeDiffAndCloseAsync(window, workspace, relativePath, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunStageThreeSmoke(
        string settingsPath,
        string workspace,
        string trackedPath,
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyStageThreeAndCloseAsync(
                window,
                workspace,
                trackedPath,
                runtime,
                repository,
                completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunStageFourSmoke(
        string settingsPath,
        GitRepositorySnapshot repository,
        GitRuntimeInfo runtime,
        GitConflictDocument document,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            ApplicationSettings settings = new();
            using MainWindow window = new(new SettingsStore(settingsPath), settings);
            window.Show();
            window.Post(() => _ = VerifyStageFourAndCloseAsync(
                window,
                repository,
                runtime,
                document,
                settings,
                completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task VerifyStageFourAndCloseAsync(
        MainWindow window,
        GitRepositorySnapshot repository,
        GitRuntimeInfo runtime,
        GitConflictDocument document,
        ApplicationSettings settings,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            GitOperationService operationService = new(runtime);
            GitConflictService conflictService = new(runtime, operationService);
            using NativeGitOperationDialog operationDialog = new(
                window.Handle,
                repository,
                operationService,
                conflictService,
                settings,
                _ => { });
            await operationDialog.RefreshForTestAsync();
            if (operationDialog.HandleForTest == 0
                || operationDialog.OperationKindForTest != GitOperationKind.Merge
                || operationDialog.ConflictCountForTest != 1)
            {
                throw new InvalidOperationException("阶段四操作窗口没有展示真实 Merge 冲突会话。");
            }

            using NativeConflictResolverDialog resolver = new(
                operationDialog.HandleForTest,
                repository,
                conflictService,
                document,
                settings,
                _ => { });
            if (resolver.HandleForTest == 0
                || !resolver.YoursIsReadOnlyForTest
                || resolver.ResultIsReadOnlyForTest
                || !resolver.TheirsIsReadOnlyForTest)
            {
                throw new InvalidOperationException("三栏解决器没有保持左右只读且仅中间结果可编辑。");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            string conflictPath = Path.Combine(repository.RepositoryRoot!, "conflict.txt");
            await File.WriteAllTextAsync(conflictPath, "外部工具更新\n");
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (resolver.ResultTextForTest?.Contains("外部工具更新", StringComparison.Ordinal) != true
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            stopwatch.Stop();
            if (resolver.ResultTextForTest?.Contains("外部工具更新", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException("三栏解决器没有同步外部文件变化。");
            }

            if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                throw new InvalidOperationException(
                    $"三栏解决器同步外部文件耗时 {stopwatch.Elapsed.TotalMilliseconds:F2} 毫秒，超过 500 毫秒。");
            }

            resolver.CloseForTest();
            await RunGitAsync(runtime, repository.RepositoryRoot!, "add", "--", "conflict.txt");
            stopwatch.Restart();
            deadline = DateTime.UtcNow.AddSeconds(2);
            while (operationDialog.ConflictCountForTest != 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            stopwatch.Stop();
            if (operationDialog.ConflictCountForTest != 0)
            {
                throw new InvalidOperationException("阶段四操作窗口没有同步外部 Git 解决状态。");
            }

            if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                throw new InvalidOperationException(
                    $"阶段四操作窗口同步外部 Git 状态耗时 {stopwatch.Elapsed.TotalMilliseconds:F2} 毫秒，超过 500 毫秒。");
            }

            operationDialog.CloseForTest();
            window.Close();
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            window.Close();
            completion.TrySetResult(exception);
        }
    }

    private static async Task VerifyStageThreeAndCloseAsync(
        MainWindow window,
        string workspace,
        string trackedPath,
        GitRuntimeInfo runtime,
        GitRepositorySnapshot repository,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("阶段三界面测试工作区未能打开。");
            }

            window.ShowHistoryForTest();
            DateTime historyDeadline = DateTime.UtcNow.AddSeconds(8);
            while ((window.HistoryRepositoryKindForTest != GitRepositoryKind.WorkingTree
                    || window.HistoryEntryCountForTest != 100)
                && DateTime.UtcNow < historyDeadline)
            {
                await Task.Delay(25);
            }

            NativeGitHistoryPanel history = window.HistoryPanelForTest
                ?? throw new InvalidOperationException("历史面板未按需创建。");
            if (!window.HistoryRuntimeAvailableForTest
                || window.HistoryRepositoryKindForTest != GitRepositoryKind.WorkingTree)
            {
                throw new InvalidOperationException("历史面板未识别 Git 工作区。");
            }

            if (window.HistoryEntryCountForTest != 100 || !history.HasNextPageForTest)
            {
                throw new InvalidOperationException("历史面板没有按每页 100 条分页读取。");
            }

            await history.NextPageForTestAsync();
            if (history.CurrentPageForTest != 1 || history.EntryCount != 1)
            {
                throw new InvalidOperationException("历史面板下一页结果不正确。");
            }

            await history.SelectCommitForTestAsync(0);
            if (history.ChangedFileCountForTest != 1)
            {
                throw new InvalidOperationException("根提交详情没有展示变化文件。");
            }

            await history.SelectCommitFileForTestAsync(0);
            if (!history.DisplayedDiffTextForTest.Contains("阶段三.txt", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("根提交文件 diff 没有显示中文路径。");
            }

            window.ShowFileHistoryForTest(trackedPath);
            DateTime filterDeadline = DateTime.UtcNow.AddSeconds(5);
            while (history.OperationRunningForTest && DateTime.UtcNow < filterDeadline)
            {
                await Task.Delay(20);
            }

            if (history.CurrentPageForTest != 0
                || history.FileFilterForTest != "阶段三.txt"
                || history.EntryCount != 1)
            {
                throw new InvalidOperationException("文件树文件历史入口没有应用文件路径筛选。");
            }

            if (!history.ManagementButtonsCreatedForTest)
            {
                throw new InvalidOperationException("历史面板缺少阶段三管理入口。");
            }

            await ShowAndCloseDialogAsync(
                UiText.ReferenceManagement,
                () => NativeGitReferenceDialog.Show(
                    window.Handle,
                    repository,
                    new GitReferenceService(runtime),
                    new(),
                    _ => { }));
            await ShowAndCloseDialogAsync(
                UiText.LocalStateManagement,
                () => NativeGitLocalStateDialog.Show(
                    window.Handle,
                    repository,
                    new GitWorkspaceStateService(runtime),
                    new(),
                    _ => { }));
            await ShowAndCloseDialogAsync(
                UiText.WorktreeManagement,
                () => NativeGitWorktreeDialog.Show(
                    window.Handle,
                    repository,
                    new GitWorktreeService(runtime),
                    new(),
                    _ => { }));

            window.ShowGitForTest();
            DateTime gitDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitChangedFileCountForTest != 1 && DateTime.UtcNow < gitDeadline)
            {
                await Task.Delay(25);
            }

            if (!window.RollbackButtonCreatedForTest
                || !await window.SelectGitFileForTestAsync("阶段三.txt"))
            {
                throw new InvalidOperationException("Changes 面板没有完成 Rollback 接线。");
            }

            await window.RollbackSelectedForTestAsync();
            DateTime rollbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitChangedFileCountForTest != 0 && DateTime.UtcNow < rollbackDeadline)
            {
                await Task.Delay(20);
            }

            if (window.GitChangedFileCountForTest != 0)
            {
                throw new InvalidOperationException("界面触发 Rollback 后 Git 状态没有恢复干净。");
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task ShowAndCloseDialogAsync(string title, Action show)
    {
        Task close = Task.Run(async () =>
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                nint handle = FindWindow(null, title);
                if (handle != 0)
                {
                    _ = NativeMethods.PostMessage(handle, NativeMethods.WindowMessageClose, 0, 0);
                    return;
                }

                await Task.Delay(20);
            }

            throw new InvalidOperationException($"未找到原生弹窗：{title}");
        });
        show();
        await close;
    }

    private static async Task RunGitAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = runtime.ExecutablePath!,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动测试 Git 进程。");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"测试 Git 命令失败：{error}{output}");
        }
    }

    private static async Task<string> RunGitOutputAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(runtime, workingDirectory, arguments);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动测试 Git 进程。");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"测试 Git 命令失败：{error}{output}");
        }

        return output;
    }

    private static async Task<int> RunGitForExitCodeAsync(
        GitRuntimeInfo runtime,
        string workingDirectory,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = CreateGitStartInfo(runtime, workingDirectory, arguments);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动测试 Git 进程。");
        _ = await process.StandardOutput.ReadToEndAsync();
        _ = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateGitStartInfo(
        GitRuntimeInfo runtime,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = runtime.ExecutablePath!,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    private static async Task VerifyGitPanelAndCloseAsync(
        MainWindow window,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("Git 面板测试工作区未能打开。");
            }

            window.ShowGitForTest();
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.GitRepositoryKindForTest != GitRepositoryKind.WorkingTree
                    || window.GitChangedFileCountForTest == 0)
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            if (!window.GitRuntimeAvailableForTest)
            {
                throw new InvalidOperationException("Git 面板未能加载受支持的本机 Git。");
            }

            if (window.GitRepositoryKindForTest != GitRepositoryKind.WorkingTree)
            {
                throw new InvalidOperationException("Git 面板未识别工作区仓库。");
            }

            if (window.GitChangedFileCountForTest != 1)
            {
                throw new InvalidOperationException(
                    $"Git 面板改动数量为 {window.GitChangedFileCountForTest}，预期为 1。");
            }

            if (!window.AdvancedOperationsButtonCreatedForTest)
            {
                throw new InvalidOperationException("Git 面板没有创建阶段四高级操作入口。");
            }

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await File.WriteAllTextAsync(Path.Combine(workspace, "external.txt"), "外部变化\n");
            DateTime refreshDeadline = DateTime.UtcNow.AddSeconds(2);
            while (window.GitChangedFileCountForTest != 2 && DateTime.UtcNow < refreshDeadline)
            {
                await Task.Delay(10);
            }

            stopwatch.Stop();
            if (window.GitChangedFileCountForTest != 2)
            {
                throw new InvalidOperationException("Git 面板没有同步外部文件变化。");
            }

            if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                throw new InvalidOperationException(
                    $"Git 状态变化同步耗时 {stopwatch.Elapsed.TotalMilliseconds:F2} 毫秒，超过 500 毫秒。");
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task VerifyLargeDiffAndCloseAsync(
        MainWindow window,
        string workspace,
        string relativePath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("一万行 diff 测试工作区未能打开。");
            }

            window.ShowGitForTest();
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitChangedFileCountForTest != 1 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            if (window.GitChangedFileCountForTest != 1)
            {
                throw new InvalidOperationException("一万行 diff 测试改动未能加载。");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task<bool> selection = window.SelectGitFileForTestAsync(relativePath);
            Task<(int Timeouts, double MaximumMilliseconds)> probe = Task.Run(async () =>
            {
                int timeouts = 0;
                double maximumMilliseconds = 0;
                do
                {
                    Stopwatch response = Stopwatch.StartNew();
                    nint sent = SendMessageTimeout(
                        window.Handle,
                        0,
                        0,
                        0,
                        2,
                        250,
                        out _);
                    response.Stop();
                    maximumMilliseconds = Math.Max(maximumMilliseconds, response.Elapsed.TotalMilliseconds);
                    if (sent == 0)
                    {
                        timeouts++;
                    }

                    if (!selection.IsCompleted)
                    {
                        await Task.Delay(25);
                    }
                }
                while (!selection.IsCompleted);
                return (timeouts, maximumMilliseconds);
            });
            bool selected = await selection;
            (int timeouts, double maximumMilliseconds) = await probe;
            stopwatch.Stop();
            if (!selected)
            {
                throw new InvalidOperationException("一万行 diff 未能完成加载与显示。");
            }

            if (timeouts != 0)
            {
                throw new InvalidOperationException($"一万行 diff 加载期间窗口消息探针超时 {timeouts} 次。");
            }

            Console.WriteLine(
                $"一万行 diff 在 {stopwatch.Elapsed.TotalMilliseconds:F2} 毫秒内加载，窗口消息探针最大响应 {maximumMilliseconds:F2} 毫秒。");
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task OpenAndCloseAsync(
        MainWindow window,
        string workspace,
        string documentPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            bool opened = await window.OpenWorkspaceAsync(workspace);
            if (!opened || !workspace.Equals(window.WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("原生窗口未能打开工作区。");
            }

            await window.OpenDocumentForTestAsync(documentPath);
            if (window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("原生窗口未能创建只读文本标签。");
            }

            if (!window.ActiveDocumentIsReadOnly)
            {
                throw new InvalidOperationException("普通文本控件没有保持只读边界。");
            }

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await File.WriteAllTextAsync(documentPath, "外部更新");
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (window.ActiveDocumentText != "外部更新" && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            stopwatch.Stop();
            if (window.ActiveDocumentText != "外部更新")
            {
                throw new InvalidOperationException("外部文件变化没有同步到已打开标签。");
            }
            if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                throw new InvalidOperationException($"外部文件变化同步耗时 {stopwatch.Elapsed.TotalMilliseconds:F2} 毫秒，超过 500 毫秒。");
            }

            window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "说明");
            await WaitForSearchResultAsync(window);
            window.CloseSearchForTest();
            window.ShowSearchForTest(WorkspaceSearchMode.Text, "外部更新");
            await WaitForSearchResultAsync(window);
            window.CloseSearchForTest();

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static void RunPreviewSmoke(
        string settingsPath,
        string workspace,
        string markdownPath,
        string imagePath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = OpenPreviewsAndCloseAsync(window, workspace, markdownPath, imagePath, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunTerminalSmoke(
        string settingsPath,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            ApplicationSettings settings = new()
            {
                TerminalShell = TerminalShellIds.CommandPrompt,
            };
            using MainWindow window = new(new SettingsStore(settingsPath), settings);
            window.Show();
            window.Post(() => _ = OpenTerminalAndCloseAsync(window, workspace, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task OpenTerminalAndCloseAsync(
        MainWindow window,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("终端测试工作区未能打开。");
            }

            long firstClosedWorkingSet = 0;
            long tenthClosedWorkingSet = 0;
            for (int cycle = 0; cycle < 10; cycle++)
            {
                if (!await window.OpenTerminalForTestAsync())
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次内置终端未能打开。");
                }

                DateTime readyDeadline = DateTime.UtcNow.AddSeconds(10);
                while ((!window.TerminalCreatedForTest
                        || !window.TerminalRunningForTest
                        || window.TerminalBrowserProcessIdForTest <= 0
                        || window.TerminalShellProcessIdForTest <= 0)
                    && DateTime.UtcNow < readyDeadline)
                {
                    await Task.Delay(25);
                }

                int browserProcessId = window.TerminalBrowserProcessIdForTest;
                int shellProcessId = window.TerminalShellProcessIdForTest;
                if (browserProcessId <= 0 || shellProcessId <= 0 || !window.TerminalRunningForTest)
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次内置终端没有创建完整按需资源。");
                }

                if (!window.CloseTerminalForTest())
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次内置终端未能关闭。");
                }

                DateTime exitDeadline = DateTime.UtcNow.AddSeconds(5);
                while ((IsProcessRunning(browserProcessId) || IsProcessRunning(shellProcessId))
                    && DateTime.UtcNow < exitDeadline)
                {
                    await Task.Delay(25);
                }

                if (IsProcessRunning(browserProcessId) || IsProcessRunning(shellProcessId))
                {
                    throw new InvalidOperationException(
                        $"第 {cycle + 1} 次关闭终端后仍有进程：WebView2 {browserProcessId} 运行={IsProcessRunning(browserProcessId)}，Shell {shellProcessId} 运行={IsProcessRunning(shellProcessId)}。");
                }

                string terminalDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Augit",
                    "WebView2",
                    "Terminal");
                string sessionPattern = $"Session-{Environment.ProcessId}-*";
                DateTime cleanupDeadline = DateTime.UtcNow.AddSeconds(3);
                while (Directory.Exists(terminalDataRoot)
                    && Directory.EnumerateDirectories(terminalDataRoot, sessionPattern).Any()
                    && DateTime.UtcNow < cleanupDeadline)
                {
                    await Task.Delay(25);
                }

                if (Directory.Exists(terminalDataRoot)
                    && Directory.EnumerateDirectories(terminalDataRoot, sessionPattern).Any())
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次关闭终端后仍有 WebView2 会话目录。");
                }

                if (cycle is 0 or 9)
                {
                    await Task.Delay(cycle == 9 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1));
                    using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
                    current.Refresh();
                    if (cycle == 0)
                    {
                        firstClosedWorkingSet = current.WorkingSet64;
                    }
                    else
                    {
                        tenthClosedWorkingSet = current.WorkingSet64;
                    }
                }
            }

            if (tenthClosedWorkingSet - firstClosedWorkingSet > 20 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    $"终端第十次关闭后的工作集比第一次高 {(tenthClosedWorkingSet - firstClosedWorkingSet) / 1024d / 1024d:F2} MiB。");
            }

            Console.WriteLine(
                $"终端第一次关闭后工作集 {firstClosedWorkingSet / 1024d / 1024d:F2} MiB，第十次关闭并空闲十秒后 {tenthClosedWorkingSet / 1024d / 1024d:F2} MiB，增量 {(tenthClosedWorkingSet - firstClosedWorkingSet) / 1024d / 1024d:F2} MiB。");

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task OpenPreviewsAndCloseAsync(
        MainWindow window,
        string workspace,
        string markdownPath,
        string imagePath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("预览测试工作区未能打开。");
            }

            await window.OpenDocumentForTestAsync(markdownPath);
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!window.ActiveMarkdownPreviewReady && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            if (!window.ActiveMarkdownPreviewReady)
            {
                throw new InvalidOperationException(
                    $"Markdown 原生 WebView2 预览未能按需创建。{window.ActiveMarkdownPreviewError}");
            }

            long firstClosedWorkingSet = 0;
            long tenthClosedWorkingSet = 0;
            for (int cycle = 0; cycle < 10; cycle++)
            {
                int browserProcessId = cycle == 0
                    ? window.ActiveMarkdownBrowserProcessId
                    : await window.ShowActiveMarkdownPreviewForTestAsync();
                if (browserProcessId <= 0)
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次 Markdown 预览没有浏览器进程。");
                }

                int closingProcessId = window.CloseActiveMarkdownPreviewForTest();
                if (closingProcessId != browserProcessId)
                {
                    throw new InvalidOperationException("关闭的 Markdown 预览与当前浏览器进程不一致。");
                }

                DateTime exitDeadline = DateTime.UtcNow.AddSeconds(5);
                while (IsProcessRunning(browserProcessId) && DateTime.UtcNow < exitDeadline)
                {
                    await Task.Delay(25);
                }

                if (IsProcessRunning(browserProcessId))
                {
                    throw new InvalidOperationException($"第 {cycle + 1} 次关闭 Markdown 后 WebView2 浏览器进程没有退出。");
                }

                if (cycle is 0 or 9)
                {
                    await Task.Delay(cycle == 9 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1));
                    using System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
                    current.Refresh();
                    if (cycle == 0)
                    {
                        firstClosedWorkingSet = current.WorkingSet64;
                    }
                    else
                    {
                        tenthClosedWorkingSet = current.WorkingSet64;
                    }
                }
            }
            if (tenthClosedWorkingSet - firstClosedWorkingSet > 20 * 1024 * 1024)
            {
                throw new InvalidOperationException(
                    $"Markdown 第十次关闭后的工作集比第一次高 {(tenthClosedWorkingSet - firstClosedWorkingSet) / 1024d / 1024d:F2} MiB。");
            }

            Console.WriteLine(
                $"Markdown 第一次关闭后工作集 {firstClosedWorkingSet / 1024d / 1024d:F2} MiB，第十次关闭并空闲十秒后 {tenthClosedWorkingSet / 1024d / 1024d:F2} MiB，增量 {(tenthClosedWorkingSet - firstClosedWorkingSet) / 1024d / 1024d:F2} MiB。");

            await window.OpenDocumentForTestAsync(imagePath);
            if (!window.ActiveImagePreviewReady)
            {
                throw new InvalidOperationException("Windows Imaging Component 图片预览未能创建。");
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task CreateLargeTextAsync(string path)
    {
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] header = Enumerable.Repeat((byte)'a', 64 * 1024).ToArray();
        await stream.WriteAsync(header);
        stream.SetLength(DocumentLimits.MaximumTextBytes + 1);
    }

    private static byte[] CreateOnePixelBmp()
    {
        byte[] bitmap = new byte[58];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        BitConverter.GetBytes(bitmap.Length).CopyTo(bitmap, 2);
        BitConverter.GetBytes(54).CopyTo(bitmap, 10);
        BitConverter.GetBytes(40).CopyTo(bitmap, 14);
        BitConverter.GetBytes(1).CopyTo(bitmap, 18);
        BitConverter.GetBytes(1).CopyTo(bitmap, 22);
        BitConverter.GetBytes((short)1).CopyTo(bitmap, 26);
        BitConverter.GetBytes((short)24).CopyTo(bitmap, 28);
        BitConverter.GetBytes(4).CopyTo(bitmap, 34);
        bitmap[54] = 0x40;
        bitmap[55] = 0x80;
        bitmap[56] = 0xFF;
        return bitmap;
    }

    private static void RunClassificationSmoke(
        string settingsPath,
        string workspace,
        string validJson,
        string invalidJson,
        string largeText,
        string bmp,
        string gif,
        string webp,
        string binary,
        string invalidUtf8,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyClassificationsAndCloseAsync(
                window,
                workspace,
                validJson,
                invalidJson,
                largeText,
                bmp,
                gif,
                webp,
                binary,
                invalidUtf8,
                completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task VerifyClassificationsAndCloseAsync(
        MainWindow window,
        string workspace,
        string validJson,
        string invalidJson,
        string largeText,
        string bmp,
        string gif,
        string webp,
        string binary,
        string invalidUtf8,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("分类测试工作区未能打开。");
            }

            await window.OpenDocumentForTestAsync(validJson);
            AssertActiveDocument(window, DocumentReadStatus.TextReady, DocumentKind.Json);
            if (!window.ActiveDocumentIsShowingAlternative)
            {
                throw new InvalidOperationException("有效 JSON 没有默认显示格式化结果。");
            }

            await window.OpenDocumentForTestAsync(invalidJson);
            AssertActiveDocument(window, DocumentReadStatus.TextReady, DocumentKind.Json);
            if (window.ActiveDocumentIsShowingAlternative)
            {
                throw new InvalidOperationException("无效 JSON 不应隐藏原文。");
            }

            await window.OpenDocumentForTestAsync(largeText);
            AssertActiveDocument(window, DocumentReadStatus.TextTooLarge, DocumentKind.Text);
            await window.OpenDocumentForTestAsync(bmp);
            AssertActiveDocument(window, DocumentReadStatus.ImageReady, DocumentKind.Bmp);
            if (!window.ActiveImagePreviewReady)
            {
                throw new InvalidOperationException("BMP 没有使用 WIC 创建预览。");
            }

            await window.OpenDocumentForTestAsync(gif);
            AssertActiveDocument(window, DocumentReadStatus.BinarySummary, DocumentKind.Gif);
            await window.OpenDocumentForTestAsync(webp);
            AssertActiveDocument(window, DocumentReadStatus.BinarySummary, DocumentKind.WebP);
            await window.OpenDocumentForTestAsync(binary);
            AssertActiveDocument(window, DocumentReadStatus.BinarySummary, DocumentKind.Binary);
            await window.OpenDocumentForTestAsync(invalidUtf8);
            AssertActiveDocument(window, DocumentReadStatus.InvalidUtf8, DocumentKind.InvalidUtf8);
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertActiveDocument(MainWindow window, DocumentReadStatus status, DocumentKind kind)
    {
        if (window.ActiveDocumentStatus != status || window.ActiveDocumentKind != kind)
        {
            throw new InvalidOperationException(
                $"文档分类不匹配，实际为 {window.ActiveDocumentStatus}/{window.ActiveDocumentKind}，预期为 {status}/{kind}。");
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitForSearchResultAsync(MainWindow window)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (window.SearchResultCountForTest == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        if (window.SearchResultCountForTest == 0)
        {
            throw new InvalidOperationException("原生搜索面板没有返回结果。");
        }
    }
}
