using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Documents;
using Augit.Core.Git;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Win32SmokeTests
{
    private static readonly string[] MainMenuLabels = ["文件", "视图", "Git", "终端", "设置"];

    private static readonly string[] RestoredTopBarLabels =
        [UiText.AppName, "Git", UiText.CurrentFile, string.Empty, string.Empty];

    [TestMethod]
    public void 项目右键菜单按视觉稿保留文件历史和Blame()
    {
        CollectionAssert.AreEqual(
            new List<string>
            {
                "复制路径",
                "在资源管理器中定位",
                "在外部终端打开",
                "刷新",
                "文件历史",
                "Blame",
            },
            MainWindow.ProjectContextMenuLabelsForTest(directory: false).ToArray());
    }

    [TestMethod]
    public void 主窗口实例登记前即禁用系统非客户区()
    {
        Assert.AreEqual(
            (nint)0,
            MainWindow.ResolveCustomNonClientMessage(NativeMethods.WindowMessageNonClientCalculateSize));
        Assert.AreEqual(
            (nint)0,
            MainWindow.ResolveCustomNonClientMessage(NativeMethods.WindowMessageNonClientPaint));
        Assert.AreEqual(
            (nint)1,
            MainWindow.ResolveCustomNonClientMessage(NativeMethods.WindowMessageNonClientActivate));
        Assert.IsNull(MainWindow.ResolveCustomNonClientMessage(NativeMethods.WindowMessagePaint));
    }

    [TestMethod]
    public void 主窗口重复关闭与释放只执行一次销毁链路()
    {
        using TemporaryDirectory temporary = new();
        MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        try
        {
            window.Show();
            window.Close();

            Assert.IsTrue(window.DestroyedForTest, "主窗口关闭后必须完成唯一销毁链路。");
            Assert.AreEqual((nint)0, window.Handle, "主窗口销毁后不能保留失效句柄。");

            window.Close();
            window.Dispose();
            Assert.AreEqual((nint)0, window.Handle, "重复关闭或释放不能重新创建主窗口句柄。");
        }
        finally
        {
            window.Dispose();
        }
    }

    [TestMethod]
    public void 汉堡按钮以内嵌标题栏菜单替换原入口并可恢复()
    {
        using TemporaryDirectory temporary = new();
        using MainWindow window = new(new SettingsStore(temporary.GetPath("settings.json")), new());
        window.Show();

        window.ShowMainMenuForTest();
        CollectionAssert.AreEqual(
            MainMenuLabels,
            window.MainMenuLabelsForTest.ToArray());
        Assert.IsTrue(window.MainMenuOpenForTest);
        Assert.IsTrue(window.MainMenuUsesInlineButtonsForTest);

        Assert.IsTrue(window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape));
        Assert.IsFalse(window.MainMenuOpenForTest);
        CollectionAssert.AreEqual(
            RestoredTopBarLabels,
            window.MainMenuLabelsForTest.ToArray());
    }

    [TestMethod]
    public void 标题栏工作区入口使用当前目录名且空态保留产品名()
    {
        Assert.AreEqual(UiText.AppName, MainWindow.ResolveWorkspaceDisplayNameForTest(null));
        Assert.AreEqual(UiText.AppName, MainWindow.ResolveWorkspaceDisplayNameForTest(string.Empty));
        Assert.AreEqual("复原项目", MainWindow.ResolveWorkspaceDisplayNameForTest("D:\\github\\复原项目"));
        Assert.AreEqual("Augit", MainWindow.ResolveWorkspaceDisplayNameForTest("D:\\github\\Augit\\"));
    }

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
    public async Task Git局部通知在进行中显示取消并在结果态隐藏()
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunOperationNotificationSmoke(
            temporary.GetPath("settings.json"),
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task Git不可用通知保留文件浏览并提供配置入口()
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunGitUnavailableSmoke(
            temporary.GetPath("settings.json"),
            temporary.FullPath,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 内置终端启动失败使用局部通知并保留当前工作区()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunTerminalFailureSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 后台再次激活不会破坏最大化窗口状态()
    {
        using TemporaryDirectory temporary = new();
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunWindowActivationSmoke(temporary.GetPath("settings.json"), completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
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
        await File.WriteAllTextAsync(Path.Combine(workspace, "第二份.txt"), "第二份内容");
        await File.WriteAllTextAsync(Path.Combine(workspace, "连续预览-A.txt"), "连续预览 A");
        await File.WriteAllTextAsync(Path.Combine(workspace, "连续预览-B.txt"), "连续预览 B");
        await File.WriteAllTextAsync(Path.Combine(workspace, "连续预览-C.txt"), "连续预览 C");
        string directoryPath = Path.Combine(workspace, "目录");
        Directory.CreateDirectory(directoryPath);
        await File.WriteAllTextAsync(Path.Combine(directoryPath, "子文件.txt"), "子文件内容");
        string settingsPath = temporary.GetPath("settings.json");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> stages = new();
        Thread thread = new(() => RunWorkspaceSmoke(
            settingsPath,
            workspace,
            documentPath,
            completion,
            stages));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception;
        try
        {
            exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            Assert.Fail(string.Join(" -> ", stages));
            return;
        }
        bool stopped = thread.Join(TimeSpan.FromSeconds(2));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
        ApplicationSettings savedSettings = await new SettingsStore(settingsPath).LoadAsync();
        Assert.IsNotNull(savedSettings.ToolWindows.ProjectPanelWidth);
    }

    [TestMethod]
    public async Task 恢复工作区后文件树选中活动标签()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string docs = Path.Combine(workspace, "docs");
        Directory.CreateDirectory(docs);
        string documentPath = Path.Combine(docs, "roadmap.md");
        await File.WriteAllTextAsync(documentPath, "# 路线图\n");
        string firstRestoredPath = Path.Combine(docs, "第一份.txt");
        string secondRestoredPath = Path.Combine(docs, "第二份.txt");
        await File.WriteAllTextAsync(firstRestoredPath, "第一份\n");
        await File.WriteAllTextAsync(secondRestoredPath, "第二份\n");
        for (int index = 0; index < 500; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(docs, $"文档-{index:D3}.txt"),
                index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        string[] overflowRestoredPaths = Enumerable.Range(0, 24)
            .Select(index => Path.Combine(docs, $"文档-{index:D3}.txt"))
            .ToArray();

        ApplicationSettings settings = new()
        {
            LastWorkspace = workspace,
            OpenFiles = [firstRestoredPath, documentPath, .. overflowRestoredPaths, secondRestoredPath],
            ActiveFile = secondRestoredPath,
            ExpandedDirectories = [workspace, docs],
        };
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunWorkspaceRestoreSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            secondRestoredPath,
            settings,
            completion));
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
        await RunGitAsync(runtime, workspace, "config", "user.name", "Augit Tests");
        await RunGitAsync(runtime, workspace, "config", "user.email", "augit-tests@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(workspace, "tracked.txt"), "基线内容\n");
        await RunGitAsync(runtime, workspace, "add", "--", "tracked.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: changes list interaction baseline");
        await RunGitAsync(runtime, workspace, "branch", "feature");
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
    public async Task 从历史切换到Commit后底部面板仍位于编辑器前方()
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
        string trackedPath = Path.Combine(workspace, "tracked.txt");
        await File.WriteAllTextAsync(trackedPath, "基线\n");
        await RunGitAsync(runtime, workspace, "add", "--", "tracked.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: bottom panel z order");
        await File.WriteAllTextAsync(trackedPath, "修改\n");

        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunBottomPanelSwitchSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task Commit工具栏入口具有真实命令启用条件和悬停说明()
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
        string trackedPath = Path.Combine(workspace, "tracked.txt");
        await File.WriteAllTextAsync(trackedPath, "基线内容\n");
        await RunGitAsync(runtime, workspace, "add", "--", "tracked.txt");
        await RunGitAsync(runtime, workspace, "commit", "-m", "test: commit toolbar baseline");
        await File.WriteAllTextAsync(trackedPath, "待回滚内容\n");
        string previewPath = Path.Combine(workspace, "preview.md");
        await File.WriteAllTextAsync(previewPath, "# 预览\n");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> stages = new();
        Thread thread = new(() => RunCommitToolbarSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            trackedPath,
            previewPath,
            completion,
            stages));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception;
        try
        {
            exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            Assert.Fail(string.Join(" -> ", stages));
            return;
        }
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
        Assert.AreEqual(
            "基线内容\n",
            (await File.ReadAllTextAsync(trackedPath)).Replace("\r\n", "\n", StringComparison.Ordinal));
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
        string settingsPath = temporary.GetPath("settings.json");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string progress = "创建原生窗口";
        Thread thread = new(() => RunStageThreeSmoke(
            settingsPath,
            workspace,
            trackedPath,
            runtime,
            initialized.Repository!,
            completion,
            step => progress = step));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception;
        try
        {
            exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException timeout)
        {
            throw new TimeoutException($"阶段三原生烟雾测试停在“{progress}”。", timeout);
        }
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
        ApplicationSettings savedSettings = await new SettingsStore(settingsPath).LoadAsync();
        Assert.IsNotNull(savedSettings.ToolWindows.BottomPanelHeight);
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
    public async Task 快速切换Markdown标签只完成当前预览并保留用户模式()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string firstPath = Path.Combine(workspace, "first.md");
        string secondPath = Path.Combine(workspace, "second.md");
        string thirdPath = Path.Combine(workspace, "third.md");
        await File.WriteAllTextAsync(firstPath, "# 第一个文档\n\n快速切换");
        await File.WriteAllTextAsync(secondPath, "# 第二个文档\n\n快速切换");
        await File.WriteAllTextAsync(thirdPath, "# 第三个文档\n\n当前预览");
        TaskCompletionSource<Exception?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunRapidPreviewSwitchSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            firstPath,
            secondPath,
            thirdPath,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Exception? exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(exception, exception?.ToString());
        Assert.IsTrue(stopped);
    }

    [TestMethod]
    public async Task 窗口关闭时释放仍在显示的Markdown浏览器进程()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        Directory.CreateDirectory(workspace);
        string markdownPath = Path.Combine(workspace, "README.md");
        await File.WriteAllTextAsync(markdownPath, "# 标题\n\n关闭窗口时释放预览");
        TaskCompletionSource<(Exception? Error, int BrowserProcessId)> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() => RunActivePreviewWindowCloseSmoke(
            temporary.GetPath("settings.json"),
            workspace,
            markdownPath,
            completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        (Exception? error, int browserProcessId) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        bool stopped = thread.Join(TimeSpan.FromSeconds(3));

        Assert.IsNull(error, error?.ToString());
        Assert.IsTrue(stopped);
        Assert.IsGreaterThan(0, browserProcessId);
        Assert.IsFalse(IsProcessRunning(browserProcessId), "关闭窗口后 Markdown WebView2 浏览器进程仍在运行。");
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

    [TestMethod]
    public async Task Wic转换为预乘透明Bgra并保留不透明Bmp像素颜色()
    {
        using TemporaryDirectory temporary = new();
        string imagePath = temporary.GetPath("pixel.bmp");
        await File.WriteAllBytesAsync(imagePath, CreateOnePixelBmp());

        using WicBitmap bitmap = WicBitmapLoader.Load(imagePath);
        nint deviceContext = NativeMethods.CreateCompatibleDeviceContext(0);
        Assert.AreNotEqual(0, deviceContext);
        nint previous = NativeMethods.SelectObject(deviceContext, bitmap.Handle);
        try
        {
            Assert.AreEqual(0x004080FFu, NativeMethods.GetPixel(deviceContext, 0, 0));
            Assert.AreEqual(
                new Guid("6FDDC324-4E03-4BFE-B185-3D77768DC910"),
                WicBitmapLoader.PixelFormat32PbgraForTest);
        }
        finally
        {
            _ = NativeMethods.SelectObject(deviceContext, previous);
            _ = NativeMethods.DeleteDeviceContext(deviceContext);
        }
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

    private static void RunOperationNotificationSmoke(
        string settingsPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.ShowOperationProgressForTest("正在执行 Smart Checkout…", "正在恢复临时 Stash");
            if (!window.OperationNotificationVisibleForTest
                || !window.OperationNotificationIsProgressForTest
                || !window.OperationNotificationCancelVisibleForTest
                || !window.OperationNotificationCancelParentedToCardForTest
                || !window.OperationNotificationCancelWithinCardForTest
                || !window.StatusTextForTest.Contains("Smart Checkout", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Git 进行中通知没有同时显示进度与取消入口。");
            }

            window.ShowOperationResultForTest("推送失败", "当前仓库未配置远端。", error: true);
            if (!window.OperationNotificationVisibleForTest
                || !window.OperationNotificationIsErrorForTest
                || window.OperationNotificationCancelVisibleForTest
                || !string.Equals(window.StatusTextForTest, "推送失败", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Git 结果通知没有切换为错误态或仍然显示取消入口。");
            }

            window.Close();
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunGitUnavailableSmoke(
        string settingsPath,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            ApplicationSettings settings = new()
            {
                GitExecutablePath = Path.Combine(workspace, "__missing_git_for_test.exe"),
            };
            using MainWindow window = new(new SettingsStore(settingsPath), settings);
            window.Show();
            window.Post(() => _ = VerifyGitUnavailableAndCloseAsync(window, workspace, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task VerifyGitUnavailableAndCloseAsync(
        MainWindow window,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            string trackedPath = Path.Combine(workspace, "tracked.txt");
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(trackedPath, "只读内容");
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("Git 不可用测试工作区未能打开。");
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!window.GitRuntimeUnavailableForTest && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            if (!window.GitRuntimeUnavailableForTest || !window.GitUnavailableNoticeVisibleForTest)
            {
                throw new InvalidOperationException("Git 探测失败后没有自动显示一次局部不可用通知。");
            }

            // 真实恢复顺序可能先完成 Git 探测，再晚创建文档视图；通知必须继续位于正文之上。
            await window.OpenDocumentForTestAsync(trackedPath);
            if (!window.OperationNotificationIsAboveActiveDocumentForTest)
            {
                throw new InvalidOperationException("打开文档后，Git 不可用通知被正文窗口遮挡。");
            }

            window.ShowGitUnavailableForTest("未找到 Git for Windows 2.40 或更高版本。");
            if (!window.GitRuntimeUnavailableForTest
                || !window.GitNavigationDisabledForTest
                || !window.GitUnavailableNoticeVisibleForTest
                || !window.OperationNotificationIsAboveActiveDocumentForTest
                || !string.Equals(
                    window.OperationNotificationDetailForTest,
                    "未找到 Git for Windows 2.40 或更高版本，文件浏览仍可使用。",
                    StringComparison.Ordinal)
                || !window.OperationNotificationActionVisibleForTest
                || window.OperationNotificationCancelVisibleForTest
                || !window.ProjectPanelVisibleForTest
                || window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("Git 不可用时没有保持文件浏览，或配置入口状态不正确。");
            }

            window.ShowCloneForTest();
            DateTime cloneDeadline = DateTime.UtcNow.AddSeconds(2);
            while (!string.Equals(
                    window.OperationNotificationTitleForTest,
                    UiText.CloneFailed,
                    StringComparison.Ordinal)
                && DateTime.UtcNow < cloneDeadline)
            {
                await Task.Delay(20);
            }

            if (!window.OperationNotificationIsErrorForTest
                || !string.Equals(window.OperationNotificationTitleForTest, UiText.CloneFailed, StringComparison.Ordinal)
                || !window.OperationNotificationDetailForTest.Contains("Git", StringComparison.OrdinalIgnoreCase)
                || !window.OperationNotificationIsAboveActiveDocumentForTest
                || !window.ProjectPanelVisibleForTest
                || window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("Git 不可用时点击 Clone 没有显示局部失败通知，或破坏了文件浏览上下文。");
            }

            window.CloseAllDocumentsForTest();
            if (!window.GitUnavailableNoticeVisibleForTest
                || !window.OperationNotificationIsAboveEmptyDocumentForTest)
            {
                throw new InvalidOperationException("关闭全部标签后，Git 不可用通知被空正文窗口遮挡。");
            }

            window.Close();
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunTerminalFailureSmoke(
        string settingsPath,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            ApplicationSettings settings = new()
            {
                TerminalShell = TerminalShellIds.Custom,
                TerminalCustomCommand = string.Empty,
            };
            using MainWindow window = new(new SettingsStore(settingsPath), settings);
            window.Show();
            window.Post(() => _ = VerifyTerminalFailureAndCloseAsync(window, workspace, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task VerifyTerminalFailureAndCloseAsync(
        MainWindow window,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            string documentPath = Path.Combine(workspace, "说明.txt");
            await File.WriteAllTextAsync(documentPath, "当前文档");
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("终端失败测试工作区未能打开。");
            }

            await window.OpenDocumentForTestAsync(documentPath);
            bool opened = await window.OpenTerminalForTestAsync();
            if (opened
                || window.TerminalCreatedForTest
                || !window.OperationNotificationIsErrorForTest
                || !string.Equals(
                    window.OperationNotificationTitleForTest,
                    UiText.TerminalStartFailed,
                    StringComparison.Ordinal)
                || !window.OperationNotificationDetailForTest.Contains("自定义终端启动命令", StringComparison.Ordinal)
                || !window.OperationNotificationIsAboveActiveDocumentForTest
                || !window.ActiveDocumentVisibleForTest
                || !window.ProjectPanelVisibleForTest)
            {
                throw new InvalidOperationException("终端启动失败没有使用局部通知，或破坏了当前工作区上下文。");
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

    private static void RunWindowActivationSmoke(
        string settingsPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            ApplicationSettings settings = new()
            {
                Window = new()
                {
                    Left = 40,
                    Top = 40,
                    Width = 1180,
                    Height = 760,
                    IsMaximized = true,
                },
            };
            using MainWindow window = new(new SettingsStore(settingsPath), settings);
            window.Show();
            if (!NativeMethods.IsZoomed(window.Handle))
            {
                throw new InvalidOperationException("测试窗口没有按设置进入最大化状态。");
            }

            if ((unchecked((uint)NativeMethods.GetWindowLongPointer(
                    window.Handle,
                    NativeMethods.WindowLongStyle).ToInt64())
                & NativeMethods.WindowStyleCaption) != 0)
            {
                throw new InvalidOperationException("最大化主窗口仍然带有系统标题栏样式。");
            }

            if (!NativeMethods.GetClientRectangle(window.Handle, out NativeMethods.Rectangle beforeActivation))
            {
                throw new InvalidOperationException("无法读取后台激活前的客户区尺寸。");
            }

            int baselineEdgeCount = CountVisibleScreenEdges(window.Handle);

            using (MainWindow foreground = new(new SettingsStore(settingsPath + ".foreground"), new()))
            {
                foreground.Show();
                foreground.Activate();
                int redrawCountBeforeActivation = window.ActivationRedrawCountForTest;
                window.Activate();
                _ = NativeMethods.SendMessage(
                    window.Handle,
                    NativeMethods.WindowMessageActivate,
                    1,
                    0);
                if (!window.ActivationRedrawCompletedForTest)
                {
                    throw new InvalidOperationException("后台重新激活前没有同步重绘主窗口及全部子控件。");
                }
                if (window.ActivationRedrawCountForTest < redrawCountBeforeActivation + 2)
                {
                    throw new InvalidOperationException("后台重新激活没有分别覆盖单实例请求和系统激活消息的重绘路径。");
                }
                int[] activationEdgeCounts =
                [
                    CountVisibleScreenEdges(window.Handle),
                    CaptureActivationEdgesAfterDelay(window.Handle, 16),
                    CaptureActivationEdgesAfterDelay(window.Handle, 50),
                ];
                int minimumEdgeCount = activationEdgeCounts.Min();
                if (baselineEdgeCount > 0 && minimumEdgeCount < baselineEdgeCount * 0.55)
                {
                    throw new InvalidOperationException(
                        $"后台重新激活出现内容缺失帧，基准边缘数 {baselineEdgeCount}，"
                        + $"激活帧 {string.Join('/', activationEdgeCounts)}。");
                }
                foreground.Close();
            }

            if (!NativeMethods.IsZoomed(window.Handle))
            {
                throw new InvalidOperationException("后台再次激活把最大化窗口恢复成了普通窗口。");
            }

            if (!NativeMethods.GetClientRectangle(window.Handle, out NativeMethods.Rectangle afterActivation)
                || beforeActivation.Right - beforeActivation.Left != afterActivation.Right - afterActivation.Left
                || beforeActivation.Bottom - beforeActivation.Top != afterActivation.Bottom - afterActivation.Top)
            {
                throw new InvalidOperationException("后台重新激活改变了最大化窗口的客户区尺寸。");
            }

            if ((unchecked((uint)NativeMethods.GetWindowLongPointer(
                    window.Handle,
                    NativeMethods.WindowLongStyle).ToInt64())
                & NativeMethods.WindowStyleCaption) != 0)
            {
                throw new InvalidOperationException("后台重新激活后系统标题栏样式重新出现。");
            }

            window.Close();
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
        TaskCompletionSource<Exception?> completion,
        ConcurrentQueue<string> stages)
    {
        try
        {
            stages.Enqueue("创建并显示窗口");
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = OpenAndCloseAsync(window, workspace, documentPath, completion, stages));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunWorkspaceRestoreSmoke(
        string settingsPath,
        string workspace,
        string documentPath,
        ApplicationSettings settings,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            WorkspaceInstanceCoordinator coordinator = new(workspace);
            if (!coordinator.TryBecomeOwnerAsync().GetAwaiter().GetResult())
            {
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException("恢复测试未能取得临时工作区所有权。");
            }

            using MainWindow window = new(new SettingsStore(settingsPath), settings, workspace, coordinator);
            if (!window.RestoreTabsPreparedBeforeShowForTest
                || window.OpenDocumentCount != settings.OpenFiles.Length
                || window.LoadedDocumentCountForTest != 0)
            {
                throw new InvalidOperationException("恢复标签没有在窗口首次显示前建立元数据和加载占位。");
            }

            window.Show();
            window.Post(() => _ = VerifyWorkspaceRestoreAndCloseAsync(
                window,
                documentPath,
                settings.OpenFiles[^2],
                settings.OpenFiles.Length,
                completion));
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

    private static void RunBottomPanelSwitchSmoke(
        string settingsPath,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyBottomPanelSwitchAndCloseAsync(window, workspace, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static void RunCommitToolbarSmoke(
        string settingsPath,
        string workspace,
        string trackedPath,
        string previewPath,
        TaskCompletionSource<Exception?> completion,
        ConcurrentQueue<string> stages)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = VerifyCommitToolbarAndCloseAsync(
                window,
                workspace,
                trackedPath,
                previewPath,
                completion,
                stages));
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
        TaskCompletionSource<Exception?> completion,
        Action<string> reportProgress)
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
                completion,
                reportProgress));
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
            _ = NativeMethods.GetWindowRectangle(window.Handle, out NativeMethods.Rectangle mainBounds);
            int expectedResolverWidth = NativeConflictResolverDialog.CalculateDialogWidthForTest(
                (int)Math.Round(NativeTheme.Unscale(mainBounds.Right - mainBounds.Left)));
            if (resolver.HandleForTest == 0
                || !resolver.YoursIsReadOnlyForTest
                || resolver.ResultIsReadOnlyForTest
                || !resolver.TheirsIsReadOnlyForTest
                || resolver.LineNumbersVisibleForTest != (false, false, false)
                || resolver.LogicalWindowWidthForTest != expectedResolverWidth)
            {
                throw new InvalidOperationException(
                    "三栏解决器没有保持左右只读、仅中间结果可编辑、隐藏编辑区行号并相对主窗口响应式布局。");
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
        TaskCompletionSource<Exception?> completion,
        Action<string> reportProgress)
    {
        try
        {
            reportProgress("打开工作区");
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("阶段三界面测试工作区未能打开。");
            }

            Assert.IsTrue(window.ShowProjectContextMenuForTest(trackedPath));
            Dictionary<string, NativeContextMenuIcon> projectMenuIcons = window.ContextMenuForTest!
                .VisualItemsForTest.ToDictionary(item => item.Label, item => item.Icon);
            window.DismissContextMenuForTest();

            window.ShowHistoryForTest();
            reportProgress("等待历史首屏");
            DateTime historyDeadline = DateTime.UtcNow.AddSeconds(8);
            while ((window.HistoryRepositoryKindForTest != GitRepositoryKind.WorkingTree
                    || window.HistoryEntryCountForTest != 100)
                && DateTime.UtcNow < historyDeadline)
            {
                await Task.Delay(25);
            }

            NativeGitHistoryPanel history = window.HistoryPanelForTest
                ?? throw new InvalidOperationException("历史面板未按需创建。");
            await history.SelectCommitForTestAsync(0);
            Assert.IsTrue(history.ShowHistoryContextMenuForHost());
            NativeContextMenu historyMenu = history.ContextMenuForTest!;
            Dictionary<string, NativeContextMenuIcon> historyMenuIcons = historyMenu.VisualItemsForTest
                .ToDictionary(item => item.Label, item => item.Icon);
            Assert.AreEqual(NativeContextMenuIcon.Copy, historyMenuIcons[UiText.CopyCommitHash]);
            Assert.AreEqual(NativeContextMenuIcon.CherryPick, historyMenuIcons[UiText.CherryPick]);
            Assert.AreEqual(NativeContextMenuIcon.Reset, historyMenuIcons[UiText.ResetCurrentBranchHere]);
            foreach (string label in new[] { UiText.CompareWithWorkspace, UiText.RevertCommit, UiText.NewBranch, UiText.NewTag })
            {
                Assert.AreEqual(NativeContextMenuIcon.None, historyMenuIcons[label], $"{label} 应与 PyCharm 一样保留空图标列。");
            }
            _ = NativeMethods.SendMessage(historyMenu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);
            if (!history.BranchListUsesOwnerDrawForTest)
            {
                throw new InvalidOperationException("历史面板的分支栏没有使用自绘列表样式。");
            }

            if (!history.HistoryListUsesIdeaInputForTest)
            {
                throw new InvalidOperationException("历史提交列表没有接入 IDEA 风格的右键与键盘菜单输入。");
            }

            if (!history.FilesListUsesIdeaActivationForTest)
            {
                throw new InvalidOperationException("历史变化文件列表没有接入双击与 Enter 打开 Diff 的交互。");
            }

            reportProgress("读取引用");
            GitReferenceResult referenceResult = await new GitReferenceService(runtime).ReadAsync(repository);
            string currentBranch = referenceResult.Snapshot?.Branches
                .FirstOrDefault(branch => branch.IsCurrent)?.Name
                ?? throw new InvalidOperationException("测试仓库没有可筛选的当前分支。");
            history.SetBranchFilterForTest(currentBranch);
            if (!history.BranchRowsForTest.Contains(currentBranch))
            {
                throw new InvalidOperationException("历史面板的分支与标签筛选没有返回匹配引用。");
            }

            int branchResetCountBeforeNoMatch = history.BranchListResetCountForTest;
            int branchDeltaCountBeforeNoMatch = history.BranchListDeltaCountForTest;
            history.SetBranchFilterForTest("不存在的分支");
            if (history.BranchRowsForTest.Count != 0
                || history.BranchListResetCountForTest != branchResetCountBeforeNoMatch
                || history.BranchListDeltaCountForTest <= branchDeltaCountBeforeNoMatch)
            {
                throw new InvalidOperationException("历史面板的分支与标签筛选仍显示不匹配引用，或无结果时整表重建。");
            }

            int branchResetCountBeforeClear = history.BranchListResetCountForTest;
            int branchDeltaCountBeforeClear = history.BranchListDeltaCountForTest;
            history.SetBranchFilterForTest(string.Empty);
            if (history.BranchListResetCountForTest != branchResetCountBeforeClear
                || history.BranchListDeltaCountForTest <= branchDeltaCountBeforeClear)
            {
                throw new InvalidOperationException("清空历史引用筛选时没有增量恢复引用树。");
            }
            int branchResetCountBeforeCollapse = history.BranchListResetCountForTest;
            int branchDeltaCountBeforeCollapse = history.BranchListDeltaCountForTest;
            if (!history.ClickBranchSectionForTest("本地")
                || history.BranchRowsForTest.Contains(currentBranch, StringComparer.OrdinalIgnoreCase)
                || history.BranchListResetCountForTest != branchResetCountBeforeCollapse
                || history.BranchListDeltaCountForTest <= branchDeltaCountBeforeCollapse)
            {
                throw new InvalidOperationException("单击历史本地分组箭头没有增量折叠引用。");
            }

            if (!history.ToggleBranchSectionWithKeyForTest("本地", NativeMethods.VirtualKeyEnter)
                || !history.BranchRowsForTest.Contains(currentBranch, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("历史引用分组按 Enter 后没有重新展开。");
            }

            if (!history.DoubleClickBranchSectionForTest("本地")
                || history.BranchRowsForTest.Contains(currentBranch, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("双击历史引用分组标题没有折叠分组。");
            }

            if (!history.ToggleBranchSectionWithKeyForTest("本地", NativeMethods.VirtualKeySpace)
                || !history.BranchRowsForTest.Contains(currentBranch, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("历史引用分组按 Space 后没有重新展开。");
            }

            reportProgress("应用分支筛选");
            await history.SelectBranchForTestAsync(currentBranch);
            if (!currentBranch.Equals(history.BranchFilterForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("单击历史引用没有把日志筛选到对应分支。");
            }

            if (!history.FilterKindUsesOwnerDrawForTest)
            {
                throw new InvalidOperationException("历史筛选类别仍使用会破坏深色主题的系统下拉框。");
            }

            if (!history.FilterToolbarMatchesSpecForTest)
            {
                throw new InvalidOperationException("历史筛选栏没有使用文本、分支、用户、日期和路径结构，或分页仍占据标题栏。");
            }

            if (!history.FilterUtilityButtonsVisibleForTest || !history.DetailsShownForTest)
            {
                throw new InvalidOperationException("历史筛选栏没有显示视觉稿要求的详情切换和搜索入口。");
            }

            if (!history.ToggleDetailsForTest()
                || history.DetailsShownForTest
                || history.DetailsSurfaceVisibleForTest)
            {
                throw new InvalidOperationException("历史详情眼睛入口没有局部隐藏右侧详情区。");
            }

            if (!history.ToggleDetailsForTest() || !history.DetailsShownForTest)
            {
                throw new InvalidOperationException("历史详情眼睛入口没有恢复右侧详情区。");
            }

            var historyBounds = history.BoundsForTest;
            try
            {
                history.SetBounds(historyBounds.X, historyBounds.Y, historyBounds.Width, NativeTheme.Scale(305));
                _ = NativeMethods.SetFocus(history.VisibleSideToolbarButtonsForTest[^1]);
                history.SetBounds(historyBounds.X, historyBounds.Y, historyBounds.Width, NativeTheme.Scale(180));
                if (!history.ToolbarOverflowVisibleForTest)
                    throw new InvalidOperationException("短历史工具栏没有提供溢出箭头。");
                if (NativeAccessibility.GetNameForTest(NativeMethods.GetFocus()) != UiText.MoreHistoryTools)
                    throw new InvalidOperationException("被收起的历史按钮没有将焦点转移到溢出箭头。");
                _ = NativeMethods.GetWindowRectangle(history.Handle, out NativeMethods.Rectangle panelBounds);
                foreach (nint button in history.VisibleSideToolbarButtonsForTest)
                {
                    _ = NativeMethods.GetWindowRectangle(button, out NativeMethods.Rectangle buttonBounds);
                    if (buttonBounds.Bottom > panelBounds.Bottom)
                        throw new InvalidOperationException("短历史工具栏仍有可见按钮被面板底边裁切。");
                }
                history.ShowToolbarOverflowForTest();
                NativeToolbarPopup popup = history.ToolbarPopupForTest
                    ?? throw new InvalidOperationException("溢出箭头没有打开横向工具栏。");
                nint search = popup.ButtonsForTest[3];
                _ = NativeMethods.SetFocus(search);
                _ = NativeMethods.SendMessage(search, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                if (!history.FilterHasFocusForTest || popup.Handle != 0)
                    throw new InvalidOperationException("溢出工具栏的搜索未恢复到原搜索输入框。");
            }
            finally
            {
                history.SetBounds(historyBounds.X, historyBounds.Y, historyBounds.Width, historyBounds.Height);
            }
            int filterKind = history.FilterKindIndexForTest;
            int entryCountBeforeOverflow = history.EntryCount;
            try
            {
                history.SetBounds(historyBounds.X, historyBounds.Y, NativeTheme.Scale(620), historyBounds.Height);
                if (!history.FilterOverflowVisibleForTest || !history.FilterUtilityButtonsVisibleForTest)
                    throw new InvalidOperationException("窄历史栏未保留筛选收纳入口、搜索和详情切换。");
                history.ShowFilterOverflowForTest();
                nint overflowMenu = history.FilterOverflowMenuForTest;
                if (overflowMenu == 0 || !NativeMethods.IsWindowVisible(overflowMenu))
                    throw new InvalidOperationException("历史筛选右箭头未打开原生菜单。");
                _ = NativeMethods.SendMessage(overflowMenu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
                _ = NativeMethods.SendMessage(overflowMenu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyDown, 0);
                _ = NativeMethods.SendMessage(overflowMenu, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEnter, 0);
                if (history.FilterKindIndexForTest != 2 || !history.FilterHasFocusForTest
                    || history.EntryCount != entryCountBeforeOverflow || NativeMethods.IsWindow(overflowMenu))
                    throw new InvalidOperationException("收纳菜单没有执行用户筛选、恢复输入焦点，或意外刷新了提交列表。");
            }
            finally
            {
                history.SelectFilterKindForTest(filterKind);
                history.SetBounds(historyBounds.X, historyBounds.Y, historyBounds.Width, historyBounds.Height);
            }

            if (!history.ToolbarIsCompactForTest)
            {
                throw new InvalidOperationException("历史面板没有把低频管理操作收进更多菜单。");
            }

            if (!history.CommitDetailsLabelCreatedForTest)
            {
                throw new InvalidOperationException("历史详情区缺少与 IDEA 工具窗口一致的详情标题层级。");
            }

            if (!window.FileTreeVisibleForTest
                || !window.HistoryPanelVisibleForTest
                || !window.HistoryPanelIsBelowDocumentForTest
                || !window.BottomPanelIsAboveEditorForTest
                || !window.BottomPanelStartsAtEditorForTest
                || !window.ProjectPanelKeepsFullHeightForTest
                || !window.GitHistoryButtonIsAtBottomForTest)
            {
                throw new InvalidOperationException("Git 历史没有只占用编辑区底部，或左侧工具窗口没有保持完整高度。");
            }

            int initialBottomHeight = window.BottomPanelHeightForTest;
            if (!window.DragBottomSplitterForTest(-NativeTheme.Scale(24))
                || window.BottomPanelHeightForTest >= initialBottomHeight
                || !window.HistoryPanelIsBelowDocumentForTest
                || !window.BottomPanelIsAboveEditorForTest
                || !window.BottomPanelStartsAtEditorForTest
                || !window.ProjectPanelKeepsFullHeightForTest)
            {
                throw new InvalidOperationException("底部 Git 工具窗口不能像 IDEA 一样调整高度。");
            }

            if (!window.HistoryRuntimeAvailableForTest
                || window.HistoryRepositoryKindForTest != GitRepositoryKind.WorkingTree)
            {
                throw new InvalidOperationException("历史面板未识别 Git 工作区。");
            }

            if (window.HistoryEntryCountForTest != 100 || !history.HasNextPageForTest)
            {
                throw new InvalidOperationException("历史面板没有按每页 100 条分页读取。");
            }

            await history.SelectCommitForTestAsync(0);
            string firstPageSelection = history.SelectedCommitHashForTest
                ?? throw new InvalidOperationException("历史首屏选择没有记录提交标识。");
            reportProgress("加载历史下一页");
            await history.ScrollHistoryToBottomForTestAsync();
            if (history.CurrentPageForTest != 1
                || history.EntryCount != 101
                || !firstPageSelection.Equals(
                    history.SelectedCommitHashForTest,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("历史面板滚动加载下一页时没有保留已有提交、选择和详情上下文。");
            }

            reportProgress("加载根提交详情");
            await history.SelectCommitForTestAsync(100);
            if (history.ChangedFileCountForTest != 1)
            {
                throw new InvalidOperationException("根提交详情没有展示变化文件。");
            }

            if (!history.DetailsSurfaceVisibleForTest)
            {
                throw new InvalidOperationException("选择提交后详情正文没有在变化文件列表下方原位显示。");
            }

            int fileResetCountBeforeCollapse = history.FileListResetCountForTest;
            int fileDeltaCountBeforeCollapse = history.FileListDeltaCountForTest;
            if (!history.ToggleFileGroupForTest(".")
                || history.FileTreeRowCountForTest != 1
                || history.FileListResetCountForTest != fileResetCountBeforeCollapse
                || history.FileListDeltaCountForTest <= fileDeltaCountBeforeCollapse)
            {
                throw new InvalidOperationException("历史变化文件根分组没有增量折叠。");
            }

            if (!history.ToggleFileGroupForTest(".")
                || history.FileTreeRowCountForTest != 2
                || history.FileListResetCountForTest != fileResetCountBeforeCollapse)
            {
                throw new InvalidOperationException("历史变化文件根分组没有增量展开。");
            }

            int detailRequestsForRoot = history.CommitDetailsRequestCountForTest;
            string? focusCommit = history.SelectedCommitHashForTest;
            NativeSelectionFocusAssertions.Verify(window, history, "_branchesList");
            NativeSelectionFocusAssertions.Verify(window, history, "_historyList");
            NativeSelectionFocusAssertions.Verify(window, history, "_filesList");
            Assert.AreEqual(focusCommit, history.SelectedCommitHashForTest);
            Assert.AreEqual(detailRequestsForRoot, history.CommitDetailsRequestCountForTest, "焦点变化不能重新请求提交详情。");
            await history.SelectCommitForTestAsync(100);
            if (history.CommitDetailsRequestCountForTest != detailRequestsForRoot)
            {
                throw new InvalidOperationException("重复选择同一提交时重复请求了提交详情。");
            }

            await history.SelectCommitForTestAsync(0);
            if (history.CommitDetailsRequestCountForTest != detailRequestsForRoot + 1)
            {
                throw new InvalidOperationException("切换到另一条提交时没有重新请求提交详情。");
            }

            await history.SelectCommitForTestAsync(100);

            string selectedCommitHash = history.SelectedCommitHashForTest
                ?? throw new InvalidOperationException("选择提交后没有记录稳定提交标识。");
            string topCommitHash = history.HistoryListTopHashForTest
                ?? throw new InvalidOperationException("历史刷新前没有可恢复的顶部提交标识。");
            int resetCountBeforeHistoryRefresh = history.HistoryListResetCountForTest;
            int deltaCountBeforeHistoryRefresh = history.HistoryListDeltaCountForTest;
            int changedFileCountBeforeHistoryRefresh = history.ChangedFileCountForTest;
            await RunGitAsync(runtime, workspace, "commit", "--allow-empty", "-m", "test: incremental refresh");
            window.RequestHistoryRefreshForTest();
            DateTime historyRefreshDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((history.OperationRunningForTest
                    || history.HistoryListDeltaCountForTest == deltaCountBeforeHistoryRefresh)
                && DateTime.UtcNow < historyRefreshDeadline)
            {
                await Task.Delay(20);
            }

            if (history.OperationRunningForTest
                || history.EntryCount != 102
                || history.HistoryListResetCountForTest != resetCountBeforeHistoryRefresh
                || history.HistoryListDeltaCountForTest <= deltaCountBeforeHistoryRefresh
                || !topCommitHash.Equals(
                    history.HistoryListTopHashForTest,
                    StringComparison.OrdinalIgnoreCase)
                || !history.CommitDetailsLoadedForTest
                || !selectedCommitHash.Equals(
                    history.SelectedCommitHashForTest,
                    StringComparison.OrdinalIgnoreCase)
                || history.ChangedFileCountForTest != changedFileCountBeforeHistoryRefresh)
            {
                throw new InvalidOperationException("历史刷新没有增量插入新提交，或没有保留视口、选中提交和右侧详情。");
            }

            if (window.ReferenceComparisonVisibleForTest)
            {
                throw new InvalidOperationException("只选择历史提交后不应提前打开提交文件 Diff。");
            }

            reportProgress("单击历史变化文件只改变选中");
            await history.SelectCommitFileForTestAsync(0);
            await Task.Delay(100);
            if (window.ReferenceComparisonVisibleForTest)
            {
                throw new InvalidOperationException("单击历史变化文件错误地打开了提交 Diff。");
            }

            reportProgress("加载根提交文件差异");
            await history.ActivateCommitFileWithEnterForTestAsync(0);
            if (!window.ReferenceComparisonVisibleForTest
                || !window.ReferenceComparisonFileBarForTest.Contains("阶段三.txt", StringComparison.Ordinal)
                || !window.ReferenceComparisonFileBarForTest.Contains('→')
                || !window.ReferenceComparisonSettingsButtonUsesIconForTest
                || !window.ReferenceComparisonModeButtonsUseIconsForTest
                || !window.ReferenceComparisonToolbarToolTipsCreatedForTest)
            {
                throw new InvalidOperationException("根提交文件 diff 没有显示双方引用、中文路径或统一的设置齿轮入口。");
            }

            if (!history.DetailsSurfaceVisibleForTest)
            {
                throw new InvalidOperationException("打开中央比较标签后底部提交详情没有保持原位。");
            }

            int historyEntryCountBeforeFileHistory = history.EntryCount;
            int historyTopIndexBeforeFileHistory = history.HistoryListTopIndexForTest;
            window.CloseActiveTabForTest();
            if (window.ReferenceComparisonVisibleForTest
                || !history.DetailsSurfaceVisibleForTest
                || !window.HistoryPanelVisibleForTest)
            {
                throw new InvalidOperationException("关闭提交 Diff 标签后没有恢复中央内容，或破坏了底部历史上下文。");
            }

            window.SetStatusForTest(UiText.PathCopied);
            await history.CompareRevisionForHostAsync("HEAD", "阶段三.txt");
            DateTime comparisonDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.ComparisonViewForTest?.LoadingForTest == true && DateTime.UtcNow < comparisonDeadline)
            {
                await Task.Delay(20);
            }
            if (!window.ReferenceComparisonVisibleForTest
                || window.ComparisonViewForTest is not { LoadingForTest: false, HasDocument: true }
                || window.StatusTextForTest != UiText.PathCopied)
            {
                throw new InvalidOperationException("历史引用比较完成后残留全局加载提示，或覆盖了已有操作提示。");
            }
            window.CloseActiveTabForTest();

            window.ShowFileHistoryForTest(trackedPath);
            reportProgress("加载文件历史");
            DateTime filterDeadline = DateTime.UtcNow.AddSeconds(5);
            while (history.OperationRunningForTest && DateTime.UtcNow < filterDeadline)
            {
                await Task.Delay(20);
            }

            if (history.CurrentPageForTest != 0
                || history.FileFilterForTest != "阶段三.txt"
                || history.EntryCount != 1
                || !history.FileHistoryModeForTest
                || !history.FileHistoryEditorVisibleForTest
                || !history.FileHistoryEditorReadOnlyForTest
                || !history.FileHistoryToolbarCreatedForTest
                || !history.FileHistoryRightToolbarVisibleForTest)
            {
                throw new InvalidOperationException("文件树文件历史入口没有切换为视觉稿要求的双栏只读文件历史模式。");
            }

            DateTime fileHistoryPreviewDeadline = DateTime.UtcNow.AddSeconds(5);
            while (history.FileHistoryComparisonForTest is not { HasDocument: true, IsBusy: false }
                && DateTime.UtcNow < fileHistoryPreviewDeadline)
            {
                await Task.Delay(20);
            }

            if (history.FileHistoryComparisonForTest is not { HasDocument: true, IsBusy: false }
                || history.FileHistoryComparisonForTest.ChangedLineCountForTest == 0)
            {
                throw new InvalidOperationException("文件历史右侧只读内容没有完成异步加载。");
            }

            reportProgress("清除文件历史筛选");
            history.ReturnFromHistoryForTest();
            DateTime clearFileHistoryDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((history.FileFilterForTest is not null
                    || history.EntryCount != historyEntryCountBeforeFileHistory)
                && DateTime.UtcNow < clearFileHistoryDeadline)
            {
                await Task.Delay(20);
            }

            if (history.FileFilterForTest is not null
                || history.EntryCount != historyEntryCountBeforeFileHistory
                || history.FileHistoryModeForTest
                || history.FileHistoryEditorVisibleForTest
                || !window.HistoryPanelVisibleForTest
                || !selectedCommitHash.Equals(
                    history.SelectedCommitHashForTest,
                    StringComparison.OrdinalIgnoreCase)
                || !history.CommitDetailsLoadedForTest
                || history.HistoryListTopIndexForTest != historyTopIndexBeforeFileHistory)
            {
                throw new InvalidOperationException("清除文件历史筛选后没有恢复原 Git 历史工具窗口上下文。");
            }

            if (!history.ManagementButtonsCreatedForTest)
            {
                throw new InvalidOperationException("历史面板缺少阶段三管理入口。");
            }

            reportProgress("打开引用管理");
            await ShowAndCloseDialogAsync(
                UiText.ReferenceManagement,
                () => NativeGitReferenceDialog.Show(
                    window.Handle,
                    repository,
                    new GitReferenceService(runtime),
                    new(),
                    _ => { }));
            reportProgress("打开本地状态管理");
            await ShowAndCloseDialogAsync(
                UiText.LocalStateManagement,
                () => NativeStashManagerDialog.Show(
                    window.Handle,
                    repository,
                    new GitWorkspaceStateService(runtime),
                    new(),
                    _ => { }));
            reportProgress("打开 Worktree 管理");
            await ShowAndCloseDialogAsync(
                UiText.WorktreeManagement,
                () => NativeWorktreeManagerDialog.Show(
                    window.Handle,
                    repository,
                    new GitWorktreeService(runtime),
                    new(),
                    _ => { }));
            reportProgress("打开远端管理");
            await ShowAndCloseDialogAsync(
                UiText.RemoteManagement,
                () => NativeRemoteDialog.Show(
                    window.Handle,
                    repository,
                    new GitRemoteService(runtime),
                    new(),
                    _ => { }));

            window.ShowGitForTest();
            reportProgress("等待 Changes 列表");
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

            reportProgress("检查 Changes 菜单图标语义");
            Assert.IsTrue(window.GitPanelForTest!.ShowChangesContextMenuForHost("阶段三.txt"));
            NativeContextMenu changesMenu = window.GitPanelForTest.ContextMenuForTest!;
            Dictionary<string, NativeContextMenuIcon> changesMenuIcons = changesMenu.VisualItemsForTest
                .ToDictionary(item => item.Label, item => item.Icon);
            foreach (string label in new[] { UiText.FileHistory, UiText.Blame, UiText.CopyPath, UiText.RevealInExplorer })
            {
                Assert.AreEqual(projectMenuIcons[label], changesMenuIcons[label], $"{label} 在项目与 Changes 菜单中必须使用相同图标");
            }
            Assert.AreEqual(NativeContextMenuIcon.Compare, changesMenuIcons[UiText.ShowDiff]);
            Assert.AreEqual(NativeContextMenuIcon.Reset, changesMenuIcons[UiText.Rollback + "…"]);
            _ = NativeMethods.SendMessage(changesMenu.Handle, NativeMethods.WindowMessageKeyDown, NativeMethods.VirtualKeyEscape, 0);

            GitStatusResult rollbackStatus = await new GitStatusService(runtime).ReadAsync(repository);
            GitChangedFile rollbackFile = rollbackStatus.Snapshot?.Files
                .SingleOrDefault(file => file.RelativePath.Equals("阶段三.txt", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Rollback 确认测试没有读取到目标文件状态。");
            GitDiffService rollbackDiffService = new(runtime);
            GitDiffResult rollbackDiff = await rollbackDiffService.CreateAsync(
                repository,
                rollbackFile,
                new());
            if (!rollbackDiff.IsSuccess || rollbackDiff.Document is null)
            {
                throw new InvalidOperationException("Rollback 确认测试没有生成真实 Diff。");
            }

            bool rollbackConfirmed = false;
            reportProgress("打开 Rollback 确认");
            await ShowAndCloseDialogAsync(
                UiText.RollbackDialogTitle,
                () => rollbackConfirmed = NativeRollbackDialog.Show(
                    window.Handle,
                    repository,
                    rollbackFile,
                    rollbackDiff.Document,
                    rollbackDiffService,
                    new(),
                    _ => { }));
            if (rollbackConfirmed)
            {
                throw new InvalidOperationException("关闭 Rollback 确认窗口不应执行危险操作。");
            }

            reportProgress("执行 Rollback");
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

            window.ToggleHistoryForTest();
            reportProgress("验证历史工具窗口切换");
            if (window.HistoryPanelVisibleForTest || !window.GitPanelVisibleForTest || window.FileTreeVisibleForTest)
            {
                throw new InvalidOperationException("再次点击已展开的 Git 历史入口没有只收起底部工具区并保留 Commit。");
            }

            window.ToggleHistoryForTest();
            if (!window.HistoryPanelVisibleForTest || !window.GitPanelVisibleForTest || window.FileTreeVisibleForTest)
            {
                throw new InvalidOperationException("重新展开 Git 历史后没有恢复 Commit 与底部工具区的独立组合。");
            }

            reportProgress("完成");
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

    [DllImport("user32.dll", EntryPoint = "GetDC")]
    private static extern nint GetDeviceContext(nint window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static extern int ReleaseDeviceContext(nint window, nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyPixels(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll", EntryPoint = "GetDIBits")]
    private static extern int GetBitmapPixels(
        nint deviceContext,
        nint bitmap,
        uint firstScanLine,
        uint scanLineCount,
        [Out] byte[] pixels,
        ref NativeMethods.BitmapInfo bitmapInfo,
        uint colorUse);

    private static async Task VerifyCommitToolbarAndCloseAsync(
        MainWindow window,
        string workspace,
        string trackedPath,
        string previewPath,
        TaskCompletionSource<Exception?> completion,
        ConcurrentQueue<string> stages)
    {
        try
        {
            stages.Enqueue("开始打开工作区");
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("Commit 工具栏测试工作区未能打开。");
            }

            stages.Enqueue("工作区已打开");
            window.ShowGitForTest();
            stages.Enqueue("Commit 面板已创建");
            DateTime loadDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.GitRepositoryKindForTest != GitRepositoryKind.WorkingTree
                    || window.GitChangedFileCountForTest != 2)
                && DateTime.UtcNow < loadDeadline)
            {
                await Task.Delay(20);
            }

            if (window.GitChangedFileCountForTest != 2 || !window.CommitToolbarToolTipsCreatedForTest)
            {
                throw new InvalidOperationException("Commit 工具栏没有完成五个入口及其悬停说明的接线。");
            }

            stages.Enqueue("Commit 面板已加载");
            NativeGitPanel gitPanel = window.GitPanelForTest
                ?? throw new InvalidOperationException("Commit 工具窗口实例不存在。");
            if (!gitPanel.LastCommitActionVisibleForTest
                || gitPanel.CommitChangeCountForTest != "1 modified"
                || !gitPanel.CommitSettingsActionCreatedForTest
                || !gitPanel.CommitSupplementalControlsWithinBoundsForTest)
            {
                throw new InvalidOperationException(
                    "Commit 区没有按视觉稿显示上一次提交、改动计数、设置入口或有效边界："
                    + $"上一次提交={gitPanel.LastCommitActionVisibleForTest}，"
                    + $"改动计数={gitPanel.CommitChangeCountForTest}，"
                    + $"设置入口={gitPanel.CommitSettingsActionCreatedForTest}，"
                    + $"边界={gitPanel.CommitSupplementalControlsWithinBoundsForTest}。");
            }

            gitPanel.SetCommitMessageForTest("尚未提交的草稿");
            if (!await gitPanel.ToggleAmendForTestAsync().WaitAsync(TimeSpan.FromSeconds(5))
                || !gitPanel.AmendSelectedForTest
                || gitPanel.CommitMessageForTest != "test: commit toolbar baseline"
                || !window.CommitActionEnabledForTest)
            {
                throw new InvalidOperationException("勾选 Amend 后没有读取上一次提交信息或启用仅改写提交信息的提交动作。");
            }

            stages.Enqueue("Amend 已启用");
            if (!await gitPanel.ToggleAmendForTestAsync().WaitAsync(TimeSpan.FromSeconds(5))
                || gitPanel.AmendSelectedForTest
                || gitPanel.CommitMessageForTest != "尚未提交的草稿")
            {
                throw new InvalidOperationException("取消 Amend 后没有恢复用户尚未提交的草稿。");
            }

            stages.Enqueue("Amend 已取消");
            var overviewState = window.CommitToolbarEnabledStateForTest;
            if (!overviewState.Refresh
                || overviewState.Rollback
                || overviewState.ShowDiff
                || overviewState.ExpandAll
                || overviewState.Preview)
            {
                throw new InvalidOperationException("Commit 概览空选择时的工具栏启用条件不正确。");
            }

            int populateCount = window.GitChangesPopulateCountForTest;
            if (!await window.InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction.Refresh))
            {
                throw new InvalidOperationException("Commit 刷新按钮没有通过窗口命令触发。");
            }

            DateTime refreshDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitRefreshingForTest && DateTime.UtcNow < refreshDeadline)
            {
                await Task.Delay(10);
            }
            if (window.GitRefreshingForTest || window.GitChangesPopulateCountForTest != populateCount)
            {
                throw new InvalidOperationException("无变化刷新重建了 Changes 列表或没有完成。");
            }

            stages.Enqueue("无变化刷新已完成");
            if (!window.SelectGitFileRowForTest(Path.GetFileName(previewPath)))
            {
                throw new InvalidOperationException("未能为 Commit 工具栏选择 Markdown 变更行。");
            }

            var previewState = window.CommitToolbarEnabledStateForTest;
            if (!previewState.Refresh
                || !previewState.Rollback
                || !previewState.ShowDiff
                || previewState.ExpandAll
                || !previewState.Preview)
            {
                throw new InvalidOperationException("选择可预览文件后 Commit 工具栏没有同步启用正确动作。");
            }

            if (!await window.InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction.ShowDiff)
                || !window.GitDiffVisibleForTest
                || !window.CurrentFileContextForTest.Equals(UiText.CurrentFile, StringComparison.Ordinal)
                || !window.GitDiffPathForTest.Equals(Path.GetFileName(previewPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("显示 Diff 工具按钮没有通过窗口命令打开当前文件 Diff。");
            }

            stages.Enqueue("Diff 已打开");
            if (!await window.InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction.Preview))
            {
                throw new InvalidOperationException("预览工具按钮没有通过窗口命令触发。");
            }

            DateTime previewDeadline = DateTime.UtcNow.AddSeconds(3);
            while (!previewPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow < previewDeadline)
            {
                await Task.Delay(10);
            }
            if (!previewPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || window.ActiveDocumentKind != DocumentKind.Markdown)
            {
                throw new InvalidOperationException("预览工具按钮没有打开 Markdown 文件标签。");
            }

            stages.Enqueue("Markdown 预览已打开");
            int expandedEntryCount = window.VisibleGitChangeEntryCountForTest;
            if (!window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles))
            {
                throw new InvalidOperationException("未能折叠 Unversioned Files 分组以验证展开全部命令。");
            }

            var collapsedState = window.CommitToolbarEnabledStateForTest;
            if (!collapsedState.ExpandAll
                || !await window.InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction.ExpandAll)
                || window.VisibleGitChangeEntryCountForTest != expandedEntryCount)
            {
                throw new InvalidOperationException("展开全部工具按钮没有恢复折叠的 Changes 分组。");
            }

            if (!window.SelectGitFileRowForTest(Path.GetFileName(trackedPath))
                || !window.CommitToolbarEnabledStateForTest.Rollback
                || !await window.InvokeCommitToolbarActionForTestAsync(NativeCommitToolbarAction.Rollback))
            {
                throw new InvalidOperationException("回滚工具按钮没有通过窗口命令执行所选文件回滚。");
            }

            stages.Enqueue("回滚命令已返回");
            DateTime rollbackDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.GitRefreshingForTest || window.GitChangedFileCountForTest != 1)
                && DateTime.UtcNow < rollbackDeadline)
            {
                await Task.Delay(10);
            }
            if (window.GitChangedFileCountForTest != 1)
            {
                throw new InvalidOperationException("回滚完成后 Changes 列表没有局部刷新到最新状态。");
            }

            stages.Enqueue("回滚刷新已完成");
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

    private static async Task VerifyBottomPanelSwitchAndCloseAsync(
        MainWindow window,
        string workspace,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("底部工具窗口切换测试工作区未能打开。");
            }

            window.ShowHistoryForTest();
            DateTime historyDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((!window.HistoryPanelVisibleForTest || window.HistoryEntryCountForTest == 0)
                && DateTime.UtcNow < historyDeadline)
            {
                await Task.Delay(20);
            }

            if (!window.HistoryPanelVisibleForTest || !window.BottomPanelIsAboveEditorForTest)
            {
                throw new InvalidOperationException("直接打开 Git 历史后底部面板没有位于编辑器前方。");
            }

            NativeGitHistoryPanel historyPanel = window.HistoryPanelForTest
                ?? throw new InvalidOperationException("Git 历史面板句柄未创建。");
            if (!NativeMethods.GetWindowRectangle(window.DocumentTabsHandleForTest, out NativeMethods.Rectangle editorBounds)
                || !NativeMethods.GetWindowRectangle(historyPanel.Handle, out NativeMethods.Rectangle historyBounds)
                || historyBounds.Left != editorBounds.Left + MainWindow.SurfaceContentInsetForTest
                || historyBounds.Right != editorBounds.Right - MainWindow.SurfaceContentInsetForTest)
            {
                throw new InvalidOperationException("底部 Git 历史没有与编辑工作区共用完整的左右边界。");
            }

            window.ShowGitForTest();
            DateTime gitDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.GitChangedFileCountForTest == 0 || !window.GitPanelVisibleForTest)
                && DateTime.UtcNow < gitDeadline)
            {
                await Task.Delay(20);
            }

            if (!window.HistoryPanelVisibleForTest
                || !window.GitPanelVisibleForTest
                || !window.BottomPanelIsAboveEditorForTest)
            {
                throw new InvalidOperationException("从 Git 历史切换到 Commit 后底部面板被编辑器遮挡。");
            }

            if (!await window.SelectGitFileForTestAsync("tracked.txt")
                || !window.GitDiffVisibleForTest
                || !window.BottomPanelIsAboveEditorForTest)
            {
                throw new InvalidOperationException("打开 Commit Diff 后底部面板没有保持在编辑器前方。");
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

    private static void VerifyGitPanelMoveInvalidation(nint parent, NativeGitPanel panel)
    {
        _ = NativeMethods.GetWindowRectangle(panel.Handle, out NativeMethods.Rectangle panelBounds);
        _ = NativeMethods.GetWindowRectangle(panel.DiffHandleForTest, out NativeMethods.Rectangle diffBounds);
        NativeMethods.Point panelOrigin = new() { X = panelBounds.Left, Y = panelBounds.Top };
        NativeMethods.Point diffOrigin = new() { X = diffBounds.Left, Y = diffBounds.Top };
        _ = NativeMethods.ScreenToClient(parent, ref panelOrigin);
        _ = NativeMethods.ScreenToClient(parent, ref diffOrigin);
        int panelWidth = panelBounds.Right - panelBounds.Left;
        int panelHeight = panelBounds.Bottom - panelBounds.Top;
        int diffWidth = diffBounds.Right - diffBounds.Left;
        int diffHeight = diffBounds.Bottom - diffBounds.Top;
        // 清除旧更新区后只移动位置，防止尺寸变更或控件初次显示掩盖缺失的重绘请求。
        const uint ValidateChildren = 0x0008 | 0x0020 | NativeMethods.RedrawAllChildren;
        _ = NativeMethods.RedrawWindow(panel.Handle, 0, 0, ValidateChildren);
        try
        {
            panel.SetBounds(panelOrigin.X + 1, panelOrigin.Y, panelWidth, panelHeight,
                diffOrigin.X, diffOrigin.Y, diffWidth, diffHeight);
            Assert.IsTrue(GetUpdateRectangle(panel.Handle, out NativeMethods.Rectangle dirty, false), "提交面板移动后未请求重绘，旧项目区像素可能残留。");
            Assert.AreEqual(0, dirty.Left);
            Assert.AreEqual(0, dirty.Top);
            Assert.AreEqual(panelWidth, dirty.Right);
            Assert.AreEqual(panelHeight, dirty.Bottom);

            _ = NativeMethods.RedrawWindow(panel.Handle, 0, 0, ValidateChildren);
            panel.SetBounds(panelOrigin.X + 1, panelOrigin.Y, panelWidth, panelHeight,
                diffOrigin.X, diffOrigin.Y, diffWidth, diffHeight);
            Assert.IsFalse(GetUpdateRectangle(panel.Handle, out _, false), "未变的面板位置不应触发重复重绘。");
        }
        finally
        {
            panel.SetBounds(panelOrigin.X, panelOrigin.Y, panelWidth, panelHeight,
                diffOrigin.X, diffOrigin.Y, diffWidth, diffHeight);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

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

            string activePath = Path.Combine(workspace, "untracked.txt");
            await window.OpenDocumentForTestAsync(activePath);
            if (!activePath.Equals(window.SelectedTreePathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("打开文档后文件树没有选中对应文件。");
            }

            if (!window.ProjectHeaderActionsCreatedForTest)
            {
                throw new InvalidOperationException("项目工具窗口没有创建可交互的定位、折叠与更多操作。");
            }

            window.ResizeToMinimumForTest();
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

            if (window.GitSelectedFileCountForTest != 0
                || window.CommitActionEnabledForTest
                || window.GitCommitChangeCountForTest.Length != 0)
            {
                throw new InvalidOperationException("未跟踪文件被默认勾选，或提交计数与当前选择不一致。");
            }

            if (window.GetTreeGitStatusForTest(activePath) != GitChangeKind.Untracked)
            {
                throw new InvalidOperationException("Git 状态快照没有同步到项目树文件状态。");
            }

            int focusDiffRequests = window.GitDiffRequestCountForTest;
            int focusCheckedCount = window.GitSelectedFileCountForTest;
            NativeSelectionFocusAssertions.Verify(window, window.GitPanelForTest!, "_changesList");
            Assert.AreEqual(focusDiffRequests, window.GitDiffRequestCountForTest, "焦点变化不能请求 Diff。");
            Assert.AreEqual(focusCheckedCount, window.GitSelectedFileCountForTest, "焦点变化不能改变提交勾选。");

            if (!window.GitChangesListHasFocusForTest)
            {
                throw new InvalidOperationException("打开搜索前 Changes 列表没有保持焦点。");
            }
            window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "untracked");
            await WaitForSearchResultAsync(window);
            if (!window.GitPanelVisibleForTest
                || !window.GitNavigationActiveForTest
                || window.SearchNavigationActiveForTest
                || !window.SearchPanelVisibleForTest)
            {
                throw new InvalidOperationException("快速打开错误替换了 Commit 工具窗口，而不是覆盖当前上下文。");
            }
            if (!window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape)
                || !window.GitPanelVisibleForTest
                || !window.GitChangesListHasFocusForTest)
            {
                throw new InvalidOperationException("关闭快速打开后没有恢复 Commit 工具窗口和 Changes 焦点。");
            }

            if (window.ActiveDocumentTabStatusColorForTest
                != NativeGitStatusPalette.Resolve(GitChangeKind.Untracked, NativeTheme.IsDark("System")))
            {
                throw new InvalidOperationException("未跟踪文件的文档标签没有继承 IDEA 蓝色 Git 状态。");
            }

            int expectedTreeItemHeight = NativeTheme.Scale(27);
            expectedTreeItemHeight += expectedTreeItemHeight & 1;
            if (window.FileTreeItemHeightForTest != expectedTreeItemHeight)
            {
                throw new InvalidOperationException(
                    $"项目树行高为 {window.FileTreeItemHeightForTest}，预期为 {expectedTreeItemHeight}。");
            }

            if (!window.AdvancedOperationsButtonCreatedForTest)
            {
                throw new InvalidOperationException("Git 面板没有创建阶段四高级操作入口。");
            }

            if (!window.CommitPanelTitleCreatedForTest)
            {
                throw new InvalidOperationException("Git 改动页没有显示提交工具窗口标题。");
            }

            if (!window.CommitHeaderActionsCreatedForTest)
            {
                throw new InvalidOperationException("Git 改动页没有创建紧凑的更多与关闭操作。");
            }

            if (!window.CommitToolbarActionsCreatedForTest)
            {
                throw new InvalidOperationException("Git 改动页没有按视觉稿创建刷新、回滚、Diff、展开和预览工具栏。");
            }

            NativeGitPanel gitPanel = window.GitPanelForTest
                ?? throw new InvalidOperationException("Git 改动页实例不存在，无法核对提交操作尺寸。");
            VerifyGitPanelMoveInvalidation(window.Handle, gitPanel);
            if (!window.CommitActionsVisibleForTest)
            {
                throw new InvalidOperationException("最小窗口尺寸下的提交按钮被界面边界裁切。");
            }

            (int commitWidth, int commitPushWidth, int commitHeight, int commitPushHeight)
                = gitPanel.CommitActionGeometryForTest;
            if (commitWidth != NativeTheme.Scale(53)
                || commitPushWidth != NativeTheme.Scale(102)
                || commitHeight != NativeTheme.Scale(30)
                || commitPushHeight != NativeTheme.Scale(30))
            {
                throw new InvalidOperationException(
                    $"提交操作尺寸没有对齐视觉稿：提交={commitWidth}x{commitHeight}，"
                    + $"提交并推送={commitPushWidth}x{commitPushHeight}。");
            }

            if (window.CommitAndPushButtonWidthForTest > NativeTheme.Scale(140))
            {
                throw new InvalidOperationException("提交并推送按钮被拉伸为整栏宽度，没有保持紧凑操作区。");
            }

            if (!window.GitRemoteActionsUseOverflowForTest)
            {
                throw new InvalidOperationException("低频远端与高级操作没有收进提交标题栏的更多菜单。");
            }

            if (!window.GitPullModeUsesOwnerDrawForTest)
            {
                throw new InvalidOperationException("Pull 模式仍使用会破坏深色主题的系统下拉框。");
            }

            if (!window.ChangesListUsesOwnerDrawForTest)
            {
                throw new InvalidOperationException("Git 改动列表没有使用自绘分组样式。");
            }

            if (!window.ChangesListUsesIdeaInputForTest)
            {
                throw new InvalidOperationException("Git 改动列表没有安装 IDEA 式复选框与键盘交互。");
            }

            if (window.VisibleGitChangeEntryCountForTest != 3
                || !window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles)
                || window.VisibleGitChangeEntryCountForTest != 2)
            {
                throw new InvalidOperationException("单击 Unversioned Files 分组箭头没有折叠文件行。");
            }

            if (!window.ToggleGitGroupWithEnterForTest(GitChangeGroup.UnversionedFiles)
                || window.VisibleGitChangeEntryCountForTest != 3)
            {
                throw new InvalidOperationException("Changes 分组按 Enter 后没有重新展开。");
            }

            if (!window.DoubleClickGitGroupTitleForTest(GitChangeGroup.UnversionedFiles)
                || window.VisibleGitChangeEntryCountForTest != 2
                || !window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles)
                || window.VisibleGitChangeEntryCountForTest != 3)
            {
                throw new InvalidOperationException("双击 Changes 分组标题或再次单击箭头没有正确切换折叠状态。");
            }

            if (!window.ClickGitFileCheckboxForTest("untracked.txt"))
            {
                throw new InvalidOperationException("单击 Git 文件复选框没有切换提交选择。");
            }


            if (window.GitSelectedFileCountForTest != 1
                || !window.CommitActionEnabledForTest
                || window.GitCommitChangeCountForTest != "1 modified")
            {
                throw new InvalidOperationException("勾选文件后提交按钮或提交计数没有同步更新。");
            }

            Assert.AreEqual(NativeCheckboxState.Checked, gitPanel.GroupCheckStateForTest(GitChangeGroup.UnversionedFiles));
            Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));
            Assert.AreEqual(NativeCheckboxState.Checked, gitPanel.GroupCheckStateForTest(GitChangeGroup.UnversionedFiles), "折叠分组后仍应显示全选状态");
            Assert.IsTrue(window.ClickGitGroupChevronForTest(GitChangeGroup.UnversionedFiles));

            if (!window.ToggleGitFileWithSpaceForTest("untracked.txt"))
            {
                throw new InvalidOperationException("选中 Git 文件后按 Space 没有切换提交选择。");
            }


            if (window.GitSelectedFileCountForTest != 0
                || window.CommitActionEnabledForTest
                || window.GitCommitChangeCountForTest.Length != 0)
            {
                throw new InvalidOperationException("取消最后一个文件勾选后提交按钮或提交计数没有同步更新。");
            }

            if (!window.CommitMessageHidesPermanentScrollBarForTest)
            {
                throw new InvalidOperationException("空提交信息区域仍显示永久滚动条，没有保持 IDEA 式轻量边缘。");
            }

            if (window.GitDiffVisibleForTest || !window.ActiveDocumentVisibleForTest)
            {
                throw new InvalidOperationException("提交概览没有保留当前文档。");
            }

            window.ToggleGitForTest();
            if (window.GitPanelVisibleForTest || !window.ProjectNavigationActiveForTest)
            {
                throw new InvalidOperationException("再次点击提交入口没有收起工具窗口并返回项目面。");
            }

            window.ToggleGitForTest();
            if (!window.GitPanelVisibleForTest || window.ProjectNavigationActiveForTest)
            {
                throw new InvalidOperationException("重新点击提交入口没有恢复提交工具窗口。");
            }

            int diffRequestsBeforeSingleClick = window.GitDiffRequestCountForTest;
            if (!await window.ClickGitFileForTestAsync("untracked.txt")
                || window.GitDiffVisibleForTest
                || window.GitDiffRequestCountForTest != diffRequestsBeforeSingleClick
                || !window.ActiveDocumentVisibleForTest)
            {
                throw new InvalidOperationException("首次单击 Changes 文件没有保持只选中、不打开 Diff 的 IDEA 交互。");
            }

            window.DelayNextGitDiffForTest(350);
            if (!await window.EnterGitFileForTestAsync("untracked.txt")
                || !window.GitDiffVisibleForTest
                || !window.GitDiffLoadingForTest
                || window.GitDiffChangeSummaryForTest != UiText.CalculatingDiff
                || window.ActiveDocumentVisibleForTest
                || !window.GitDiffUsesSeparateWindowForTest
                || !window.DocumentTabsVisibleForTest
                || !window.ActiveDocumentTabVisibleForTest
                || !window.ActiveTransientTabUsesPreviewTypographyForTest
                || window.WorkspaceGitDiffTabCountForTest != 1
                || !string.Equals(
                    window.PreviewGitDiffPathForTest,
                    "untracked.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("按 Enter 后没有在独立中央窗口打开未固定的临时 Diff，或临时标签错误替换了已有文件标签。");
            }

            NativeGitDiffGeometrySnapshot loadingGeometry = window.GitDiffGeometryForTest;
            int layoutCountWhileLoading = window.LayoutInvocationCountForTest;
            int gitPanelLayoutCountWhileLoading = window.GitPanelLayoutInvocationCountForTest;
            DateTime diffCompletionDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitDiffLoadingForTest && DateTime.UtcNow < diffCompletionDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitDiffLoadingForTest
                || window.StatusTextForTest == UiText.GeneratingDiff
                || window.GitDiffTitleForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("按 Enter 打开的 Changes Diff 没有清除加载状态。");
            }

            NativeGitDiffGeometrySnapshot completedGeometry = window.GitDiffGeometryForTest;
            if (!loadingGeometry.IsValid
                || loadingGeometry != completedGeometry
                || window.LayoutInvocationCountForTest != layoutCountWhileLoading
                || window.GitPanelLayoutInvocationCountForTest != gitPanelLayoutCountWhileLoading)
            {
                throw new InvalidOperationException(
                    $"Diff 异步完成后改变了主区域几何或重复布局。加载中={loadingGeometry}，完成后={completedGeometry}。");
            }

            if (!window.GitDiffUsesSingleMarginForTest
                || !window.GitDiffUsesSideBySideForTest
                || !window.GitDiffUsesCentralGutterForTest)
            {
                throw new InvalidOperationException("diff 没有按视觉稿默认显示双栏中央行号栏，或文本控件仍保留了未使用的辅助边栏。");
            }

            if (!window.GitDiffToolbarActionsCreatedForTest
                || !window.GitDiffSurfaceOrderForTest
                || !window.GitDiffSettingsButtonUsesIconForTest)
            {
                throw new InvalidOperationException("工作区 Diff 没有按视觉稿布局，或设置入口未使用可见的自绘齿轮图标。");
            }

            if (!window.CurrentFileContextForTest.Equals(UiText.CurrentFile, StringComparison.Ordinal)
                || !window.GitDiffPathForTest.Equals("untracked.txt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Diff 测试路径没有保持当前文件上下文。");
            }


            int documentCountBeforeClosingDiff = window.OpenDocumentCount;
            window.CloseActiveTabForTest();
            if (window.GitDiffVisibleForTest
                || !window.ActiveDocumentVisibleForTest
                || window.OpenDocumentCount != documentCountBeforeClosingDiff
                || !activePath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Ctrl+W 关闭临时 Diff 时误关了背后的正式文件或没有恢复原文档。");
            }

            if (!await window.SelectGitFileForTestAsync("untracked.txt")
                || !window.GitDiffVisibleForTest)
            {
                throw new InvalidOperationException("关闭临时 Diff 后无法再次从 Changes 打开同一文件。");
            }

            window.ShowFilesForTest();
            if (!window.GitDiffVisibleForTest
                || window.GitPanelVisibleForTest
                || window.ActiveDocumentVisibleForTest)
            {
                throw new InvalidOperationException("切换到 Project 工具窗时没有保留当前 Diff 标签和中央正文。");
            }

            window.ShowGitForTest();
            if (!window.GitDiffVisibleForTest || !window.GitPanelVisibleForTest)
            {
                throw new InvalidOperationException("从 Project 切回 Commit 工具窗时没有恢复当前 Diff。");
            }

            window.ShowHistoryForTest();
            DateTime historyDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((!window.HistoryPanelVisibleForTest || window.HistoryEntryCountForTest == 0)
                && DateTime.UtcNow < historyDeadline)
            {
                await Task.Delay(10);
            }

            if (!window.GitPanelVisibleForTest
                || !window.GitDiffVisibleForTest
                || !window.HistoryPanelVisibleForTest
                || !window.BottomPanelStartsAtEditorForTest)
            {
                throw new InvalidOperationException("打开底部 Git 历史时没有保持左侧 Commit 和当前 Diff 上下文。");
            }

            if (!window.HandleTabNavigationForTest()
                || !window.HistoryBranchFilterHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.HistoryBranchesListHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.HistoryFilterHasFocusForTest)
            {
                throw new InvalidOperationException("Git 历史工具窗口没有从分支筛选按视觉顺序经过引用树移动到提交筛选。");
            }

            int diffRequestsAfterFirstSelection = window.GitDiffRequestCountForTest;
            int diffPresentationNotificationsAfterFirstSelection = window.GitDiffPresentationNotificationCountForTest;
            int layoutCountAfterFirstSelection = window.LayoutInvocationCountForTest;
            int gitPanelLayoutCountAfterFirstSelection = window.GitPanelLayoutInvocationCountForTest;
            for (int index = 0; index < 10; index++)
            {
                if (!await window.ClickGitFileForTestAsync("untracked.txt"))
                {
                    throw new InvalidOperationException("重复选择当前 Changes 文件时丢失了当前选择。");
                }
            }

            if (window.GitDiffRequestCountForTest != diffRequestsAfterFirstSelection)
            {
                throw new InvalidOperationException("同一 Changes 文件连续单击十次后重复请求了 diff。");
            }

            if (window.GitDiffPresentationNotificationCountForTest
                != diffPresentationNotificationsAfterFirstSelection)
            {
                throw new InvalidOperationException("同一 Changes 文件连续单击十次后重复通知主窗口刷新临时 Diff 标签。");
            }

            if (window.LayoutInvocationCountForTest != layoutCountAfterFirstSelection)
            {
                throw new InvalidOperationException("同一 Changes 文件连续单击十次后触发了主窗口全量布局。");
            }

            if (window.GitPanelLayoutInvocationCountForTest != gitPanelLayoutCountAfterFirstSelection)
            {
                throw new InvalidOperationException("同一 Changes 文件连续单击十次后重排了 Commit 工具窗口。");
            }

            int populateCountBeforeRefresh = window.GitChangesPopulateCountForTest;
            for (int index = 0; index < 10; index++)
            {
                window.RequestGitRefreshForTest();
                DateTime stabilityRefreshDeadline = DateTime.UtcNow.AddSeconds(5);
                while (window.GitRefreshingForTest && DateTime.UtcNow < stabilityRefreshDeadline)
                {
                    await Task.Delay(10);
                }

                if (window.GitRefreshingForTest)
                {
                    throw new InvalidOperationException("Git 无变化刷新没有在五秒内完成。");
                }
            }

            if (window.GitChangesPopulateCountForTest != populateCountBeforeRefresh
                || window.GitDiffRequestCountForTest != diffRequestsAfterFirstSelection)
            {
                throw new InvalidOperationException("Git 无变化刷新重建了 Changes 列表或重新请求了当前 diff。");
            }

            window.RequestGitMetadataRefreshForTest();
            DateTime metadataRefreshDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitRefreshingForTest && DateTime.UtcNow < metadataRefreshDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitRefreshingForTest
                || window.GitChangesPopulateCountForTest != populateCountBeforeRefresh
                || window.GitDiffRequestCountForTest != diffRequestsAfterFirstSelection)
            {
                throw new InvalidOperationException("无变化 Git 元数据事件重建了 Changes 列表或重新请求了当前 diff。");
            }

            window.RequestGitRefreshForTest(Path.Combine(workspace, "unrelated.txt"));
            DateTime unrelatedRefreshDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitRefreshingForTest && DateTime.UtcNow < unrelatedRefreshDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitDiffRequestCountForTest != diffRequestsAfterFirstSelection)
            {
                throw new InvalidOperationException("无关文件变化错误地重新请求了当前 diff。");
            }

            window.RequestGitRefreshForTest(activePath);
            DateTime activeRefreshDeadline = DateTime.UtcNow.AddSeconds(5);
            while (window.GitRefreshingForTest && DateTime.UtcNow < activeRefreshDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitDiffRequestCountForTest != diffRequestsAfterFirstSelection + 1)
            {
                throw new InvalidOperationException("当前文件外部变化后没有重新请求当前 diff。");
            }

            string secondPath = Path.Combine(workspace, "second.txt");
            string thirdPath = Path.Combine(workspace, "third.txt");
            int changesResetCountBeforeAddedFiles = window.GitChangesListResetCountForTest;
            int changesDeltaCountBeforeAddedFiles = window.GitChangesListDeltaCountForTest;
            await File.WriteAllTextAsync(secondPath, "第二个文件\n");
            await File.WriteAllTextAsync(thirdPath, "第三个文件\n");
            window.RequestGitRefreshForTest(secondPath, thirdPath);
            DateTime addedFilesDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.GitRefreshingForTest || window.GitChangedFileCountForTest != 3)
                && DateTime.UtcNow < addedFilesDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitChangedFileCountForTest != 3
                || window.GitChangesListResetCountForTest != changesResetCountBeforeAddedFiles
                || window.GitChangesListDeltaCountForTest <= changesDeltaCountBeforeAddedFiles)
            {
                throw new InvalidOperationException("连续选择测试需要的三个 Changes 文件没有增量加载，或发生了整表重建。");
            }

            Task<bool> firstSelection = window.SelectGitFileForTestAsync("untracked.txt");
            Task<bool> secondSelection = window.SelectGitFileForTestAsync("second.txt");
            Task<bool> thirdSelection = window.SelectGitFileForTestAsync("third.txt");
            int layoutCountBeforeRapidSelection = window.LayoutInvocationCountForTest;
            int gitPanelLayoutCountBeforeRapidSelection = window.GitPanelLayoutInvocationCountForTest;
            await Task.WhenAll(firstSelection, secondSelection, thirdSelection);
            if (!thirdSelection.Result
                || !window.CurrentFileContextForTest.Equals(UiText.CurrentFile, StringComparison.Ordinal)
                || !window.GitDiffPathForTest.Equals("third.txt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("快速连续选择三个 Changes 文件后没有稳定显示第三个文件。");
            }

            if (!string.Equals(
                    window.GitSelectedChangedFilePathForTest,
                    "third.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("快速连续选择后 Changes 列表的视觉选中项没有保持最后一个文件。");
            }

            if (window.LayoutInvocationCountForTest != layoutCountBeforeRapidSelection)
            {
                throw new InvalidOperationException("快速连续选择 Changes 文件时触发了主窗口全量布局。");
            }

            if (window.GitPanelLayoutInvocationCountForTest != gitPanelLayoutCountBeforeRapidSelection)
            {
                throw new InvalidOperationException("快速连续选择 Changes 文件时重排了 Commit 工具窗口。");
            }

            string trackedPath = Path.Combine(workspace, "tracked.txt");
            await window.OpenDocumentForTestAsync(trackedPath);
            window.ShowGitForTest();
            if (!await window.ClickGitFileForTestAsync("second.txt")
                || window.GitDiffVisibleForTest
                || !window.ActiveDocumentVisibleForTest
                || !string.Equals(
                    window.PreviewGitDiffPathForTest,
                    "second.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("已有后台 Diff 时单击 Changes 文件抢占了普通文件，或没有更新后台临时标签。");
            }

            DateTime backgroundDiffDeadline = DateTime.UtcNow.AddSeconds(3);
            while (window.GitDiffLoadingForTest && DateTime.UtcNow < backgroundDiffDeadline)
            {
                await Task.Delay(10);
            }

            if (window.GitDiffLoadingForTest
                || window.GitDiffVisibleForTest
                || !window.ActiveDocumentVisibleForTest
                || !string.Equals(
                    window.PreviewGitDiffPathForTest,
                    "second.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("后台更新 Changes Diff 时没有保持普通文件正文和临时标签状态。");
            }

            int diffRequestsBeforeDoubleClick = window.GitDiffRequestCountForTest;
            if (!window.DoubleClickGitFileForTest("untracked.txt"))
            {
                throw new InvalidOperationException("双击 Changes 文件没有发送完整的选择与打开消息。");
            }

            DateTime doubleClickDeadline = DateTime.UtcNow.AddSeconds(3);
            while ((window.WorkspaceGitDiffTabCountForTest != 1
                    || window.GitDiffLoadingForTest)
                && DateTime.UtcNow < doubleClickDeadline)
            {
                await Task.Delay(10);
            }

            if (!window.GitDiffVisibleForTest
                || window.WorkspaceGitDiffTabCountForTest != 1
                || !window.ActiveTransientTabUsesPreviewTypographyForTest
                || window.GitDiffRequestCountForTest != diffRequestsBeforeDoubleClick + 1
                || window.PreviewGitDiffPathForTest != "untracked.txt"
                || !trackedPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("双击 Changes 文件没有复用唯一 Diff 标签，或重复请求了差异。");
            }

            const string statusBeforeBackgroundDiff = "当前文件提示";
            window.SetStatusForTest(statusBeforeBackgroundDiff);
            window.DelayNextGitDiffForTest(350);
            if (!await window.SelectGitFileForTestAsync("second.txt")
                || !window.GitDiffVisibleForTest
                || window.WorkspaceGitDiffTabCountForTest != 1
                || !string.Equals(window.PreviewGitDiffPathForTest, "second.txt", StringComparison.OrdinalIgnoreCase)
                || window.VisibleEditorTabCountForTest != window.OpenDocumentCount + 1
                || window.StatusTextForTest != statusBeforeBackgroundDiff)
            {
                throw new InvalidOperationException("已有 Diff 时单击另一个 Changes 文件没有更新唯一比较标签，或覆盖了其他状态栏提示。");
            }

            IReadOnlyList<string> topBarBranches = await window.LoadTopBarBranchesForTestAsync();
            if (!window.BranchSwitchCancelActionCreatedForTest)
            {
                throw new InvalidOperationException("顶部分支切换没有创建用户可取消的操作入口。");
            }
            if (!topBarBranches.Contains("feature", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("顶部分支菜单没有读取真实本地分支。");
            }

            if (!await window.ShowBranchPopupForTestAsync()
                || window.BranchPopupForTest is not { SearchHasFocusForTest: true } branchPopup
                || !branchPopup.RowLabelsForTest.Contains(UiText.UpdateProject)
                || !branchPopup.RowLabelsForTest.Contains(UiText.LocalReferences)
                || !branchPopup.SelectReferenceForTest(window.CurrentBranchLabelForTest)
                || !branchPopup.ActionLabelsForTest.Contains(UiText.CompareWithWorkspace))
            {
                throw new InvalidOperationException("顶部分支入口没有打开可搜索并带二级动作的 IDEA 风格弹层。");
            }

            if (!branchPopup.ActionsPopupVisibleForTest)
            {
                throw new InvalidOperationException("分支弹层选中引用后没有显示二级动作弹层。");
            }

            if (!window.HandleTabNavigationForTest())
            {
                throw new InvalidOperationException("分支弹层从搜索框前进 Tab 时没有处理焦点移动。");
            }

            if (!branchPopup.ReferencesListHasFocusForTest)
            {
                throw new InvalidOperationException("分支弹层前进 Tab 后焦点没有到达引用列表。");
            }

            if (!window.HandleTabNavigationForTest())
            {
                throw new InvalidOperationException(
                    $"分支弹层从引用列表前进 Tab 时没有处理焦点移动；二级弹层可见={branchPopup.ActionsPopupVisibleForTest}，列表可见={branchPopup.ActionsListVisibleForTest}，搜索焦点={branchPopup.SearchHasFocusForTest}，引用焦点={branchPopup.ReferencesListHasFocusForTest}，动作焦点={branchPopup.ActionsListHasFocusForTest}。");
            }

            if (!branchPopup.ActionsListHasFocusForTest)
            {
                throw new InvalidOperationException("分支弹层前进 Tab 后焦点没有到达二级动作列表。");
            }

            if (!window.HandleTabNavigationForTest(backwards: true)
                || !branchPopup.ReferencesListHasFocusForTest)
            {
                throw new InvalidOperationException("分支弹层反向 Tab 没有回到引用列表。");
            }

            window.CloseBranchPopupForTest();

            if (!await window.SwitchTopBarBranchForTestAsync("feature")
                || !window.CurrentBranchLabelForTest.Equals("feature", StringComparison.OrdinalIgnoreCase)
                || window.BranchSwitchCancelActionVisibleForTest)
            {
                throw new InvalidOperationException("顶部分支菜单没有通过可取消的本机 Git 操作切换并更新当前分支。");
            }

            window.ShowGitForTest();
            if (!window.GitDiffVisibleForTest || window.ActiveDocumentVisibleForTest)
            {
                throw new InvalidOperationException("切换分支后没有保留当前 Diff 编辑上下文。");
            }

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await File.WriteAllTextAsync(Path.Combine(workspace, "external.txt"), "外部变化\n");
            DateTime refreshDeadline = DateTime.UtcNow.AddSeconds(2);
            while (window.GitChangedFileCountForTest != 4 && DateTime.UtcNow < refreshDeadline)
            {
                await Task.Delay(10);
            }

            stopwatch.Stop();
            if (window.GitChangedFileCountForTest != 4)
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
        TaskCompletionSource<Exception?> completion,
        ConcurrentQueue<string> stages)
    {
        try
        {
            stages.Enqueue("打开工作区");
            bool opened = await window.OpenWorkspaceAsync(workspace);
            if (!opened || !workspace.Equals(window.WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("原生窗口未能打开工作区。");
            }

            if (!await window.ClickTreePathForTestAsync(documentPath))
            {
                throw new InvalidOperationException("项目树没有响应文件名称的真实单击选择动作。");
            }

            stages.Enqueue("验证单击文件只选择");
            await Task.Delay(100);
            if (window.OpenDocumentCount != 0
                || !documentPath.Equals(window.SelectedTreePathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("项目树单击文件后错误地打开了文档标签。");
            }

            await window.OpenSelectedTreeNodeForTestAsync();
            if (window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("项目树显式打开文件后没有创建只读文本标签。");
            }

            NativeSelectionFocusAssertions.Verify(window, window, "_fileTree", tree: true);
            Assert.AreEqual(1, window.OpenDocumentCount, "焦点变化不能重新打开文件。");

            string secondDocumentPath = Path.Combine(workspace, "第二份.txt");
            if (!window.SelectTreeContextTargetForTest(secondDocumentPath)
                || window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("项目树右键目标没有切换到鼠标下的文件，或错误地打开了文件。");
            }

            string directoryPath = Path.Combine(workspace, "目录");
            stages.Enqueue("真实单击目录展开标");
            if (!window.SelectTreePathForTest(directoryPath)
                || !await window.ClickSelectedTreeDisclosureForTestAsync())
            {
                throw new InvalidOperationException("项目树没有响应展开标的真实单击动作。");
            }

            DateTime expandDeadline = DateTime.UtcNow.AddSeconds(2);
            while ((window.ExpandedDirectoryCountForTest < 2 || !window.SelectedTreeNodeLoadedForTest)
                && DateTime.UtcNow < expandDeadline)
            {
                await Task.Delay(20);
            }
            if (window.ExpandedDirectoryCountForTest < 2 || !window.SelectedTreeNodeLoadedForTest)
            {
                throw new InvalidOperationException("单次目录展开动作没有完成所选目录的按需加载。");
            }

            stages.Enqueue("验证目录名称双击");
            bool doubleClickHandled = await window.DoubleClickSelectedTreeNameForTestAsync();
            int expandedAfterDoubleClick = window.ExpandedDirectoryCountForTest;
            if (!doubleClickHandled || expandedAfterDoubleClick != 1)
            {
                throw new InvalidOperationException(
                    $"项目树没有响应目录名称双击折叠动作，处理结果 {doubleClickHandled}，展开目录数 {expandedAfterDoubleClick}。");
            }

            stages.Enqueue("验证目录键盘展开");
            if (!await window.ActivateSelectedTreeNodeWithEnterForTestAsync()
                || window.ExpandedDirectoryCountForTest != 2
                || !await window.ActivateSelectedTreeNodeWithEnterForTestAsync()
                || window.ExpandedDirectoryCountForTest != 1)
            {
                throw new InvalidOperationException("项目树选中目录后按 Enter 没有按 IDEA 逻辑切换展开状态。");
            }

            stages.Enqueue("验证文档与界面布局");
            await window.OpenDocumentForTestAsync(documentPath);

            if (!window.DocumentTabsUseOwnerDrawForTest)
            {
                throw new InvalidOperationException("文档标签没有使用自绘标签条。");
            }

            if (!window.TopBarProductHierarchyForTest)
            {
                throw new InvalidOperationException("顶部栏没有保持产品标识、主菜单、项目身份和分支的层级顺序。");
            }

            if (!window.TopBarControlsVerticallyAlignedForTest)
            {
                throw new InvalidOperationException("顶部栏的产品标识、主菜单、项目身份和分支没有保持同一视觉中心。");
            }

            if (!window.MainSurfaceSpacingMatchesForTest)
            {
                throw new InvalidOperationException("项目卡片与文档卡片没有保持统一的 IDEA 式面板间距。");
            }

            if (window.ToolTipCountForTest < 12)
            {
                throw new InvalidOperationException(
                    $"主窗口 Tooltip 创建状态 {window.ToolTipCreatedForTest}，只注册了 {window.ToolTipCountForTest} 个悬停说明。");
            }

            int initialProjectWidth = window.ProjectPanelWidthForTest;
            if (!window.DragProjectSplitterForTest(NativeTheme.Scale(24))
                || window.ProjectPanelWidthForTest <= initialProjectWidth
                || !window.MainSurfaceSpacingMatchesForTest)
            {
                throw new InvalidOperationException("项目工具窗口不能像 IDEA 一样调整宽度，或调整后破坏了面板间距。");
            }

            if (!window.ActiveDocumentVisibleForTest)
            {
                throw new InvalidOperationException("已打开标签的正文窗口不可见或没有有效尺寸。");
            }

            if (!window.ActiveDocumentTextToolbarWithinBoundsForTest
                || !window.ActiveDocumentToolTipsCreatedForTest)
            {
                throw new InvalidOperationException("普通文本工具栏缺少紧凑图标入口、悬停说明或有效边界。");
            }

            int contentTopBeforeFind = window.ActiveDocumentContentTopForTest;
            window.ShowActiveDocumentFindForTest("行");
            if (!window.ActiveDocumentFindOverlayVisibleForTest
                || !window.ActiveDocumentFindOverlayWithinBoundsForTest
                || window.ActiveDocumentContentTopForTest != contentTopBeforeFind + NativeDocumentView.FindOverlayHeightForTest
                || window.ActiveDocumentFindStatusForTest != UiText.FindResults(2))
            {
                throw new InvalidOperationException(
                    "当前文件查找没有在正文顶部占行显示，或没有显示当前项与总数。");
            }

            if (!window.ActiveFindEditHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.ActiveFindMatchCaseHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.ActiveFindWholeWordHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.ActiveFindMatchCaseHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.ActiveFindEditHasFocusForTest)
            {
                throw new InvalidOperationException("当前文件查找条没有在输入、选项和按钮之间按视觉顺序循环焦点。");
            }

            _ = window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape);
            if (window.ActiveDocumentFindOverlayVisibleForTest
                || window.ActiveDocumentContentTopForTest != contentTopBeforeFind
                || !window.ActiveDocumentHasFocusForTest)
            {
                throw new InvalidOperationException("关闭当前文件查找后没有保持正文边界或恢复文档焦点。");
            }

            if (!window.HandleTabNavigationForTest())
            {
                throw new InvalidOperationException($"文档区从正文前进 Tab 时没有处理焦点移动：{window.ActiveNavigationFocusForTest}。");
            }

            if (!window.ActiveDocumentTabsHasFocusForTest)
            {
                throw new InvalidOperationException("文档区从正文前进 Tab 后焦点没有到达标签栏。");
            }

            if (!window.HandleTabNavigationForTest())
            {
                throw new InvalidOperationException($"文档区从标签栏前进 Tab 时没有处理焦点移动：{window.ActiveNavigationFocusForTest}。");
            }

            if (!window.ActiveDocumentHasFocusForTest)
            {
                throw new InvalidOperationException("文档区从标签栏前进 Tab 后焦点没有回到工具栏或正文。");
            }

            if (!window.HandleTabNavigationForTest(backwards: true)
                || !window.ActiveDocumentTabsHasFocusForTest)
            {
                throw new InvalidOperationException(
                    $"文档区反向 Tab 没有从工具栏或正文回到标签栏：标签焦点={window.ActiveDocumentTabsHasFocusForTest}，"
                    + $"换行焦点={window.ActiveWordWrapHasFocusForTest}，空白焦点={window.ActiveWhitespaceHasFocusForTest}，"
                    + $"查找焦点={window.ActiveFindButtonHasFocusForTest}，正文焦点={window.ActiveDocumentHasFocusForTest}，"
                    + $"实际焦点={window.ActiveNavigationFocusForTest}，控件={window.ActiveNavigationControlsStateForTest}。");
            }

            if (!window.HandleTabNavigationForTest(backwards: true)
                || !window.ActiveDocumentHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.ActiveDocumentTabsHasFocusForTest)
            {
                throw new InvalidOperationException("文档区反向 Tab 从标签栏进入正文后，无法按正向 Tab 返回标签栏。");
            }

            window.ShowFilesForTest();
            if (!window.ProjectTreeHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.ProjectLocateActionHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.ProjectCollapseActionHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.ProjectLocateActionHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.ProjectTreeHasFocusForTest)
            {
                throw new InvalidOperationException("项目工具窗口没有在当前区域内按视觉顺序循环键盘焦点。");
            }

            window.ShowGitForTest();
            if (!window.GitChangesListHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.GitAmendActionHasFocusForTest
                || !window.HandleTabNavigationForTest()
                || !window.GitCommitMessageHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.GitAmendActionHasFocusForTest)
            {
                throw new InvalidOperationException("提交工具窗口没有在列表、Amend 和提交信息之间循环键盘焦点。");
            }

            window.ShowFilesForTest();

            int documentTabsLeftWithProject = window.DocumentTabsLeftForTest;
            window.HideProjectForTest();
            if (window.ProjectPanelVisibleForTest
                || window.ProjectNavigationActiveForTest
                || !window.ActiveDocumentVisibleForTest
                || window.DocumentTabsLeftForTest >= documentTabsLeftWithProject)
            {
                throw new InvalidOperationException("再次点击项目入口没有像 IDEA 一样收起项目工具窗口并扩展正文。");
            }

            window.ShowSearchForTest(WorkspaceSearchMode.Text, "外部更新");
            window.ToggleWorkspaceSearchForTest();
            if (window.ProjectPanelVisibleForTest
                || window.ProjectNavigationActiveForTest
                || window.DocumentTabsLeftForTest >= documentTabsLeftWithProject)
            {
                throw new InvalidOperationException("关闭搜索后没有恢复进入搜索前已收起的项目工具窗口状态。");
            }

            window.ShowGitForTest();
            if (!window.GitPanelVisibleForTest || window.ProjectPanelVisibleForTest)
            {
                throw new InvalidOperationException("项目工具窗口收起后打开提交面板时错误恢复了项目树。");
            }

            window.ToggleGitForTest();
            if (window.GitPanelVisibleForTest
                || window.ProjectPanelVisibleForTest
                || window.ProjectNavigationActiveForTest
                || window.DocumentTabsLeftForTest >= documentTabsLeftWithProject)
            {
                throw new InvalidOperationException(
                    "关闭提交面板后没有恢复进入前已收起的项目工具窗口状态："
                    + $"提交={window.GitPanelVisibleForTest}，项目={window.ProjectPanelVisibleForTest}，"
                    + $"导航={window.ProjectNavigationActiveForTest}，标签左边={window.DocumentTabsLeftForTest}，"
                    + $"展开时标签左边={documentTabsLeftWithProject}。");
            }

            window.ToggleProjectForTest();
            if (!window.ProjectPanelVisibleForTest
                || !window.ProjectNavigationActiveForTest
                || window.DocumentTabsLeftForTest != documentTabsLeftWithProject)
            {
                throw new InvalidOperationException("重新点击项目入口没有恢复项目工具窗口及原正文布局。");
            }

            if (!window.ActiveDocumentIsReadOnly)
            {
                throw new InvalidOperationException("普通文本控件没有保持只读边界。");
            }

            await window.ShowBlameForTestAsync(
                documentPath,
                [
                    new(
                        1,
                        new string('a', 40),
                        "测试作者",
                        "author@example.com",
                        DateTimeOffset.UnixEpoch,
                        "初始化",
                        "说明.txt",
                        "第一行"),
                ]);
            if (!window.ActiveDocumentIsShowingBlameForTest || !window.ActiveDocumentIsReadOnly)
            {
                throw new InvalidOperationException("Blame 没有以内嵌归属栏显示，或破坏了普通文件只读边界。");
            }

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            stages.Enqueue("验证外部文件变化");
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
            stages.Enqueue("验证文件名和正文搜索");
            await WaitForSearchResultAsync(window);
            if (!window.SearchPanelVisibleForTest
                || !window.SearchPanelUsesRoundedChromeForTest
                || window.SearchNavigationActiveForTest
                || !window.ProjectNavigationActiveForTest
                || window.SearchPanelLogicalWidthForTest != 730
                || window.SearchPanelPreferredHeightForTest != 122
                || !window.SearchPanelShortcutVisibleForTest
                || window.SearchPanelCloseButtonVisibleForTest
                || window.SearchPanelNoticeVisibleForTest
                || !window.SearchPanelUsesSingleLineResultsForTest
                || window.SearchPanelPlaceholderForTest != UiText.QuickOpenPlaceholder
                || !window.SearchPanelUsesFocusedSearchFieldChromeForTest)
            {
                throw new InvalidOperationException(
                    "文件名搜索没有按视觉稿显示 730 像素宽度、Ctrl+P、单行结果，或错误显示了关闭按钮与统计尾栏。");
            }

            window.ShowFilesForTest();
            if (window.SearchPanelVisibleForTest || !window.ProjectNavigationActiveForTest)
            {
                throw new InvalidOperationException("点击项目入口没有关闭搜索并恢复项目工作面。");
            }


            window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "第二份");
            await WaitForSearchResultAsync(window);
            int formalDocumentCountBeforeSearchPreview = window.OpenDocumentCount;
            if (!window.PreviewFirstSearchResultForTest())
            {
                throw new InvalidOperationException("快速打开结果没有响应单击预览。");
            }

            DateTime searchPreviewDeadline = DateTime.UtcNow.AddSeconds(2);
            while ((!window.ActiveDocumentIsPreviewForTest
                    || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
                && DateTime.UtcNow < searchPreviewDeadline)
            {
                await Task.Delay(10);
            }

            if (!window.SearchPanelVisibleForTest
                || !window.ActiveDocumentIsPreviewForTest
                || !window.ActiveDocumentTabUsesPreviewTypographyForTest
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || window.OpenDocumentCount != formalDocumentCountBeforeSearchPreview
                || window.SearchResultListDeltaCountForTest < 1)
            {
                throw new InvalidOperationException("快速打开单击没有保持弹层、使用可辨认的临时标签，或搜索结果仍在整表重置。");
            }

            if (!window.SearchResultListHasFocusForTest
                || !window.HandleTabNavigationForTest(backwards: true)
                || !window.SearchPanelUsesFocusedSearchFieldChromeForTest
                || !window.HandleTabNavigationForTest()
                || !window.SearchResultListHasFocusForTest)
            {
                throw new InvalidOperationException("快速打开弹层没有在输入框与结果列表之间按视觉顺序循环焦点。");
            }

            if (!window.OpenFirstSearchResultWithEnterForTest())
            {
                throw new InvalidOperationException("文件搜索结果没有响应搜索框中的 Enter。");
            }

            DateTime searchOpenDeadline = DateTime.UtcNow.AddSeconds(2);
            while ((window.SearchPanelVisibleForTest
                    || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
                && DateTime.UtcNow < searchOpenDeadline)
            {
                await Task.Delay(10);
            }
            if (window.SearchPanelVisibleForTest
                || window.ActiveDocumentIsPreviewForTest
                || window.ActiveDocumentTabUsesPreviewTypographyForTest
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || window.OpenDocumentCount != formalDocumentCountBeforeSearchPreview + 1)
            {
                throw new InvalidOperationException("搜索框按 Enter 后没有将临时预览提升为正式标签并关闭搜索面板。");
            }

            window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "连续预览");
            await WaitForSearchResultAsync(window);
            if (window.SearchResultCountForTest < 3)
            {
                throw new InvalidOperationException("连续搜索预览测试没有得到三个文件结果。");
            }

            string finalPreviewPath = window.SearchResultPathForTest(2)
                ?? throw new InvalidOperationException("连续搜索预览缺少最终结果路径。");
            string expectedPreviewText = await File.ReadAllTextAsync(finalPreviewPath);
            int formalCountBeforeRapidPreview = window.OpenDocumentCount;
            if (!window.PreviewSearchResultForTest(0)
                || !window.PreviewSearchResultForTest(1)
                || !window.PreviewSearchResultForTest(2))
            {
                throw new InvalidOperationException("快速连续单击搜索结果没有完整触发三次选择。");
            }

            DateTime rapidPreviewDeadline = DateTime.UtcNow.AddSeconds(2);
            while ((!finalPreviewPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(window.ActiveDocumentText, expectedPreviewText, StringComparison.Ordinal))
                && DateTime.UtcNow < rapidPreviewDeadline)
            {
                await Task.Delay(10);
            }
            await Task.Delay(150);
            if (!window.SearchPanelVisibleForTest
                || !finalPreviewPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(window.ActiveDocumentText, expectedPreviewText, StringComparison.Ordinal)
                || window.PreviewDocumentCountForTest != 1
                || window.OpenDocumentCount != formalCountBeforeRapidPreview)
            {
                throw new InvalidOperationException("快速连续搜索预览没有稳定停在最后结果，或旧读取结果覆盖了最新选择。");
            }

            if (!window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape)
                || !window.ActiveDocumentIsPreviewForTest)
            {
                throw new InvalidOperationException("关闭快速打开后错误关闭了当前临时预览标签。");
            }
            window.CloseActiveTabForTest();
            if (window.PreviewDocumentCountForTest != 0
                || window.OpenDocumentCount != formalCountBeforeRapidPreview)
            {
                throw new InvalidOperationException("关闭临时预览标签时错误改变了正式标签集合。");
            }

            window.ShowSearchForTest(WorkspaceSearchMode.Text, "外部更新");
            await WaitForSearchResultAsync(window);
            window.ToggleWorkspaceSearchForTest();
            if (window.SearchPanelVisibleForTest
                || window.SearchNavigationActiveForTest
                || !window.ProjectNavigationActiveForTest)
            {
                throw new InvalidOperationException("再次点击搜索入口没有收起搜索工作面。");
            }

            window.ShowFilesForTest();
            window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "说明");
            await WaitForSearchResultAsync(window);
            if (!window.HandleApplicationShortcutForTest(NativeMethods.VirtualKeyEscape))
            {
                throw new InvalidOperationException("快速打开弹层没有响应 Esc。");
            }
            if (!window.ProjectTreeHasFocusForTest)
            {
                throw new InvalidOperationException("按 Esc 或关闭快速打开后没有恢复打开前的项目树焦点。");
            }

            await window.OpenDocumentForTestAsync(Path.Combine(workspace, "第二份.txt"));
            if (window.OpenDocumentCount != 2 || window.VisibleDocumentTabCloseCountForTest != 1)
            {
                throw new InvalidOperationException("多标签模式没有只为当前标签显示关闭入口。");
            }

            if (!await window.LeftClickDocumentTabForTestAsync(0)
                || !documentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || !await window.LeftClickDocumentTabForTestAsync(1)
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("文档标签没有响应 IDEA 式左键切换动作。");
            }

            if (!window.FocusDocumentTabsForTest()
                || !window.HandleDocumentTabsShortcutForTest(NativeMethods.VirtualKeyRight)
                || !documentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || !window.HandleDocumentTabsShortcutForTest(NativeMethods.VirtualKeyLeft)
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || !window.HandleDocumentTabsShortcutForTest(NativeMethods.VirtualKeyEnter)
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("文档标签没有按 IDEA 式响应左右方向键和 Enter，或没有保持活动标签。");
            }

            if (!window.HoverDocumentTabForTest(0) || window.VisibleDocumentTabCloseCountForTest != 2)
            {
                throw new InvalidOperationException("鼠标悬停非活动标签后没有像 IDEA 一样显示关闭入口。");
            }

            window.LeaveDocumentTabsForTest();
            if (window.VisibleDocumentTabCloseCountForTest != 1)
            {
                throw new InvalidOperationException("鼠标离开标签栏后仍残留悬停关闭入口。");
            }

            if (!window.DocumentTabsUseIdeaMouseInputForTest
                || !window.MiddleClickDocumentTabForTest(0)
                || window.OpenDocumentCount != 1)
            {
                throw new InvalidOperationException("文档标签没有响应 IDEA 风格的中键关闭动作。");
            }

            await window.OpenDocumentForTestAsync(documentPath);
            if (!window.HandleApplicationShortcutForTest('W', control: true)
                || window.OpenDocumentCount != 1
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Ctrl+W 没有关闭当前标签并保留相邻标签。");
            }

            await window.OpenDocumentForTestAsync(documentPath);
            window.CloseOtherDocumentsForTest(0);
            if (window.OpenDocumentCount != 1
                || !secondDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("关闭其他标签没有保留指定标签。");
            }

            await window.OpenDocumentForTestAsync(documentPath);
            window.CloseAllDocumentsForTest();
            if (window.OpenDocumentCount != 0 || window.ActiveDocumentPathForTest is not null)
            {
                throw new InvalidOperationException("关闭全部标签后仍残留活动文档。");
            }

            stages.Enqueue("完成");
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

    private static async Task VerifyWorkspaceRestoreAndCloseAsync(
        MainWindow window,
        string documentPath,
        string lazyDocumentPath,
        int expectedDocumentCount,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while ((window.OpenDocumentCount == 0
                    || !documentPath.Equals(window.SelectedTreePathForTest, StringComparison.OrdinalIgnoreCase))
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            if (window.OpenDocumentCount != expectedDocumentCount)
            {
                throw new InvalidOperationException("工作区没有恢复完整标签顺序。");
            }

            if (window.LoadedDocumentCountForTest != 1)
            {
                throw new InvalidOperationException(
                    $"恢复 {expectedDocumentCount} 个标签时创建了 {window.LoadedDocumentCountForTest} 个文件视图，预期只加载活动标签。");
            }

            if (!window.RestoreLayoutUsedLoadingPlaceholderForTest)
            {
                throw new InvalidOperationException("恢复标签的首帧布局没有先显示文件读取占位。");
            }

            if (!window.ActiveDocumentTabVisibleForTest)
            {
                throw new InvalidOperationException("恢复大量标签后活动标签没有保持在可见标签区。");
            }

            int lazyDocumentIndex = expectedDocumentCount - 2;
            bool lazyWasLoadedBeforeClick = window.IsDocumentLoadedForTest(lazyDocumentIndex);
            bool lazyClickCompleted = await window.LeftClickDocumentTabForTestAsync(lazyDocumentIndex);
            bool lazyIsActive = lazyDocumentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase);
            bool lazyLoadCountIsExpected = window.LoadedDocumentCountForTest == 2;
            if (lazyWasLoadedBeforeClick || !lazyClickCompleted || !lazyIsActive || !lazyLoadCountIsExpected)
            {
                throw new InvalidOperationException(
                    $"首次选择恢复标签时没有只按需加载该文件视图。点击完成={lazyClickCompleted}，"
                    + $"点击前已加载={lazyWasLoadedBeforeClick}，活动路径={window.ActiveDocumentPathForTest ?? "空"}，"
                    + $"预期路径={lazyDocumentPath}，已加载数量={window.LoadedDocumentCountForTest}。");
            }

            if (!await window.LeftClickDocumentTabForTestAsync(expectedDocumentCount - 1)
                || !documentPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || window.LoadedDocumentCountForTest != 2)
            {
                throw new InvalidOperationException("切回已加载的恢复标签时重复创建了文件视图。");
            }

            if (!documentPath.Equals(window.SelectedTreePathForTest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"恢复后文件树选中 {window.SelectedTreePathForTest ?? "空项"}，预期为活动标签 {documentPath}。");
            }

            if (!window.ProjectHeaderActionsCreatedForTest)
            {
                throw new InvalidOperationException("项目工具窗口没有创建可交互的定位、折叠与更多操作。");
            }

            window.CollapseProjectTreeForTest();
            if (window.ExpandedDirectoryCountForTest != 1)
            {
                throw new InvalidOperationException("折叠全部目录后没有只保留工作区根目录展开。");
            }

            await window.LocateActiveFileForTestAsync();
            if (!documentPath.Equals(window.SelectedTreePathForTest, StringComparison.OrdinalIgnoreCase)
                || window.ExpandedDirectoryCountForTest < 2)
            {
                throw new InvalidOperationException("定位当前文件没有重新展开父目录并选中活动文档。");
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

    private static void RunRapidPreviewSwitchSmoke(
        string settingsPath,
        string workspace,
        string firstPath,
        string secondPath,
        string thirdPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = SwitchMarkdownPreviewsAndCloseAsync(
                window,
                workspace,
                firstPath,
                secondPath,
                thirdPath,
                completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult(exception);
        }
    }

    private static async Task SwitchMarkdownPreviewsAndCloseAsync(
        MainWindow window,
        string workspace,
        string firstPath,
        string secondPath,
        string thirdPath,
        TaskCompletionSource<Exception?> completion)
    {
        try
        {
            if (!await window.OpenWorkspaceAsync(workspace))
            {
                throw new InvalidOperationException("快速预览测试工作区未能打开。");
            }

            await window.OpenDocumentForTestAsync(firstPath);
            await window.OpenDocumentForTestAsync(secondPath);
            await window.OpenDocumentForTestAsync(thirdPath);
            await WaitForActiveMarkdownPreviewAsync(window, thirdPath);

            await window.OpenDocumentForTestAsync(firstPath);
            await WaitForActiveMarkdownPreviewAsync(window, firstPath);
            int closedBrowserProcessId = window.CloseActiveMarkdownPreviewForTest();
            if (closedBrowserProcessId <= 0)
            {
                throw new InvalidOperationException("切回首个 Markdown 标签后没有可关闭的预览进程。");
            }

            await window.OpenDocumentForTestAsync(thirdPath);
            await window.OpenDocumentForTestAsync(firstPath);
            await Task.Delay(300);
            if (window.ActiveMarkdownPreviewLoading
                || window.ActiveMarkdownPreviewReady
                || window.ActiveMarkdownBrowserProcessId != 0)
            {
                throw new InvalidOperationException("用户选择原文后，跨标签返回时被错误地重新打开预览。");
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

    private static async Task WaitForActiveMarkdownPreviewAsync(MainWindow window, string expectedPath)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while ((!expectedPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
                || !window.ActiveMarkdownPreviewReady)
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        if (!expectedPath.Equals(window.ActiveDocumentPathForTest, StringComparison.OrdinalIgnoreCase)
            || !window.ActiveMarkdownPreviewReady
            || window.ActiveMarkdownPreviewLoading)
        {
            throw new InvalidOperationException(
                $"活动 Markdown 标签没有在十秒内完成预览：{Path.GetFileName(expectedPath)}。{window.ActiveMarkdownPreviewError}");
        }
    }

    private static void RunActivePreviewWindowCloseSmoke(
        string settingsPath,
        string workspace,
        string markdownPath,
        TaskCompletionSource<(Exception? Error, int BrowserProcessId)> completion)
    {
        try
        {
            using MainWindow window = new(new SettingsStore(settingsPath), new());
            window.Show();
            window.Post(() => _ = OpenActivePreviewAndCloseWindowAsync(window, workspace, markdownPath, completion));
            _ = MainWindow.RunMessageLoop();
        }
        catch (Exception exception)
        {
            completion.TrySetResult((exception, 0));
        }
    }

    private static async Task OpenActivePreviewAndCloseWindowAsync(
        MainWindow window,
        string workspace,
        string markdownPath,
        TaskCompletionSource<(Exception? Error, int BrowserProcessId)> completion)
    {
        int browserProcessId = 0;
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

            browserProcessId = window.ActiveMarkdownBrowserProcessId;
            if (browserProcessId <= 0)
            {
                throw new InvalidOperationException($"Markdown 预览未能创建浏览器进程。{window.ActiveMarkdownPreviewError}");
            }

            window.Close();
            completion.TrySetResult((null, browserProcessId));
        }
        catch (Exception exception)
        {
            completion.TrySetResult((exception, browserProcessId));
            window.Close();
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
                    throw new InvalidOperationException(
                        $"第 {cycle + 1} 次内置终端没有创建完整按需资源："
                        + $"面板={window.TerminalCreatedForTest}，运行={window.TerminalRunningForTest}，"
                        + $"WebView2={browserProcessId}，Shell={shellProcessId}。");
                }

                if (window.TerminalHeaderHeightForTest != NativeTheme.Scale(38))
                {
                    throw new InvalidOperationException("终端标题栏高度没有对齐底部工具窗口视觉基线。");
                }

                if (!window.TerminalSessionCloseActionCreatedForTest
                    || !window.TerminalHideActionCreatedForTest
                    || !window.TerminalMoreActionCreatedForTest)
                {
                    throw new InvalidOperationException("终端没有创建会话关闭、工具窗口隐藏和更多操作入口。");
                }

                window.ShowTerminalMoreMenuForTest();
                if (!window.TerminalMoreMenuVisibleForTest)
                {
                    throw new InvalidOperationException("终端更多入口没有打开自绘菜单。");
                }
                window.DismissContextMenuForTest();

                if (cycle == 0)
                {
                    window.ShowGitForTest();
                    window.ShowTerminalForTest();
                    if (!window.GitPanelVisibleForTest
                        || window.FileTreeVisibleForTest
                        || !window.TerminalPanelVisibleForTest)
                    {
                        throw new InvalidOperationException("从 Commit 打开终端后没有保留左侧提交工具窗。");
                    }

                    window.ToggleTerminalForTest();
                    if (window.TerminalPanelVisibleForTest
                        || !window.TerminalRunningForTest
                        || !window.GitPanelVisibleForTest)
                    {
                        throw new InvalidOperationException("收起终端工具区时错误结束了终端会话，或没有保留 Commit。");
                    }

                    window.ToggleTerminalForTest();
                    if (!window.TerminalPanelVisibleForTest || !window.TerminalRunningForTest)
                    {
                        throw new InvalidOperationException("重新展开终端工具区后没有恢复原会话。");
                    }

                    window.ShowHistoryForTest();
                    if (!window.HistoryPanelVisibleForTest
                        || window.TerminalPanelVisibleForTest
                        || !window.TerminalRunningForTest
                        || !window.GitPanelVisibleForTest)
                    {
                        throw new InvalidOperationException("打开 Git 历史时没有互斥隐藏终端并保留终端会话。");
                    }

                    window.ShowTerminalForTest();
                    if (window.HistoryPanelVisibleForTest
                        || !window.TerminalPanelVisibleForTest
                        || !window.TerminalRunningForTest
                        || !window.GitPanelVisibleForTest)
                    {
                        throw new InvalidOperationException("从 Git 历史切回终端时没有恢复原终端会话。");
                    }
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
            if (!window.ActiveMarkdownPreviewLoading && !window.ActiveMarkdownPreviewReady)
            {
                throw new InvalidOperationException("Markdown 文档既没有显示加载占位，也没有显示已完成的预览。");
            }
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
            if (window.ActiveMarkdownPreviewLoading)
            {
                throw new InvalidOperationException("Markdown 预览完成后仍然显示加载占位。");
            }

            int splitBrowserProcessId = await window.ShowActiveMarkdownSplitForTestAsync();
            if (splitBrowserProcessId <= 0 || !window.DragActiveMarkdownSplitterForTest(NativeTheme.Scale(80)))
            {
                throw new InvalidOperationException("Markdown 对照分隔条没有响应真实窗口拖动，或拖动后没有释放鼠标捕获。");
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
            if (!window.ActiveDocumentModeToolbarWithinBoundsForTest)
            {
                throw new InvalidOperationException("有效 JSON 的原文与格式化工具栏没有显示在文档客户区内。");
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

            if (!window.ActiveImageToolbarWithinBoundsForTest || !window.ActiveImageFitToAreaForTest)
            {
                throw new InvalidOperationException("图片尺寸与缩放工具栏没有按视觉稿显示在文档区域内。");
            }

            int fittedZoom = window.ActiveImageZoomPercentageForTest;
            if (fittedZoom <= 0 || !window.ZoomActiveImageInForTest())
            {
                throw new InvalidOperationException("图片放大按钮没有改变当前缩放比例。");
            }

            window.FitActiveImageToAreaForTest();
            if (!window.ActiveImageFitToAreaForTest
                || window.ActiveImageZoomPercentageForTest != fittedZoom)
            {
                throw new InvalidOperationException("图片适应区域按钮没有恢复窗口对应比例。");
            }

            await window.OpenDocumentForTestAsync(gif);
            AssertActiveDocument(window, DocumentReadStatus.BinarySummary, DocumentKind.Gif);
            await window.OpenDocumentForTestAsync(webp);
            AssertActiveDocument(window, DocumentReadStatus.BinarySummary, DocumentKind.WebP);
            if (!window.ActiveInfoPageHasSinglePrimaryActionForTest
                || !window.ActiveInfoPageActionWithinBoundsForTest
                || !window.ActiveInfoPageActionHasFocusForTest)
            {
                throw new InvalidOperationException("WebP 信息页没有保留唯一、可见且自动获得焦点的外部打开动作。");
            }
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

    private static int CaptureActivationEdgesAfterDelay(nint window, int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return CountVisibleScreenEdges(window);
    }

    private static int CountVisibleScreenEdges(nint window)
    {
        if (!NativeMethods.GetClientRectangle(window, out NativeMethods.Rectangle client)
            || client.Right <= client.Left
            || client.Bottom <= client.Top)
        {
            return 0;
        }

        NativeMethods.Point origin = new();
        if (!NativeMethods.ClientToScreen(window, ref origin))
        {
            return 0;
        }

        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        byte[] pixels = new byte[checked(width * height * 4)];
        nint deviceContext = GetDeviceContext(0);
        if (deviceContext == 0)
        {
            return 0;
        }

        nint memoryDeviceContext = NativeMethods.CreateCompatibleDeviceContext(deviceContext);
        nint bitmap = memoryDeviceContext == 0 ? 0 : CreateCompatibleBitmap(deviceContext, width, height);
        nint previousBitmap = bitmap == 0 ? 0 : NativeMethods.SelectObject(memoryDeviceContext, bitmap);
        try
        {
            if (memoryDeviceContext == 0
                || bitmap == 0
                || !CopyPixels(
                    memoryDeviceContext,
                    0,
                    0,
                    width,
                    height,
                    deviceContext,
                    origin.X,
                    origin.Y,
                    NativeMethods.RasterOperationSourceCopy))
            {
                return 0;
            }

            NativeMethods.BitmapInfo bitmapInfo = new()
            {
                Header = new()
                {
                    Size = unchecked((uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>()),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    ImageSize = unchecked((uint)pixels.Length),
                },
            };
            if (GetBitmapPixels(
                    memoryDeviceContext,
                    bitmap,
                    0,
                    unchecked((uint)height),
                    pixels,
                    ref bitmapInfo,
                    NativeMethods.DeviceIndependentRgbColors) != height)
            {
                return 0;
            }
        }
        finally
        {
            if (previousBitmap != 0)
            {
                _ = NativeMethods.SelectObject(memoryDeviceContext, previousBitmap);
            }
            if (bitmap != 0)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }
            if (memoryDeviceContext != 0)
            {
                _ = NativeMethods.DeleteDeviceContext(memoryDeviceContext);
            }
            _ = ReleaseDeviceContext(0, deviceContext);
        }

        const int columns = 96;
        const int rows = 54;
        uint[] sampledPixels = new uint[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            int sourceY = row * Math.Max(0, height - 1) / (rows - 1);
            for (int column = 0; column < columns; column++)
            {
                int sourceX = column * Math.Max(0, width - 1) / (columns - 1);
                int source = ((sourceY * width) + sourceX) * 4;
                sampledPixels[(row * columns) + column] = unchecked((uint)(
                    pixels[source]
                    | (pixels[source + 1] << 8)
                    | (pixels[source + 2] << 16)));
            }
        }

        int edges = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = (row * columns) + column;
                if (column > 0 && ColorDistance(sampledPixels[index], sampledPixels[index - 1]) > 45)
                {
                    edges++;
                }
                if (row > 0 && ColorDistance(sampledPixels[index], sampledPixels[index - columns]) > 45)
                {
                    edges++;
                }
            }
        }

        return edges;
    }

    private static int ColorDistance(uint first, uint second)
    {
        int firstRed = unchecked((int)(first & 0xFF));
        int firstGreen = unchecked((int)((first >> 8) & 0xFF));
        int firstBlue = unchecked((int)((first >> 16) & 0xFF));
        int secondRed = unchecked((int)(second & 0xFF));
        int secondGreen = unchecked((int)((second >> 8) & 0xFF));
        int secondBlue = unchecked((int)((second >> 16) & 0xFF));
        return Math.Abs(firstRed - secondRed)
            + Math.Abs(firstGreen - secondGreen)
            + Math.Abs(firstBlue - secondBlue);
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
