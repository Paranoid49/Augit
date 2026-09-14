using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App.VisualAuditHost;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments.Length is < 2 or > 6)
        {
            return 2;
        }

        try
        {
            string settingsPath = Path.GetFullPath(arguments[0]);
            string workspacePath = Path.GetFullPath(arguments[1]);
            string? surface = arguments.Length >= 3 ? arguments[2] : null;
            string? capturePath = arguments.Length >= 4 ? arguments[3] : null;
            int? visualAuditDpi = arguments.Length >= 5
                ? ParseVisualAuditDpi(arguments[4])
                : null;
            bool unified = arguments.Length == 6 && arguments[5] == "--unified";
            bool jsonSource = arguments.Length == 6 && arguments[5] == "--json-source";
            bool wideGraph = arguments.Length == 6 && arguments[5] == "--wide-graph";
            string? emptyConflictSide = arguments.Length == 6 ? arguments[5] switch
            {
                "--empty-left" => "left",
                "--empty-right" => "right",
                _ => null,
            } : null;
            if (arguments.Length == 6 && !(unified && surface is "git-compare" or "commit-diff")
                && !(jsonSource && surface == "json-preview") && !(wideGraph && surface == "git-history-graph")
                && !(emptyConflictSide is not null && surface == "conflict-resolver"))
                throw new ArgumentException("--unified 仅用于比较审计，--json-source 仅用于 JSON 审计，--wide-graph 仅用于提交图审计，--empty-left/--empty-right 仅用于三栏冲突审计。");
            using IDisposable? dpiOverride = visualAuditDpi is { } dpi
                ? NativeTheme.PushVisualAuditDpiOverride(dpi)
                : null;
            using VisualAuditWorkspaceScope? auditWorkspace = VisualAuditWorkspaceScope.Create(surface, wideGraph, emptyConflictSide);
            string effectiveWorkspacePath = auditWorkspace?.DirectoryPath ?? workspacePath;
            SettingsStore settingsStore = new(settingsPath);
            ApplicationSettings settings = settingsStore.LoadAsync().GetAwaiter().GetResult();
            string isolatedSettingsPath = $"{settingsPath}.{Environment.ProcessId}.audit";
            WorkspaceInstanceCoordinator? coordinator = new(
                Path.Combine(effectiveWorkspacePath, $".Augit.VisualAudit.{Environment.ProcessId}"));
            try
            {
                if (!coordinator.TryBecomeOwnerAsync().GetAwaiter().GetResult())
                {
                    return 3;
                }

                using VisualAuditAssetScope? auditAssets = VisualAuditAssetScope.Create(effectiveWorkspacePath, surface);
                ApplicationSettings windowSettings = surface?.Equals(
                    "git-unavailable",
                    StringComparison.OrdinalIgnoreCase) == true
                    ? settings with
                    {
                        GitExecutablePath = Path.Combine(effectiveWorkspacePath, "__missing_git_for_visual_audit.exe"),
                    }
                    : settings;
                SettingsStore windowSettingsStore = new(isolatedSettingsPath);
                windowSettingsStore.SaveAsync(windowSettings).GetAwaiter().GetResult();

                using MainWindow window = new(windowSettingsStore, windowSettings, effectiveWorkspacePath, coordinator);
                coordinator = null;
                window.Show();
                if (surface is not null)
                {
                    window.Post(() => _ = ShowSurfaceSafelyAsync(
                        window,
                        windowSettings,
                        surface,
                        auditAssets,
                        capturePath,
                        unified,
                        jsonSource,
                        wideGraph));
                }
                int exitCode = MainWindow.RunMessageLoop();
                _afterTerminalStartupClose?.Invoke();
                window.Dispose();
                return exitCode;
            }
            finally
            {
                if (coordinator is not null)
                {
                    coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                if (File.Exists(isolatedSettingsPath))
                {
                    File.Delete(isolatedSettingsPath);
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int ParseVisualAuditDpi(string argument)
    {
        const string Prefix = "--dpi=";
        if (!argument.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(argument[Prefix.Length..], out int dpi)
            || dpi is not 96 and not 120 and not 144)
        {
            throw new ArgumentException("视觉审计 DPI 参数必须为 --dpi=96、--dpi=120 或 --dpi=144。", nameof(argument));
        }

        return dpi;
    }

    private static async Task ShowSurfaceSafelyAsync(
        MainWindow window,
        ApplicationSettings settings,
        string surface,
        VisualAuditAssetScope? auditAssets,
        string? capturePath,
        bool unified,
        bool jsonSource,
        bool wideGraph)
    {
        try
        {
            await ShowSurfaceAsync(window, settings, surface, auditAssets, capturePath, unified, jsonSource, wideGraph);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            // 审计场景失败时必须结束消息循环，不能留下无界面的后台宿主进程。
            window.Close();
        }
    }

    private static async Task ShowSurfaceAsync(
        MainWindow window,
        ApplicationSettings settings,
        string surface,
        VisualAuditAssetScope? auditAssets,
        string? capturePath,
        bool unified,
        bool jsonSource,
        bool wideGraph)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (window.WorkspaceRoot is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        if (window.WorkspaceRoot is null)
        {
            return;
        }

        while (window.OpenDocumentCount > 0
            && window.LoadedDocumentCountForTest == 0
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await Task.Delay(250);

        if (surface.StartsWith("terminal-startup-", StringComparison.Ordinal))
        {
            await VerifyTerminalStartupAsync(window, surface["terminal-startup-".Length..], capturePath!);
            return;
        }

        Task? modalCapture = null;
        switch (surface.ToLowerInvariant())
        {
            case "text-viewer":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                window.ShowActiveDocumentFindForTest("Git");
                break;
            case "go-to-line":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                modalCapture = ScheduleModalCapture(window, capturePath, "Augit.TextPrompt.Native");
                if (!window.HandleApplicationShortcutForTest('G', control: true))
                    throw new InvalidOperationException("跳转行快捷键没有进入输入窗口。");
                break;
            case "markdown-preview":
                await OpenWorkspaceDocumentAsync(window, "docs", "product-spec.md");
                _ = await window.ShowActiveMarkdownPreviewForTestAsync();
                await WaitForMarkdownReadyAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                break;
            case "json-preview":
                await OpenWorkspaceDocumentAsync(window, "global.json");
                if (jsonSource)
                {
                    _ = NativeMethods.SendMessage(window.ActiveDocumentViewForTest!.Handle,
                        NativeMethods.WindowMessageCommand, 1, 0);
                    if (window.ActiveDocumentViewForTest.IsShowingAlternative)
                        throw new InvalidOperationException("JSON 审计未切换到原文模式。");
                }
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                break;
            case "image-preview":
                await window.OpenDocumentForTestAsync(
                    auditAssets?.ImagePath
                        ?? throw new InvalidOperationException("图片视觉审计资产没有准备完成。"));
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                break;
            case "file-limit":
                await window.OpenDocumentForTestAsync(
                    auditAssets?.UnsupportedImagePath
                        ?? throw new InvalidOperationException("不可预览文件审计资产没有准备完成。"));
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                break;
            case "main-project":
                await OpenReferenceDocumentsAsync(window);
                _ = await window.ShowActiveMarkdownPreviewForTestAsync();
                await WaitForMarkdownReadyAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                // 主界面视觉稿保留底部 Git 历史，确保项目树、文档和历史工具窗处于同一工作上下文。
                await ShowHistoryAndSelectAsync(window);
                break;
            case "commit-changes":
                await OpenReferenceDocumentsAsync(window);
                _ = await window.ShowActiveMarkdownPreviewForTestAsync();
                await WaitForMarkdownReadyAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                break;
            case "commit-diff":
            case "diff-boundary":
                await OpenReferenceDocumentsAsync(window);
                // 保留底部 Git 历史，再打开左侧提交工具窗，复现 Diff 页面要求的上下文保持。
                await ShowHistoryAndSelectAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                await ReselectHistoryForVisualAsync(window);
                _ = await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest");
                await WaitForDiffReadyAsync(window, "app.manifest");
                NativeGitPanel diffPanel = window.GitPanelForTest!;
                nint[] diffModes = diffPanel.DiffModeToolbarHandlesForTest;
                ValidateDiffModeGroup(diffPanel.DiffHandleForTest, diffPanel.DiffModeGroupBoundsForTest, diffModes[1], diffModes[2]);
                if (surface == "diff-boundary")
                {
                    NativeGitPanel panel = window.GitPanelForTest!;
                    panel.ClickDiffChangeForTest(-1);
                    panel.ClickDiffChangeForTest(1);
                    if (!panel.DiffBoundaryHintVisibleForTest
                        || panel.DiffBoundaryHintTextForTest != UiText.DiffNextFileBoundary)
                    {
                        throw new InvalidOperationException("Diff 文件边界提示未显示。");
                    }
                    if (!NativeMethods.GetWindowRectangle(panel.DiffHandleForTest, out NativeMethods.Rectangle body)
                        || !NativeMethods.GetWindowRectangle(panel.DiffBoundaryHintHandleForTest, out NativeMethods.Rectangle hint)
                        || hint.Left < body.Left || hint.Right > body.Right || hint.Top < body.Top
                        || hint.Bottom > body.Bottom || hint.Right <= hint.Left || hint.Bottom <= hint.Top)
                    {
                        throw new InvalidOperationException("Diff 文件边界提示越过正文范围。");
                    }
                }
                break;
            case "git-history":
            case "git-history-filter-overflow":
            case "git-history-toolbar-overflow":
            case "git-history-graph":
                await OpenReferenceDocumentsAsync(window);
                _ = await window.ShowActiveMarkdownPreviewForTestAsync();
                await WaitForMarkdownReadyAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                await ShowHistoryAndSelectAsync(window);
                if (surface.Equals("git-history-filter-overflow", StringComparison.OrdinalIgnoreCase))
                {
                    NativeGitHistoryPanel history = window.HistoryPanelForTest
                        ?? throw new InvalidOperationException("历史筛选审计未创建工具窗口。");
                    if (!history.FilterOverflowVisibleForTest)
                        throw new InvalidOperationException("筛选收纳截图需要使用最小窗口设置。");
                    window.Post(history.ShowFilterOverflowForTest);
                }
                if (surface.Equals("git-history-toolbar-overflow", StringComparison.OrdinalIgnoreCase))
                {
                    NativeGitHistoryPanel history = window.HistoryPanelForTest
                        ?? throw new InvalidOperationException("历史工具栏审计未创建工具窗口。");
                    if (!history.ToolbarOverflowVisibleForTest)
                        throw new InvalidOperationException("工具栏溢出截图需要使用最小窗口设置。");
                    window.Post(history.ShowToolbarOverflowForTest);
                }
                break;
            case "git-compare":
                await OpenReferenceDocumentsAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                await ShowHistoryAndSelectAsync(window);
                if (window.HistoryPanelForTest is { } comparisonHistory)
                {
                    await comparisonHistory.CompareRevisionForHostAsync(
                        "HEAD",
                        "src/Augit.App/app.manifest");
                }
                DateTime comparisonDeadline = DateTime.UtcNow.AddSeconds(8);
                while (window.ComparisonViewForTest is not { LoadingForTest: false, HasDocument: true }
                    && DateTime.UtcNow < comparisonDeadline)
                {
                    await Task.Delay(20);
                }
                if (!window.ReferenceComparisonVisibleForTest
                    || window.StatusTextForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal)
                    || window.ComparisonViewForTest is not { LoadingForTest: false, HasDocument: true, LoadingTextForTest.Length: 0 })
                {
                    throw new InvalidOperationException("引用比较未完成正文或摘要呈现，不能采集最终帧。");
                }
                ValidateComparisonToolbar(window.ComparisonViewForTest!);
                break;
            case "history-diff-loading":
            case "history-diff-failure":
            case "history-diff-cancelled":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "app.manifest");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                await ShowHistoryAndSelectAsync(window);
                NativeGitHistoryPanel historyDiff = window.HistoryPanelForTest
                    ?? throw new InvalidOperationException("历史 Diff 审计没有创建历史窗口。");
                GitRuntimeInfo historyRuntime = await new GitExecutableLocator().ResolveAsync(settings.GitExecutablePath);
                historyDiff.SetHistoryServiceForTest(new HistoryDiffAuditService(
                    new GitHistoryService(historyRuntime), surface == "history-diff-failure"));
                // 没有截图参数时保留操作前状态，由真实鼠标或键盘启动查询，供连续工作流复核。
                if (!string.IsNullOrWhiteSpace(capturePath))
                {
                    await PrepareHistoryDiffStateAsync(window, historyDiff, surface);
                }
                break;
            case "file-history":
                // 文件历史视觉稿保留前台普通文本标签，底部历史工具窗口单独限定到目标路径。
                // 这样不会把 Markdown 预览误当成文件历史页面的正文状态。
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                _ = window.SelectTreePathForTest(Path.Combine(
                    window.WorkspaceRoot,
                    "docs",
                    "product-spec.md"));
                window.ShowFileHistoryForTest(Path.Combine(
                    window.WorkspaceRoot,
                    "docs",
                    "product-spec.md"));
                await WaitForFileHistoryReadyAsync(window, "docs/product-spec.md");
                break;
            case "blame":
                await OpenWorkspaceDocumentAsync(window, "docs", "product-spec.md");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                _ = window.SelectTreePathForTest(Path.Combine(
                    window.WorkspaceRoot,
                    "docs",
                    "product-spec.md"));
                window.ShowFileHistoryForTest(Path.Combine(window.WorkspaceRoot, "docs", "product-spec.md"));
                await WaitForFileHistoryReadyAsync(window, "docs/product-spec.md");
                if (window.HistoryPanelForTest is { } blameHistory)
                {
                    await blameHistory.ShowBlameForPathAsync("docs/product-spec.md");
                    await WaitForBlameReadyAsync(window);
                    if (!blameHistory.FileHistoryModeForTest || blameHistory.FileFilterForTest != "docs/product-spec.md")
                        throw new InvalidOperationException("打开归属不得丢失原文件历史上下文。");
                }
                break;
            case "branches":
                await OpenReferenceDocumentsAsync(window);
                await ShowHistoryAndSelectAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                _ = await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest");
                await WaitForDiffReadyAsync(window, "app.manifest");
                if (!await window.ShowBranchPopupForTestAsync()
                    || window.BranchPopupForTest is not { } branchPopup
                    || !branchPopup.SelectReferenceForTest(window.CurrentBranchLabelForTest)
                    || !branchPopup.ActionsPopupVisibleForTest)
                {
                    throw new InvalidOperationException("分支视觉审计没有显示选中引用的二级动作弹层。");
                }

                break;
            case "terminal-output":
                await VerifyTerminalOutputAsync(window, capturePath!);
                return;
            case "terminal":
                await OpenReferenceDocumentsAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                _ = await window.OpenTerminalForTestAsync();
                break;
            case "quick-open":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                window.ShowSearchForTest(WorkspaceSearchMode.FileNames, "NativeGitPanel");
                await WaitForSearchReadyAsync(window, requireResults: true);
                break;
            case "quick-open-empty":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                window.ShowSearchForTest(
                    WorkspaceSearchMode.FileNames,
                    string.Empty);
                await WaitForSearchReadyAsync(window, requireResults: false);
                break;
            case "repository-search":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "NativeGitPanel.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                window.ShowSearchForTest(WorkspaceSearchMode.Text, "Git 状态");
                await WaitForSearchReadyAsync(window, requireResults: true);
                break;
            case "settings":
                await OpenReferenceDocumentsAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowSettingsForTest();
                break;
            case "remote":
                await PrepareHistoryTextContextAsync(window, requireGitPanel: true, surface: surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowRemotesForTest();
                break;
            case "stash":
                await PrepareCommitDiffContextAsync(window, showHistory: true, surface: surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowCreateStashForTest();
                break;
            case "stash-manager":
                await PrepareHistoryTextContextAsync(window, requireGitPanel: true, surface: surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowStashManagerForTest();
                break;
            case "reset":
                await PrepareHistoryTextContextAsync(window, requireGitPanel: true, surface: surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowResetForTest();
                break;
            case "rollback":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                if (await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest"))
                {
                    await WaitForDiffReadyAsync(window, "app.manifest");
                    modalCapture = ScheduleModalCapture(window, capturePath);
                    await window.ShowRollbackConfirmationForTestAsync();
                }
                break;
            case "clone":
                // 克隆是模态流程，背景必须保留用户当前的标签和项目上下文。
                // 视觉稿明确展示已有文档标签，不能为了制造空正文而关闭用户状态。
                await OpenReferenceDocumentsAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: true);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowCloneForTest();
                break;
            case "push":
                await PrepareCommitDiffContextAsync(window, showHistory: false, surface: surface);
                modalCapture = ScheduleModalCapture(
                    window,
                    capturePath,
                    NativePushDialog.WindowClassNameForTest,
                    NativePushDialog.IsPreviewLoadCompletedForTest);
                window.ShowPushForTest();
                break;
            case "push-no-remote":
                await PrepareCommitDiffContextAsync(window, showHistory: false, surface: surface);
                modalCapture = ScheduleModalCapture(
                    window,
                    capturePath,
                    NativePushDialog.WindowClassNameForTest,
                    NativePushDialog.IsPreviewLoadCompletedForTest);
                window.ShowPushForTest();
                break;
            case "commit-empty":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window);
                window.ShowCommitEmptyForTest();
                break;
            case "diff-loading":
                await OpenReferenceDocumentsAsync(window);
                // Diff 加载只替换临时标签正文，已有底部 Git 与左侧 Commit 工具窗必须继续保留。
                await ShowHistoryAndSelectAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                await ReselectHistoryForVisualAsync(window);
                if (!window.ShowGitDiffLoadingForTest("src/Augit.App/app.manifest"))
                {
                    throw new InvalidOperationException("无法选择 Diff 加载审计文件。");
                }

                await WaitForDiffLoadingAsync(window, "app.manifest");
                if (!window.HistoryPanelVisibleForTest)
                {
                    throw new InvalidOperationException("Diff 局部加载错误隐藏了已有的底部 Git 工具窗口。");
                }

                break;
            case "project-context-menu":
                await OpenWorkspaceDocumentAsync(window, "docs", "product-spec.md");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                if (!await window.OpenTerminalForTestAsync())
                {
                    throw new InvalidOperationException("项目右键菜单视觉场景未能保留底部终端。");
                }

                if (!window.ProjectNavigationActiveForTest
                    || !window.ProjectPanelVisibleForTest
                    || !window.ActiveDocumentVisibleForTest
                    || !window.TerminalPanelVisibleForTest)
                {
                    throw new InvalidOperationException("项目右键菜单没有进入视觉稿要求的项目文本与底部终端上下文。");
                }

                window.Post(() => window.ShowProjectContextMenuForTest(
                    Path.Combine(window.WorkspaceRoot!, "docs", "product-spec.md")));
                break;
            case "changes-context-menu":
                await PrepareCommitDiffContextAsync(window, showHistory: true, surface: surface);
                window.Post(() => window.GitPanelForTest?.ShowChangesContextMenuForHost(
                    "src/Augit.App/app.manifest"));
                break;
            case "git-history-menu":
                await OpenReferenceDocumentsAsync(window);
                await ShowHistoryAndSelectAsync(window);
                window.Post(() => window.HistoryPanelForTest?.ShowHistoryContextMenuForHost());
                break;
            case "git-unavailable":
                window.ShowFilesForTest();
                window.CloseAllDocumentsForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                window.ShowGitForTest();
                await WaitForGitUnavailableAsync(window);
                window.ShowGitUnavailableForTest("未找到 Git for Windows 2.40 或更高版本。");
                EnsureEmptyProjectContext(window, surface);
                break;
            case "operation-progress":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                _ = await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest");
                await WaitForDiffReadyAsync(window, "app.manifest");
                window.ShowOperationProgressForTest("正在执行 Smart Checkout…", "正在恢复临时 Stash");
                break;
            case "operation-result":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window, requireChanges: true);
                _ = await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest");
                await WaitForDiffReadyAsync(window, "app.manifest");
                window.ShowOperationResultForTest(
                    "推送失败",
                    "当前仓库未配置远端，Git 没有修改本地提交或工作区。",
                    error: true);
                break;
            case "workspace-open":
                window.ShowFilesForTest();
                window.CloseAllDocumentsForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                EnsureEmptyProjectContext(window, surface);
                window.Post(() => window.ShowWorkspaceOpenForTest());
                break;
            case "repository-init":
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window);
                window.ShowFilesForTest();
                window.CloseAllDocumentsForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                EnsureEmptyProjectContext(window, surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                window.ShowRepositoryInitForTest();
                break;
            case "smart-checkout":
                await PrepareCommitDiffContextAsync(window, showHistory: false, surface: surface);
                modalCapture = ScheduleModalCapture(window, capturePath);
                await window.ShowSmartCheckoutForTestAsync();
                break;
            case "terminal-close":
                await OpenReferenceDocumentsAsync(window);
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                if (await window.OpenTerminalForTestAsync())
                {
                    DateTime terminalDeadline = DateTime.UtcNow.AddSeconds(8);
                    while (!window.TerminalRunningForTest && DateTime.UtcNow < terminalDeadline)
                    {
                        await Task.Delay(25);
                    }

                    if (!window.TerminalRunningForTest)
                    {
                        throw new InvalidOperationException("关闭终端视觉审计未能等待内置终端会话启动。");
                    }

                    await window.WriteTerminalInputForTestAsync("Start-Sleep -Seconds 30\r\n");
                    window.SetStatusForTest("终端中仍有命令正在运行，关闭将结束命令及其子进程树。");
                    modalCapture = ScheduleModalCapture(
                        window,
                        capturePath,
                        NativeTerminalCloseDialog.WindowClassNameForTest);
                    if (!window.ShowTerminalCloseConfirmationForTest())
                    {
                        throw new InvalidOperationException("关闭终端视觉审计未能打开确认窗口。");
                    }
                }
                break;
            case "search-limited":
                await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "NativeGitPanel.cs");
                window.ShowFilesForTest();
                await PrepareProjectTreeAsync(window, locateActiveFile: false);
                window.ShowSearchForTest(WorkspaceSearchMode.Text, "using");
                await WaitForSearchReadyAsync(window, requireResults: true);
                window.SetStatusForTest("结果超过 1000 条，已停止搜索；已显示结果仍可打开。");
                break;
            case "worktrees":
                await PrepareHistoryTextContextAsync(window, requireGitPanel: false, surface: surface);
                modalCapture = ScheduleModalCapture(
                    window,
                    capturePath,
                    NativeWorktreeManagerDialog.WindowClassNameForTest);
                window.ShowWorktreesForTest();
                break;
            case "conflict-list":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window);
                window.CloseAllDocumentsForTest();
                EnsureEmptyCommitContext(window, surface);
                modalCapture = ScheduleModalCapture(
                    window,
                    capturePath,
                    NativeGitOperationDialog.WindowClassNameForTest);
                await ShowConflictListAsync(window, settings);
                break;
            case "conflict-resolver":
                await OpenReferenceDocumentsAsync(window);
                window.ShowGitForTest();
                await WaitForGitReadyAsync(window);
                window.CloseAllDocumentsForTest();
                EnsureEmptyCommitContext(window, surface);
                modalCapture = ScheduleModalCapture(
                    window,
                    capturePath,
                    NativeConflictResolverDialog.WindowClassNameForTest);
                await ShowConflictResolverAsync(window, settings);
                break;
        }

        if (modalCapture is not null)
        {
            await modalCapture;
            window.Close();
            return;
        }

        if (unified)
        {
            if (surface == "git-compare")
            {
                NativeGitComparisonView comparison = window.ComparisonViewForTest!;
                comparison.ClickModeForTest(false);
                DateTime readyBy = DateTime.UtcNow.AddSeconds(8);
                while (comparison.LoadingForTest && DateTime.UtcNow < readyBy) await Task.Delay(20);
                if (comparison.LoadingForTest || comparison.FileHeaderSideBySideForTest)
                    throw new InvalidOperationException("引用比较未完成单栏文件信息布局。");
                ValidateDiffFileHeader(comparison.FileBarHandleForTest, comparison.TextHandlesForTest[0], false, 8);
            }
            else
            {
                NativeGitPanel panel = window.GitPanelForTest!;
                panel.ClickDiffModeForTest(false);
                await WaitForDiffReadyAsync(window, "app.manifest");
                if (panel.DiffFileHeaderSideBySideForTest)
                    throw new InvalidOperationException("工作区 Diff 未完成单栏文件信息布局。");
                ValidateDiffFileHeader(panel.DiffFileHeaderHandleForTest, panel.DiffEditorHandleForTest, false);
            }
        }
        await Task.Delay(500);
        // Git 面板和文档加载可能在场景准备后再次写入状态栏；审计场景需要在最终帧保留目标状态。
        switch (surface.ToLowerInvariant())
        {
            case "search-limited":
                window.SetStatusForTest("结果超过 1000 条，已停止搜索；已显示结果仍可打开。");
                break;
        }

        if (surface.Equals("terminal", StringComparison.OrdinalIgnoreCase))
        {
            window.ShowTerminalForTest();
        }

        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            try
            {
                await Task.Delay(100);
                if (wideGraph)
                {
                    NativeGitHistoryPanel graphHistory = window.HistoryPanelForTest
                        ?? throw new InvalidOperationException("十二轨审计未打开历史。");
                    if (graphHistory.CommitGraphForTest.ColumnCount != 12 || graphHistory.EntryCount != 18)
                        throw new InvalidOperationException("隔离 Git 仓库未生成十二轨及完整合并历史。");
                    _ = NativeMethods.SetFocus(graphHistory.HistoryListHandleForTest);
                }
                NativeAuditCapture.CaptureClient(window, Path.GetFullPath(capturePath));
                if (wideGraph)
                {
                    NativeGitHistoryPanel history = window.HistoryPanelForTest!;
                    nint list = history.HistoryListHandleForTest;
                    string? selected = history.SelectedCommitHashForTest;
                    string? top = history.HistoryListTopHashForTest;
                    _ = NativeMethods.SendMessage(list, NativeMethods.WindowMessageHorizontalScroll, 7, 0);
                    if (NativeMethods.GetScrollPosition(list, 0) <= 0)
                        throw new InvalidOperationException("十二轨窄窗口没有产生可用的横向滚动范围。");
                    await Task.Delay(100);
                    if (selected != history.SelectedCommitHashForTest || top != history.HistoryListTopHashForTest)
                        throw new InvalidOperationException("横向滚动改变了当前提交或纵向位置。");
                    NativeAuditCapture.CaptureClient(window, Path.ChangeExtension(capturePath, ".right.bmp"));
                }
            }
            finally
            {
                window.Close();
            }
        }

    }

    private static void ValidateDiffFileHeader(nint header, nint body, bool sideBySide, int topPadding = 0)
    {
        if (!NativeMethods.GetWindowRectangle(header, out NativeMethods.Rectangle heading)
            || !NativeMethods.GetWindowRectangle(body, out NativeMethods.Rectangle content)
            || heading.Bottom - heading.Top != NativeDiffFileHeader.Height(sideBySide)
            || content.Top != heading.Bottom + NativeTheme.Scale(topPadding))
            throw new InvalidOperationException("文件信息与正文发生重叠或未使用当前模式的高度。");
    }

    private static void ValidateComparisonToolbar(NativeGitComparisonView view)
    {
        if (view.ChangeSummaryForTest != "1 处差异"
            || view.DisplayedRevisionsForTest != ("HEAD", "工作区")
            || !view.FileBarTextForTest.EndsWith("src/Augit.App/app.manifest", StringComparison.Ordinal)
            || !view.BodyTextForTest.Contains("PerMonitorV2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("引用比较没有使用视觉稿要求的固定清单差异。");
        }

        if (!NativeMethods.GetWindowRectangle(view.Handle, out NativeMethods.Rectangle bounds))
            throw new InvalidOperationException("无法读取比较窗口边界。");
        nint[] controls = [view.ToolbarButtonForTest(0), view.ToolbarButtonForTest(1), view.ToolbarButtonForTest(2),
            view.ChangeSummaryHandleForTest, view.ToolbarButtonForTest(5), view.ToolbarButtonForTest(4),
            view.ToolbarButtonForTest(3), view.ToolbarButtonForTest(6)];
        int previousRight = bounds.Left;
        foreach (nint control in controls)
        {
            if (!NativeMethods.IsWindowVisible(control)
                || !NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle rectangle)
                || rectangle.Left < previousRight || rectangle.Right > bounds.Right
                || rectangle.Right <= rectangle.Left
                || rectangle.Top < bounds.Top || rectangle.Bottom > bounds.Top + NativeGitComparisonView.ToolbarHeightForTest)
            {
                throw new InvalidOperationException("比较工具栏按钮或差异计数不可见、重叠、顺序错误或越界。");
            }
            previousRight = rectangle.Right;
        }
        ValidateDiffModeGroup(view.Handle, view.ModeGroupBoundsForTest, view.ToolbarButtonForTest(4), view.ToolbarButtonForTest(3));
    }

    private static void ValidateDiffModeGroup(nint parent, NativeMethods.Rectangle group, nint sideBySide, nint unified)
    {
        NativeMethods.Point origin = new();
        if (!NativeMethods.ClientToScreen(parent, ref origin)
            || group.Right - group.Left != NativeTheme.Scale(81)
            || group.Bottom - group.Top != NativeTheme.Scale(31))
            throw new InvalidOperationException("Diff 分段组尺寸与视觉稿不一致。");
        foreach ((nint control, bool side) in new[] { (sideBySide, true), (unified, false) })
        {
            NativeMethods.Rectangle expected = NativeTheme.DiffModeButtonBounds(group, side);
            if (!NativeMethods.IsWindowVisible(control)
                || !NativeMethods.GetWindowRectangle(control, out NativeMethods.Rectangle actual)
                || actual.Left != origin.X + expected.Left || actual.Right != origin.X + expected.Right
                || actual.Top != origin.Y + expected.Top || actual.Bottom != origin.Y + expected.Bottom)
                throw new InvalidOperationException("Diff 原生模式按钮未位于分段组内部。");
        }
    }

    private static async Task PrepareHistoryDiffStateAsync(MainWindow window, NativeGitHistoryPanel history, string surface)
    {
        Task pending = history.ActivateCommitFileWithEnterForTestAsync(0);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while ((window.ComparisonViewForTest?.LoadingTextForTest.Length is not > 0
            || !NativeMethods.IsWindowVisible(history.CancelButtonForTest)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        if (window.ComparisonViewForTest?.LoadingTextForTest.Length is not > 0
            || !NativeMethods.IsWindowVisible(history.CancelButtonForTest))
        {
            throw new InvalidOperationException("历史查询没有显示局部加载与取消入口。");
        }
        if (surface == "history-diff-loading")
        {
            return;
        }
        if (surface == "history-diff-cancelled")
        {
            _ = NativeMethods.SendMessage(history.CancelButtonForTest, 0x00F5, 0, 0);
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        string expected = surface == "history-diff-failure" ? HistoryDiffAuditService.FailureMessage : UiText.ComparisonCancelled;
        if (window.ComparisonViewForTest is not { LoadingForTest: false } view
            || !view.BodyTextForTest.Contains(expected, StringComparison.Ordinal)
            || NativeMethods.IsWindowVisible(history.CancelButtonForTest))
        {
            throw new InvalidOperationException("历史查询没有收尾到指定失败或取消状态。");
        }
    }

    private static Task? ScheduleModalCapture(
        MainWindow window,
        string? capturePath,
        string? expectedPopupClassName = null,
        Func<nint, bool>? popupReady = null)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            return null;
        }

        return CaptureModalAndCloseAsync(
            window.Handle,
            Path.GetFullPath(capturePath),
            expectedPopupClassName,
            popupReady);
    }

    private static async Task CaptureModalAndCloseAsync(
        nint owner,
        string capturePath,
        string? expectedPopupClassName,
        Func<nint, bool>? popupReady)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        nint target = owner;
        try
        {
            do
            {
                await Task.Delay(25).ConfigureAwait(false);
                target = NativeAuditCapture.GetActivePopup(owner);
            }
            while (target == owner && DateTime.UtcNow < deadline);

            if (target == owner && expectedPopupClassName is not null)
            {
                throw new InvalidOperationException($"未打开预期模态窗口：{expectedPopupClassName}");
            }

            if (target == owner)
            {
                target = owner;
            }

            if (expectedPopupClassName is not null
                && !NativeAuditCapture.HasWindowClass(target, expectedPopupClassName))
            {
                throw new InvalidOperationException($"打开的模态窗口不是预期类型：{expectedPopupClassName}");
            }

            if (target != owner && !NativeAuditCapture.IsCenteredOver(target, owner))
            {
                throw new InvalidOperationException("模态窗口没有相对主窗口居中。");
            }

            if (target != owner && !NativeAuditCapture.IsContainedWithin(target, owner))
            {
                throw new InvalidOperationException("模态窗口超出主窗口范围，内容会被视觉审计截图裁剪。");
            }

            if (popupReady is not null)
            {
                DateTime readyDeadline = DateTime.UtcNow.AddSeconds(8);
                while (!popupReady(target) && DateTime.UtcNow < readyDeadline)
                {
                    await Task.Delay(25).ConfigureAwait(false);
                }

                if (!popupReady(target))
                {
                    throw new InvalidOperationException("模态窗口内容在视觉审计超时前没有加载完成。");
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
            NativeAuditCapture.CaptureOwnerWithPopup(owner, target, capturePath);
        }
        finally
        {
            if (target != owner)
            {
                NativeAuditCapture.CloseWindow(target, expectedPopupClassName);
            }
        }
    }

    private static class NativeAuditCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint ImageSize;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Color;
            public uint Color2;
            public uint Color3;
            public uint Color4;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
        private static extern bool GetWindowRect(nint window, out Rectangle rectangle);

        [DllImport("user32.dll", EntryPoint = "GetWindowRgn", SetLastError = true)]
        private static extern int GetWindowRegion(nint window, nint region);

        public static bool IsCenteredOver(nint popup, nint owner)
        {
            if (!GetWindowRect(popup, out Rectangle popupRectangle)
                || !GetWindowRect(owner, out Rectangle ownerRectangle))
            {
                return false;
            }

            // 窗口范围是物理像素；175% DPI 下居中布局的取整误差可能达到 14 像素。
            const int CenterTolerance = 24;
            int horizontalDifference = Math.Abs(
                popupRectangle.Left + popupRectangle.Right
                - ownerRectangle.Left - ownerRectangle.Right);
            int verticalDifference = Math.Abs(
                popupRectangle.Top + popupRectangle.Bottom
                - ownerRectangle.Top - ownerRectangle.Bottom);
            return horizontalDifference <= CenterTolerance * 2
                && verticalDifference <= CenterTolerance * 2;
        }

        public static bool IsContainedWithin(nint popup, nint owner)
        {
            if (!GetWindowRect(popup, out Rectangle popupRectangle)
                || !GetWindowRect(owner, out Rectangle ownerRectangle))
            {
                return false;
            }

            return popupRectangle.Left >= ownerRectangle.Left
                && popupRectangle.Top >= ownerRectangle.Top
                && popupRectangle.Right <= ownerRectangle.Right
                && popupRectangle.Bottom <= ownerRectangle.Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "GetLastActivePopup")]
        private static extern nint GetLastActivePopupNative(nint owner);

        [DllImport("user32.dll", EntryPoint = "GetWindow")]
        private static extern nint GetWindowNative(nint window, uint command);

        [DllImport("user32.dll", EntryPoint = "EnumWindows")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProcedure procedure, nint parameter);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(nint window, [Out] char[] className, int maximumCount);

        private delegate bool EnumWindowsProcedure(nint window, nint parameter);

        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        private static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        private static extern bool PostMessage(nint window, uint message, nuint wordParameter, nint longParameter);

        private const uint WindowMessageClose = 0x0010;
        private const uint GetWindowEnabledPopup = 6;
        [DllImport("user32.dll", EntryPoint = "PrintWindow", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetDC", SetLastError = true)]
        private static extern nint GetDeviceContext(nint window);

        [DllImport("user32.dll", EntryPoint = "ReleaseDC", SetLastError = true)]
        private static extern int ReleaseDeviceContext(nint window, nint deviceContext);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC", SetLastError = true)]
        private static extern nint CreateCompatibleDeviceContext(nint deviceContext);

        [DllImport("gdi32.dll", EntryPoint = "CreateRectRgn", SetLastError = true)]
        private static extern nint CreateRectRegion(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", EntryPoint = "PtInRegion", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PtInRegion(nint region, int x, int y);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap", SetLastError = true)]
        private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

        [DllImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
        private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

        [DllImport("gdi32.dll", EntryPoint = "GetDIBits", SetLastError = true)]
        private static extern int GetBitmapPixels(
            nint deviceContext,
            nint bitmap,
            uint firstScanLine,
            uint scanLineCount,
            [Out] byte[] pixels,
            ref BitmapInfo bitmapInfo,
            uint colorUse);

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(nint graphicsObject);

        [DllImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDeviceContext(nint deviceContext);

        public static nint GetActivePopup(nint owner)
        {
            nint popup = GetWindowNative(owner, GetWindowEnabledPopup);
            if (!IsOwnedVisiblePopup(popup, owner))
            {
                popup = GetLastActivePopupNative(owner);
            }

            if (!IsOwnedVisiblePopup(popup, owner))
            {
                if (GetWindowThreadProcessId(owner, out uint processId) == 0)
                {
                    return owner;
                }

                popup = 0;
                long largestArea = 0;
                EnumWindowsProcedure procedure = (candidate, _) =>
                {
                    if (candidate == owner || !IsWindowVisible(candidate))
                    {
                        return true;
                    }

                    if (GetWindowThreadProcessId(candidate, out uint candidateProcessId) == 0
                        || candidateProcessId != processId
                        || !GetWindowRect(candidate, out Rectangle rectangle))
                    {
                        return true;
                    }

                    long area = Math.Max(0, rectangle.Right - rectangle.Left)
                        * Math.Max(0, rectangle.Bottom - rectangle.Top);
                    if (GetWindowNative(candidate, 4) == owner || area > largestArea)
                    {
                        popup = candidate;
                        largestArea = area;
                    }

                    return true;
                };
                _ = EnumWindows(procedure, 0);
            }

            return popup != 0 && IsWindowVisible(popup) ? popup : owner;
        }

        public static bool HasWindowClass(nint window, string expectedClassName)
        {
            char[] className = new char[256];
            int length = GetClassName(window, className, className.Length);
            return length > 0
                && new string(className, 0, length).Equals(expectedClassName, StringComparison.Ordinal);
        }

        private static bool IsOwnedVisiblePopup(nint popup, nint owner)
        {
            return popup != 0
                && popup != owner
                && IsWindowVisible(popup)
                && GetWindowNative(popup, 4) == owner;
        }

        public static void CloseWindow(nint window, string? expectedPopupClassName)
        {
            uint message = expectedPopupClassName is not null
                && expectedPopupClassName.Equals(
                    NativeConflictResolverDialog.WindowClassNameForTest,
                    StringComparison.Ordinal)
                ? NativeConflictResolverDialog.VisualAuditForceCloseMessageForTest
                : WindowMessageClose;
            _ = PostMessage(window, message, 0, 0);
        }

        public static void CaptureClient(MainWindow owner, string path)
        {
            nint window = owner.Handle;
            nint target = GetActivePopup(window);
            bool notificationExpected = owner.OperationNotificationVisibleForTest;
            bool notificationRendered = false;
            CapturedSurface ownerSurface = CaptureWindow(
                window,
                renderAfterCapture: deviceContext =>
                {
                    notificationRendered = owner.RenderOperationNotificationForVisualAudit(deviceContext);
                });
            if (notificationExpected && !notificationRendered)
            {
                throw new InvalidOperationException("视觉审计截图未能合成可见的局部通知。");
            }

            if (target != window)
            {
                CompositeWindow(ownerSurface, window, target);
            }

            // 分支弹层的二级动作是独立的 owned popup，PrintWindow 不会把它自动包含在主弹层中。
            // 审计截图必须合成两个窗口，才能验证 UX 规格要求的选中引用动作层级。
            if (owner.BranchPopupForTest is { ActionsPopupVisibleForTest: true } branchPopup)
            {
                CompositeWindow(ownerSurface, window, branchPopup.Handle);
                CompositeWindow(ownerSurface, window, branchPopup.ActionsHandleForTest);
            }

            WriteBitmap(path, ownerSurface.Width, ownerSurface.Height, ownerSurface.Pixels);
        }

        private static void CompositeWindow(
            CapturedSurface destination,
            nint owner,
            nint sourceWindow)
        {
            if (sourceWindow == 0
                || sourceWindow == owner
                || !IsWindowVisible(sourceWindow)
                || !GetWindowRect(owner, out Rectangle ownerRectangle)
                || !GetWindowRect(sourceWindow, out Rectangle sourceRectangle))
            {
                return;
            }

            CapturedSurface source = CaptureWindow(sourceWindow);
            nint region = CreateRectRegion(0, 0, 1, 1);
            bool hasRegion = region != 0 && GetWindowRegion(sourceWindow, region) > 0;
            Composite(
                destination,
                source,
                sourceRectangle.Left - ownerRectangle.Left,
                sourceRectangle.Top - ownerRectangle.Top,
                hasRegion ? region : 0);
            if (region != 0)
            {
                _ = DeleteObject(region);
            }
        }

        public static void CaptureOwnerWithPopup(nint owner, nint popup, string path)
        {
            if (owner == 0 || popup == 0)
            {
                throw new InvalidOperationException("视觉审计模态窗口句柄无效。");
            }

            CapturedSurface ownerSurface = CaptureWindow(owner);
            if (popup != owner
                && GetWindowRect(owner, out Rectangle ownerRectangle)
                && GetWindowRect(popup, out Rectangle popupRectangle))
            {
                CapturedSurface popupSurface = CaptureWindow(popup);
                nint region = CreateRectRegion(0, 0, 1, 1);
                bool hasRegion = region != 0 && GetWindowRegion(popup, region) > 0;
                Composite(
                    ownerSurface,
                    popupSurface,
                    popupRectangle.Left - ownerRectangle.Left,
                    popupRectangle.Top - ownerRectangle.Top,
                    hasRegion ? region : 0);
                if (region != 0)
                {
                    _ = DeleteObject(region);
                }
            }

            WriteBitmap(path, ownerSurface.Width, ownerSurface.Height, ownerSurface.Pixels);
        }

        private static void Composite(
            CapturedSurface destination,
            CapturedSurface source,
            int offsetX,
            int offsetY,
            nint sourceRegion = 0)
        {
            for (int y = 0; y < source.Height; y++)
            {
                int destinationY = offsetY + y;
                if (destinationY < 0 || destinationY >= destination.Height)
                {
                    continue;
                }

                int firstVisible = Math.Max(0, -offsetX);
                int lastVisible = Math.Min(source.Width, destination.Width - offsetX);
                if (lastVisible <= firstVisible)
                {
                    continue;
                }

                int runStart = -1;
                for (int x = firstVisible; x < lastVisible; x++)
                {
                    bool inside = sourceRegion == 0 || PtInRegion(sourceRegion, x, y);
                    if (inside)
                    {
                        runStart = runStart < 0 ? x : runStart;
                        continue;
                    }

                    CopyCompositeRun(destination, source, offsetX, destinationY, y, runStart, x);
                    runStart = -1;
                }

                CopyCompositeRun(destination, source, offsetX, destinationY, y, runStart, lastVisible);
            }
        }

        private static void CopyCompositeRun(
            CapturedSurface destination,
            CapturedSurface source,
            int offsetX,
            int destinationY,
            int sourceY,
            int sourceStart,
            int sourceEnd)
        {
            if (sourceStart < 0 || sourceEnd <= sourceStart)
            {
                return;
            }

            int sourceOffset = (sourceY * source.Width + sourceStart) * 4;
            int destinationOffset = (destinationY * destination.Width + offsetX + sourceStart) * 4;
            Buffer.BlockCopy(
                source.Pixels,
                sourceOffset,
                destination.Pixels,
                destinationOffset,
                (sourceEnd - sourceStart) * 4);
        }

        private static CapturedSurface CaptureWindow(
            nint window,
            uint flags = 2,
            Action<nint>? renderAfterCapture = null)
        {
            if (!GetWindowRect(window, out Rectangle rectangle)
                || rectangle.Right <= rectangle.Left
                || rectangle.Bottom <= rectangle.Top)
            {
                throw new InvalidOperationException("视觉审计窗口范围无效。");
            }

            int width = rectangle.Right - rectangle.Left;
            int height = rectangle.Bottom - rectangle.Top;
            byte[] pixels = new byte[checked(width * height * 4)];
            nint screenDc = GetDeviceContext(0);
            nint memoryDc = screenDc == 0 ? 0 : CreateCompatibleDeviceContext(screenDc);
            nint bitmap = memoryDc == 0 ? 0 : CreateCompatibleBitmap(screenDc, width, height);
            nint oldBitmap = bitmap == 0 ? 0 : SelectObject(memoryDc, bitmap);
            try
            {
                if (screenDc == 0
                    || memoryDc == 0
                    || bitmap == 0
                    || !PrintWindow(window, memoryDc, flags))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视觉审计窗口像素。");
                }


                renderAfterCapture?.Invoke(memoryDc);
                CopyBitmapPixels(memoryDc, bitmap, width, height, pixels);

                return new(width, height, pixels);
            }
            finally
            {
                if (oldBitmap != 0)
                {
                    _ = SelectObject(memoryDc, oldBitmap);
                }

                if (bitmap != 0)
                {
                    _ = DeleteObject(bitmap);
                }

                if (memoryDc != 0)
                {
                    _ = DeleteDeviceContext(memoryDc);
                }

                if (screenDc != 0)
                {
                    _ = ReleaseDeviceContext(0, screenDc);
                }
            }
        }

        private static void CopyBitmapPixels(
            nint deviceContext,
            nint bitmap,
            int width,
            int height,
            byte[] pixels)
        {
            BitmapInfo info = new()
            {
                Header = new()
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    ImageSize = (uint)pixels.Length,
                },
            };
            if (GetBitmapPixels(deviceContext, bitmap, 0, (uint)height, pixels, ref info, 0) != height)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法导出视觉审计窗口像素。");
            }
        }

        private sealed record CapturedSurface(int Width, int Height, byte[] Pixels);

        private static void WriteBitmap(string path, int width, int height, byte[] pixels)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            const int fileHeaderSize = 14;
            const int infoHeaderSize = 40;
            int imageSize = checked(width * height * 4);
            using FileStream stream = File.Create(path);
            using BinaryWriter writer = new(stream);
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(checked(fileHeaderSize + infoHeaderSize + imageSize));
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write(fileHeaderSize + infoHeaderSize);
            writer.Write(infoHeaderSize);
            writer.Write(width);
            writer.Write(-height);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(imageSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(pixels);
        }
    }

    private static async Task ShowConflictListAsync(
        MainWindow window,
        ApplicationSettings settings)
    {
        ConflictAuditContext? context = await LoadConflictContextAsync(window, settings);
        if (context is null)
        {
            return;
        }

        _ = NativeGitOperationDialog.Show(
            window.Handle,
            context.Repository,
            context.OperationService,
            context.ConflictService,
            settings,
            _ => { });
    }

    private static async Task ShowConflictResolverAsync(
        MainWindow window,
        ApplicationSettings settings)
    {
        ConflictAuditContext? context = await LoadConflictContextAsync(window, settings);
        if (context is null)
        {
            return;
        }

        GitAdvancedOperationResult inspected = await context.OperationService.InspectAsync(context.Repository);
        IReadOnlyList<GitConflictFileInfo>? conflicts = inspected.Session?.ConflictFiles;
        GitConflictFileInfo? conflict = conflicts is { Count: > 0 } ? conflicts[0] : null;
        if (conflict is null)
        {
            return;
        }

        GitConflictLoadResult loaded = await context.ConflictService.LoadAsync(
            context.Repository,
            conflict.RelativePath);
        if (!loaded.IsSuccess
            || loaded.Document is not { ContentKind: GitConflictContentKind.Text } document)
        {
            return;
        }

        _ = NativeConflictResolverDialog.Show(
            window.Handle,
            context.Repository,
            context.ConflictService,
            document,
            settings,
            _ => { });
    }

    private static async Task<ConflictAuditContext?> LoadConflictContextAsync(
        MainWindow window,
        ApplicationSettings settings)
    {
        string? workspace = window.WorkspaceRoot;
        if (workspace is null)
        {
            return null;
        }

        GitRuntimeInfo runtime = await new GitExecutableLocator().ResolveAsync(settings.GitExecutablePath);
        if (!runtime.IsAvailable)
        {
            return null;
        }

        GitRepositoryOperationResult inspected = await new GitRepositoryService(runtime).InspectAsync(workspace);
        if (!inspected.IsSuccess || inspected.Repository is not { HasConflicts: true } repository)
        {
            return null;
        }

        GitOperationService operationService = new(runtime);
        return new(
            repository,
            operationService,
            new GitConflictService(runtime, operationService));
    }

    private static async Task WaitForGitReadyAsync(MainWindow window, bool requireChanges = false)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while ((!window.GitRuntimeAvailableForTest
                || window.GitRefreshingForTest
                || window.GitRepositoryKindForTest is null
                || (requireChanges && window.GitChangedFileCountForTest == 0))
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static async Task WaitForGitUnavailableAsync(MainWindow window)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        // 等待降级通知完成绘制，避免在运行时状态已更新但 Toast 尚未显示的中间帧截图。
        while ((!window.GitRuntimeUnavailableForTest
                || !window.GitUnavailableNoticeVisibleForTest)
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        if (!window.GitRuntimeUnavailableForTest
            || !window.GitUnavailableNoticeVisibleForTest)
        {
            throw new InvalidOperationException("Git 不可用视觉场景没有进入可见的局部通知状态。");
        }
    }

    private static async Task WaitForDiffReadyAsync(MainWindow window, string fileName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while ((!window.GitDiffVisibleForTest
                || window.GitDiffLoadingForTest
                || window.StatusTextForTest == UiText.GeneratingDiff
                || window.GitDiffTitleForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal)
                || !Path.GetFileName(window.GitDiffPathForTest).Equals(
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        if (!window.GitDiffVisibleForTest
            || window.GitDiffLoadingForTest
            || window.StatusTextForTest == UiText.GeneratingDiff
            || window.GitDiffTitleForTest.Contains(UiText.GeneratingDiff, StringComparison.Ordinal)
            || !Path.GetFileName(window.GitDiffPathForTest).Equals(
                fileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Diff 视觉审计未在截止时间内完成：{fileName}。");
        }
    }

    private static async Task WaitForDiffLoadingAsync(MainWindow window, string fileName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while ((!window.GitDiffVisibleForTest
                || !window.GitDiffLoadingForTest
                || !Path.GetFileName(window.GitDiffPathForTest).Equals(
                    fileName,
                    StringComparison.OrdinalIgnoreCase)
                || !window.GitDiffLoadingNoticeVisibleForTest
                || !window.GitDiffLoadingNoticeTextForTest.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        if (!window.GitDiffVisibleForTest
                || !window.GitDiffLoadingForTest
                || !Path.GetFileName(window.GitDiffPathForTest).Equals(
                    fileName,
                    StringComparison.OrdinalIgnoreCase)
                || !window.GitDiffLoadingNoticeVisibleForTest
                || !window.GitDiffLoadingNoticeTextForTest.Contains(fileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Diff 局部加载审计没有稳定在当前文件的局部占位状态。");
        }
    }

    private static async Task WaitForMarkdownReadyAsync(MainWindow window)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        while ((!window.ActiveMarkdownPreviewReady || window.ActiveMarkdownPreviewLoading)
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static async Task WaitForFileHistoryReadyAsync(MainWindow window, string relativePath)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while ((window.HistoryPanelForTest?.FileFilterForTest?.Equals(
                    relativePath,
                    StringComparison.OrdinalIgnoreCase) != true
                || window.HistoryEntryCountForTest == 0
                || window.HistoryPanelForTest?.FileHistoryComparisonForTest is not { HasDocument: true, IsBusy: false })
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        if (window.HistoryPanelForTest?.FileHistoryComparisonForTest is not { HasDocument: true, IsBusy: false, HasFailed: false })
            throw new InvalidOperationException("文件历史 Diff 未完成加载与排版。");
    }

    private static async Task WaitForBlameReadyAsync(MainWindow window)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (!window.ActiveDocumentIsShowingBlameForTest && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        if (window.ActiveDocumentViewForTest is not
            {
                IsShowingBlame: true, IsTextReadOnly: true,
                BlameToolbarVisibleForTest: true, BlameToolbarWithinClientBoundsForTest: true
            })
            throw new InvalidOperationException("Blame 未显示完整的只读归属正文、数量和关闭入口。");
    }

    private static async Task WaitForSearchReadyAsync(MainWindow window, bool requireResults)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while ((!window.SearchCompletedForTest
                || (requireResults && window.SearchResultCountForTest == 0))
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
    }

    private static async Task OpenReferenceDocumentsAsync(MainWindow window)
    {
        string workspace = window.WorkspaceRoot
            ?? throw new InvalidOperationException("视觉审计工作区尚未准备完成。");
        string[] paths =
        [
            Path.Combine(workspace, "THIRD-PARTY-NOTICES.md"),
            Path.Combine(workspace, "docs", "roadmap.md"),
            Path.Combine(workspace, "docs", "product-spec.md"),
        ];
        foreach (string path in paths.Where(File.Exists))
        {
            await window.OpenDocumentForTestAsync(path);
        }
    }

    private static async Task PrepareCommitDiffContextAsync(
        MainWindow window,
        bool showHistory,
        string surface)
    {
        await OpenReferenceDocumentsAsync(window);
        if (showHistory)
        {
            await ShowHistoryAndSelectAsync(window);
        }

        window.ShowGitForTest();
        await WaitForGitReadyAsync(window, requireChanges: true);
        if (showHistory)
        {
            await ReselectHistoryForVisualAsync(window);
        }

        if (!await window.SelectGitFileForTestAsync("src/Augit.App/app.manifest"))
        {
            throw new InvalidOperationException($"{surface} 无法打开视觉稿要求的提交 Diff。");
        }

        await WaitForDiffReadyAsync(window, "app.manifest");
        if (!window.GitPanelVisibleForTest
            || !window.GitDiffVisibleForTest
            || window.FileTreeVisibleForTest
            || window.HistoryPanelVisibleForTest != showHistory)
        {
            throw new InvalidOperationException($"{surface} 没有进入视觉稿要求的提交 Diff 上下文。");
        }
    }

    private static async Task PrepareHistoryTextContextAsync(
        MainWindow window,
        bool requireGitPanel,
        string surface)
    {
        await OpenWorkspaceDocumentAsync(window, "src", "Augit.App", "MainWindow.cs");
        if (requireGitPanel)
        {
            window.ShowGitForTest();
            await WaitForGitReadyAsync(window);
        }

        window.ShowFilesForTest();
        await PrepareProjectTreeAsync(window, locateActiveFile: false);
        await ShowHistoryAndSelectAsync(window);
        if (!window.ProjectNavigationActiveForTest
            || !window.ProjectPanelVisibleForTest
            || !window.ActiveDocumentVisibleForTest
            || !window.HistoryPanelVisibleForTest
            || window.GitPanelVisibleForTest
            || window.GitDiffVisibleForTest
            || window.TerminalPanelVisibleForTest)
        {
            throw new InvalidOperationException($"{surface} 没有进入视觉稿要求的项目文本与底部 Git 上下文。");
        }
    }

    private static void EnsureEmptyCommitContext(MainWindow window, string surface)
    {
        if (window.OpenDocumentCount != 0
            || !window.GitPanelVisibleForTest
            || window.GitDiffVisibleForTest
            || window.FileTreeVisibleForTest)
        {
            throw new InvalidOperationException($"{surface} 没有进入视觉稿要求的提交工具窗与空正文状态。");
        }
    }

    /// <summary>
    /// 空正文视觉稿必须保持项目工具窗口，不能由审计宿主偷偷打开参考文档。
    /// </summary>
    private static void EnsureEmptyProjectContext(MainWindow window, string surface)
    {
        if (window.OpenDocumentCount != 0
            || !window.ProjectNavigationActiveForTest
            || !window.ProjectPanelVisibleForTest)
        {
            throw new InvalidOperationException($"{surface} 没有进入视觉稿要求的项目树与空正文状态。");
        }

        if (surface.Equals("git-unavailable", StringComparison.OrdinalIgnoreCase)
            && (!window.GitUnavailableNoticeVisibleForTest
                || !window.OperationNotificationIsAboveEmptyDocumentForTest))
        {
            throw new InvalidOperationException("Git 不可用通知没有位于空正文覆盖层之上。");
        }
    }

    private static async Task OpenWorkspaceDocumentAsync(MainWindow window, params string[] relativeParts)
    {
        string workspace = window.WorkspaceRoot
            ?? throw new InvalidOperationException("视觉审计工作区尚未准备完成。");
        string path = Path.Combine([workspace, .. relativeParts]);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("视觉审计文件不存在。", path);
        }

        await window.OpenDocumentForTestAsync(path);
    }

    private static async Task PrepareProjectTreeAsync(MainWindow window, bool locateActiveFile)
    {
        string workspace = window.WorkspaceRoot
            ?? throw new InvalidOperationException("视觉审计工作区尚未准备完成。");
        // 视觉稿默认展示根目录和 docs 内容；不要为了准备数据把视口滚动到深层 src。
        foreach (string path in new[]
        {
            Path.Combine(workspace, "docs"),
            Path.Combine(workspace, "src"),
            Path.Combine(workspace, "src", "Augit.App"),
        })
        {
            await window.ExpandTreePathForTestAsync(path);
        }

        window.KeepTreeRootVisibleForTest();

        if (locateActiveFile)
        {
            await window.LocateActiveFileForTestAsync();
        }
    }

    private static async Task ShowHistoryAndSelectAsync(MainWindow window)
    {
        window.ShowHistoryForTest();
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (window.HistoryEntryCountForTest == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        NativeGitHistoryPanel? history = window.HistoryPanelForTest;
        if (history is not null && window.HistoryEntryCountForTest > 0)
        {
            await history.SelectCommitForTestAsync(Math.Min(1, window.HistoryEntryCountForTest - 1));
            DateTime detailsDeadline = DateTime.UtcNow.AddSeconds(8);
            while (!history.CommitDetailsLoadedForTest && DateTime.UtcNow < detailsDeadline)
            {
                await Task.Delay(25);
            }
        }
    }

    private static async Task ReselectHistoryForVisualAsync(MainWindow window)
    {
        NativeGitHistoryPanel? history = window.HistoryPanelForTest;
        if (history is not null && window.HistoryEntryCountForTest > 1)
        {
            await history.SelectCommitForTestAsync(1);
            DateTime detailsDeadline = DateTime.UtcNow.AddSeconds(8);
            while (!history.CommitDetailsLoadedForTest && DateTime.UtcNow < detailsDeadline)
            {
                await Task.Delay(25);
            }
        }
    }

    private sealed record ConflictAuditContext(
        GitRepositorySnapshot Repository,
        GitOperationService OperationService,
        GitConflictService ConflictService);

    private sealed class VisualAuditWorkspaceScope : IDisposable
    {
        private readonly string _cleanupDirectory;

        private VisualAuditWorkspaceScope(string directoryPath, string cleanupDirectory)
        {
            DirectoryPath = directoryPath;
            _cleanupDirectory = cleanupDirectory;
        }

        public string DirectoryPath { get; }

        public static VisualAuditWorkspaceScope? Create(string? surface, bool wideGraph = false, string? emptyConflictSide = null)
        {
            bool isConflict = surface?.Equals("conflict-list", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("conflict-resolver", StringComparison.OrdinalIgnoreCase) == true;
            bool isPush = surface?.Equals("push", StringComparison.OrdinalIgnoreCase) == true;
            bool isPushWithoutRemote = surface?.Equals("push-no-remote", StringComparison.OrdinalIgnoreCase) == true;
            bool isBranches = surface?.Equals("branches", StringComparison.OrdinalIgnoreCase) == true;
            bool isStashManager = surface?.Equals("stash-manager", StringComparison.OrdinalIgnoreCase) == true;
            bool isWorktrees = surface?.Equals("worktrees", StringComparison.OrdinalIgnoreCase) == true;
            bool isRemote = surface?.Equals("remote", StringComparison.OrdinalIgnoreCase) == true;
            bool isChanges = surface?.Equals("commit-changes", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("commit-diff", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("diff-loading", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("diff-boundary", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("changes-context-menu", StringComparison.OrdinalIgnoreCase) == true
                // Git 历史、引用比较与 Commit/Diff 共享同一份固定仓库，避免截图受开发工作区的提交和改动影响。
                || surface?.Equals("git-compare", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("git-history", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("git-history-filter-overflow", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("git-history-toolbar-overflow", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("git-history-graph", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("history-diff-loading", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("history-diff-failure", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("history-diff-cancelled", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("smart-checkout", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("rollback", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("operation-progress", StringComparison.OrdinalIgnoreCase) == true
                || surface?.Equals("operation-result", StringComparison.OrdinalIgnoreCase) == true;
            bool isCommitEmpty = surface?.Equals("commit-empty", StringComparison.OrdinalIgnoreCase) == true;
            if (!isConflict
                && !isPush
                && !isPushWithoutRemote
                && !isBranches
                && !isStashManager
                && !isWorktrees
                && !isRemote
                && !isChanges
                && !isCommitEmpty)
            {
                return null;
            }

            string? gitExecutable = FindGitExecutable();
            if (gitExecutable is null)
            {
                throw new InvalidOperationException("Git 视觉审计需要可用的 git.exe。");
            }

            string kind = isConflict
                ? "Conflict"
                : isBranches
                    ? "Branches"
                    : isStashManager
                        ? "StashManager"
                        : isWorktrees
                            ? "Worktrees"
                            : isRemote
                                ? "Remote"
                                : isChanges
                                    ? "Changes"
                                    : isCommitEmpty
                                        ? "CommitEmpty"
                                        : "Push";
            string cleanupDirectory = Path.Combine(
                Path.GetTempPath(),
                $"Augit.VisualAudit.{kind}.{Environment.ProcessId}.{Guid.NewGuid():N}");
            string directory = isConflict
                ? cleanupDirectory
                : Path.Combine(cleanupDirectory, "Augit");
            Directory.CreateDirectory(directory);
            try
            {
                if (isConflict)
                {
                    CreateConflictRepository(directory, gitExecutable, emptyConflictSide);
                }
                else if (isChanges)
                {
                    CreateChangesRepository(
                        directory,
                        gitExecutable,
                        withBranch: surface?.Equals("smart-checkout", StringComparison.OrdinalIgnoreCase) == true);
                    if (wideGraph)
                    {
                        string basis = RunGitWithOutput(directory, gitExecutable, "rev-parse", "HEAD").Trim();
                        string[] branches = Enumerable.Range(1, 11).Select(index => $"feature/branch-{index}").ToArray();
                        for (int index = 0; index < branches.Length; index++)
                        {
                            RunGit(directory, gitExecutable, 0, "checkout", "-b", branches[index], basis);
                            RunGitAt(directory, gitExecutable, 0, $"2026-08-29T09:{10 - index:00}:00+08:00", "commit", "--allow-empty", "-m", $"feat: branch-{index + 1}");
                        }
                        RunGit(directory, gitExecutable, 0, "checkout", "main");
                        RunGitAt(directory, gitExecutable, 0, "2026-08-29T10:00:00+08:00", "commit", "--allow-empty", "-m", "feat: branch-0");
                        RunGitAt(directory, gitExecutable, 0, "2026-08-29T11:00:00+08:00", ["merge", "--no-ff", "-m", "merge: 合并十二条分支", .. branches]);
                    }
                    else if (surface?.Equals("git-history-graph", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // 只修改本次隔离仓库；空提交保持既有截图文件与工作区改动数量稳定。
                        RunGit(directory, gitExecutable, 0, "checkout", "-b", "feature/graph");
                        RunGitAt(directory, gitExecutable, 0, "2026-08-29T09:00:00+08:00", "commit", "--allow-empty", "-m", "feat: 调整历史筛选布局");
                        RunGit(directory, gitExecutable, 0, "checkout", "main");
                        RunGitAt(directory, gitExecutable, 0, "2026-08-29T10:00:00+08:00", "commit", "--allow-empty", "-m", "fix: 修正工具栏图标");
                        RunGitAt(directory, gitExecutable, 0, "2026-08-29T11:00:00+08:00", "merge", "--no-ff", "feature/graph", "-m", "merge: 合并历史界面调整");
                    }
                }
                else if (isCommitEmpty)
                {
                    CreateCleanRepository(directory, gitExecutable);
                }
                else
                {
                    CreatePushRepository(
                        directory,
                        cleanupDirectory,
                        gitExecutable,
                        withRemote: isPush || isRemote);

                    if (isBranches)
                    {
                        ConfigureBranchAuditRepository(directory, gitExecutable);
                    }
                    else if (isStashManager)
                    {
                        ConfigureStashAuditRepository(directory, gitExecutable);
                    }
                    else if (isWorktrees)
                    {
                        ConfigureWorktreeAuditRepository(directory, cleanupDirectory, gitExecutable);
                    }
                    else if (isRemote)
                    {
                        ConfigureRemoteAuditRepository(directory, gitExecutable);
                    }
                }

                return new(directory, cleanupDirectory);
            }
            catch
            {
                if (Directory.Exists(cleanupDirectory))
                {
                    DeleteDirectoryWithRetry(cleanupDirectory);
                }

                throw;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_cleanupDirectory))
            {
                DeleteDirectoryWithRetry(_cleanupDirectory);
            }
        }

        private static void DeleteDirectoryWithRetry(string directory)
        {
            for (int attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
            {
                try
                {
                    ClearReadOnlyAttributes(directory);
                    Directory.Delete(directory, true);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }

            if (Directory.Exists(directory))
            {
                throw new IOException($"无法清理视觉审计临时目录：{directory}");
            }
        }

        private static void ClearReadOnlyAttributes(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
            }

            File.SetAttributes(directory, FileAttributes.Normal);
        }

        private static string? FindGitExecutable()
        {
            string? pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return null;
            }

            foreach (string item in pathValue.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(item.Trim('"'), "git.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void CreateConflictRepository(string directory, string gitExecutable, string? emptySide)
        {
            RunGit(directory, gitExecutable, 0, "init", "-b", "main");
            RunGit(directory, gitExecutable, 0, "config", "core.autocrlf", "false");
            RunGit(directory, gitExecutable, 0, "config", "user.name", "Augit Visual Audit");
            RunGit(directory, gitExecutable, 0, "config", "user.email", "audit@example.invalid");

            string relativeConflictPath = Path.Combine("src", "Augit.App", "NativeGitPanel.cs");
            string conflictPath = Path.Combine(directory, relativeConflictPath);
            Directory.CreateDirectory(Path.GetDirectoryName(conflictPath)!);
            string baseContent = "public async Task RefreshAsync()\n{\n    await LoadStatusAsync();\n}\n";
            string common = baseContent;
            if (emptySide is not null) baseContent = "// 原有说明\n" + common;
            File.WriteAllText(conflictPath, baseContent, new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "add", "--", relativeConflictPath);
            RunGit(directory, gitExecutable, 0, "commit", "-m", "创建冲突基线");

            RunGit(directory, gitExecutable, 0, "switch", "-c", "feature/ux");
            string incomingContent = "public async Task RefreshAsync()\n{\n    var status = await LoadStatusAsync();\n    Render(status);\n}\n";
            if (emptySide is not null) incomingContent = (emptySide == "right" ? "" : "// 新增说明一\n// 新增说明二\n") + common;
            File.WriteAllText(conflictPath, incomingContent, new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "commit", "-am", "修改合入分支");

            RunGit(directory, gitExecutable, 0, "switch", "main");
            string currentContent = "public async Task RefreshAsync()\n{\n    var snapshot = await LoadStatusAsync();\n    if (snapshot != _snapshot)\n        ShowSelectedDiff();\n}\n";
            if (emptySide is not null) currentContent = (emptySide == "left" ? "" : "// 新增说明一\n// 新增说明二\n") + common;
            File.WriteAllText(conflictPath, currentContent, new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "commit", "-am", "修改当前分支");

            // 这里的非零退出码就是预期结果，用于留下真实的 Merge 冲突状态。
            RunGit(directory, gitExecutable, 1, "merge", "feature/ux");
        }

        private static void CreateChangesRepository(
            string directory,
            string gitExecutable,
            bool withBranch)
        {
            RunGit(directory, gitExecutable, 0, "init", "-b", "main");
            RunGit(directory, gitExecutable, 0, "config", "core.autocrlf", "false");
            RunGit(directory, gitExecutable, 0, "config", "user.name", "I49");
            RunGit(directory, gitExecutable, 0, "config", "user.email", "audit@example.invalid");
            RunGit(directory, gitExecutable, 0, "config", "commit.gpgsign", "false");

            Dictionary<string, string> baseline = CreateAuditTrackedFiles();
            foreach ((string relativePath, string content) in baseline)
            {
                WriteAuditFile(directory, relativePath, content);
            }

            RunGit(directory, gitExecutable, 0, "add", ".");
            RunGitAt(directory, gitExecutable, 0, "2026-08-28T08:25:00+08:00", "commit", "-m", "feat: 实现 Augit 阶段零至五功能");

            // 历史提交、时间和变化文件数量固定，保证视觉审计每次读取同一份 Git 事实。
            WriteAuditFile(
                directory,
                "src/Augit.App/app.manifest",
                CreateVisualManifestBaseline());
            RunGit(directory, gitExecutable, 0, "add", "src/Augit.App/app.manifest");
            RunGitAt(directory, gitExecutable, 0, "2026-08-28T08:45:00+08:00", "commit", "-m", "fix: 补充原生 Windows 应用清单");

            WriteAuditFile(
                directory,
                "src/Augit.App/MainWindow.cs",
                baseline["src/Augit.App/MainWindow.cs"] + "\n阶段三：刷新窗口状态。\n");
            WriteAuditFile(
                directory,
                "src/Augit.App/NativeGitPanel.cs",
                baseline["src/Augit.App/NativeGitPanel.cs"] + "\n阶段三：保持 Changes 选择。\n");
            RunGit(directory, gitExecutable, 0, "add", "src/Augit.App/MainWindow.cs", "src/Augit.App/NativeGitPanel.cs");
            RunGitAt(directory, gitExecutable, 0, "2026-08-28T21:25:00+08:00", "commit", "-m", "fix: 提升安装卸载与 Git 取消可靠性");

            foreach (string relativePath in new[]
                     {
                         "docs/architecture.md",
                         "docs/performance-report.md",
                         "docs/roadmap.md",
                         "docs/runtime-dependencies.md",
                         "global.json",
                         "Augit.slnx",
                     })
            {
                WriteAuditFile(
                    directory,
                    relativePath,
                    baseline[relativePath] + "\n阶段四：避免不必要的运行时更新。\n");
            }

            RunGit(
                directory,
                gitExecutable,
                0,
                "add",
                "docs/architecture.md",
                "docs/performance-report.md",
                "docs/roadmap.md",
                "docs/runtime-dependencies.md",
                "global.json",
                "Augit.slnx");
            RunGitAt(
                directory,
                gitExecutable,
                0,
                "2026-08-28T21:32:00+08:00",
                "commit",
                "-m",
                "fix: 避免强制更新兼容的 .NET 10",
                "-m",
                "安装器检测到兼容运行时后保持当前环境，不重复更新系统组件。");

            foreach ((string originalPath, string targetPath) in new[]
                     {
                         ("global.json", "ZAuditGlobal.json"),
                         ("docs/architecture.md", "docs/ZAuditArchitecture.md"),
                         ("src/Augit.App/NativeConflictResolverDialog.cs", "src/Augit.App/ZAuditConflictResolverDialog.cs"),
                         ("src/Augit.App/NativeDocumentView.cs", "src/Augit.App/ZAuditDocumentView.cs"),
                         ("src/Augit.App/NativeGitHistoryPanel.cs", "src/Augit.App/ZAuditGitHistoryPanel.cs"),
                         ("src/Augit.App/NativeSearchPanel.cs", "src/Augit.App/ZAuditSearchPanel.cs"),
                         ("src/Augit.App/NativeTerminalPanel.cs", "src/Augit.App/ZAuditTerminalPanel.cs"),
                         ("tools/xterm/terminal/index.html", "tools/xterm/terminal/ZAuditIndex.html"),
                     })
            {
                RunGit(directory, gitExecutable, 0, "mv", "--", originalPath, targetPath);
            }

            WriteAuditFile(
                directory,
                "docs/product-spec.md",
                baseline["docs/product-spec.md"] + "\n阶段五：精确恢复安装前系统 PATH。\n");
            RunGit(directory, gitExecutable, 0, "add", "docs/product-spec.md");
            RunGitAt(directory, gitExecutable, 0, "2026-08-28T22:55:00+08:00", "commit", "-m", "fix: 精确恢复安装前系统 PATH");

            string[] trackedFiles = RunGitWithOutput(directory, gitExecutable, "ls-files", "-z")
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (string relativePath in trackedFiles)
            {
                string path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                string content = File.ReadAllText(path, Encoding.UTF8);
                string changedContent = relativePath.Equals(
                        "src/Augit.App/app.manifest",
                        StringComparison.OrdinalIgnoreCase)
                    ? CreateVisualManifestCurrent()
                    : content + "\n工作区修改：用于视觉审计的稳定变更。\n";
                WriteAuditFile(directory, relativePath, changedContent);
            }

            foreach (string relativePath in CreateAuditUntrackedFiles())
            {
                WriteAuditFile(
                    directory,
                    relativePath,
                    "// 视觉审计未跟踪文件\n");
            }

            if (withBranch)
            {
                RunGit(directory, gitExecutable, 0, "branch", "feature/ux");
            }

            AssertChangesRepositoryStatus(directory, gitExecutable);
            AssertChangesRepositoryHistory(directory, gitExecutable);
        }

        private static void CreateCleanRepository(string directory, string gitExecutable)
        {
            RunGit(directory, gitExecutable, 0, "init", "-b", "main");
            RunGit(directory, gitExecutable, 0, "config", "core.autocrlf", "false");
            RunGit(directory, gitExecutable, 0, "config", "user.name", "Augit Visual Audit");
            RunGit(directory, gitExecutable, 0, "config", "user.email", "audit@example.invalid");
            RunGit(directory, gitExecutable, 0, "config", "commit.gpgsign", "false");

            Dictionary<string, string> baseline = CreateAuditTrackedFiles();
            foreach ((string relativePath, string content) in baseline)
            {
                WriteAuditFile(directory, relativePath, content);
            }

            RunGit(directory, gitExecutable, 0, "add", ".");
            RunGit(directory, gitExecutable, 0, "commit", "-m", "chore: 创建干净视觉审计基线");
        }

        private static Dictionary<string, string> CreateAuditTrackedFiles()
        {
            return new(StringComparer.Ordinal)
            {
                ["THIRD-PARTY-NOTICES.md"] = "# 第三方组件声明\n\n视觉审计使用本地测试数据。\n",
                ["README.md"] = "# Augit\n\n轻量 Git 编辑器视觉审计仓库。\n",
                ["global.json"] = "{\n  \"sdk\": {\n    \"version\": \"10.0.100\"\n  }\n}\n",
                ["docs/architecture.md"] = "# 架构说明\n\n主界面使用 C#、.NET 10 和 Win32。\n",
                ["docs/product-spec.md"] = CreateVisualProductSpec(),
                ["docs/roadmap.md"] = "# 实施路线\n\n按 UX 规格逐页完成视觉和交互验收。\n",
                ["src/Augit.App/MainWindow.cs"] = "namespace Augit.App;\n\ninternal sealed class MainWindow\n{\n}\n",
                ["src/Augit.App/NativeGitPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeGitPanel\n{\n}\n",
                ["src/Augit.App/app.manifest"] = CreateVisualManifestInitial(),
                ["Augit.slnx"] = "<Solution>\n  <Project Path=\"src/Augit.App/Augit.App.csproj\" />\n</Solution>\n",
                ["docs/runtime-dependencies.md"] = "# 运行时依赖\n\n记录审计所需的本机运行时。\n",
                ["docs/performance-report.md"] = "# 性能报告\n\n记录启动、内存和刷新测量。\n",
                ["tests/Augit.App.Tests/Augit.App.Tests.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
                ["src/Augit.Infrastructure/Terminal/ConPtyNativeMethods.cs"] = "namespace Augit.Infrastructure.Terminal;\n\ninternal static class ConPtyNativeMethods\n{\n}\n",
                ["src/Augit.App/AssemblyInfo.cs"] = "using System.Runtime.CompilerServices;\n\n[assembly: AssemblyMetadata(\"Audit\", \"true\")]\n",
                ["src/Augit.App/NativeTheme.cs"] = "namespace Augit.App;\n\ninternal static class NativeTheme\n{\n}\n",
                ["src/Augit.App/NativeDocumentView.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeDocumentView\n{\n}\n",
                ["src/Augit.App/NativeGitHistoryPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeGitHistoryPanel\n{\n}\n",
                ["src/Augit.App/NativeSearchPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeSearchPanel\n{\n}\n",
                ["src/Augit.App/NativeTerminalPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeTerminalPanel\n{\n}\n",
                ["src/Augit.App/NativeConflictResolverDialog.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeConflictResolverDialog\n{\n}\n",
                ["src/Augit.Infrastructure/Terminal/ConPtyTerminalSession.cs"] = "namespace Augit.Infrastructure.Terminal;\n\ninternal sealed class ConPtyTerminalSession\n{\n}\n",
                ["src/Augit.Core/ZAuditStatusSnapshot.cs"] = "namespace Augit.Core.Git;\n\npublic sealed record ZAuditStatusSnapshot;\n",
                ["src/Augit.Core/ZAuditDiffModels.cs"] = "namespace Augit.Core.Git;\n\npublic sealed record ZAuditDiffDocument;\n",
                ["src/Augit.Core/ZAuditHistoryModels.cs"] = "namespace Augit.Core.Git;\n\npublic sealed record ZAuditHistoryEntry;\n",
                ["src/Augit.Infrastructure/Git/ZAuditStatusService.cs"] = "namespace Augit.Infrastructure.Git;\n\npublic sealed class ZAuditStatusService;\n",
                ["src/Augit.Infrastructure/Git/ZAuditDiffService.cs"] = "namespace Augit.Infrastructure.Git;\n\npublic sealed class ZAuditDiffService;\n",
                ["src/Augit.Infrastructure/Git/ZAuditHistoryService.cs"] = "namespace Augit.Infrastructure.Git;\n\npublic sealed class ZAuditHistoryService;\n",
                ["src/Augit.Infrastructure/Settings/ApplicationSettings.cs"] = "namespace Augit.Infrastructure.Settings;\n\npublic sealed record ApplicationSettings;\n",
                ["src/Augit.Infrastructure/Settings/ZAuditSettingsStore.cs"] = "namespace Augit.Infrastructure.Settings;\n\npublic sealed class ZAuditSettingsStore;\n",
                ["tests/Augit.App.Tests/VisualAuditHostTests.cs"] = "namespace Augit.App.Tests;\n\npublic sealed class VisualAuditHostTests;\n",
                ["tests/Augit.Core.Tests/ZAuditGitFileSelectionTests.cs"] = "namespace Augit.Core.Tests;\n\npublic sealed class ZAuditGitFileSelectionTests;\n",
                ["tests/Augit.Infrastructure.Tests/ZAuditGitStatusServiceTests.cs"] = "namespace Augit.Infrastructure.Tests;\n\npublic sealed class ZAuditGitStatusServiceTests;\n",
                ["tools/xterm/terminal/index.html"] = "<!doctype html>\n<title>终端</title>\n",
                ["tools/xterm/terminal/terminal.js"] = "export function startTerminal() {}\n",
            };
        }

        private static string CreateVisualProductSpec()
        {
            return string.Join(
                       "\n",
                       [
                           "# Augit 产品规格",
                           "",
                           "## 1. 产品定位",
                           "",
                           "Augit 是供 100% 外部 AI 协作开发场景使用的轻量桌面工具。",
                           "",
                           "轻量优先是 Augit 的最高产品原则。",
                           "",
                           "## 2. 支持环境与性能目标",
                           "",
                           "- 支持 Windows 10 22H2 x64 和 Windows 11 x64。",
                           "- 冷启动到界面可操作优先争取不超过 1 秒。",
                           "- 核心进程树 Working Set 总和优先控制在 100 MB 内。",
                           "- 外部文件和本地 Git 状态变化在 500 毫秒内反映。",
                           "- 后台空闲时不持续扫描仓库，不明显占用 CPU。",
                           "",
                           "## 3. 工作区与文件浏览",
                           "",
                           "一个窗口只打开一个目录；同一目录不重复开窗口。",
                           "普通文件查看器不提供编辑和保存功能。",
                           "",
                           "## 4. Git 能力",
                           "",
                           "Git 操作界面和流程固定参考 IntelliJ IDEA 2026.2 New UI。",
                       ])
                   + "\n";
        }

        private static IReadOnlyList<string> CreateAuditUntrackedFiles()
        {
            return
            [
                "src/Augit.App/NativeToolTip.cs",
                "src/Augit.App/NativeModalScrim.cs",
                "src/Augit.App/NativeFocusNavigation.cs",
                "docs/ux-spec.md",
                "docs/ux-mockups/new-theme.css",
                "tests/Augit.App.Tests/VisualAuditFixtureTests.cs",
                "tools/xterm/terminal/README.md",
            ];
        }

        private static string CreateVisualManifestBaseline()
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\">\n"
                + "  <assemblyIdentity version=\"1.0.0.0\" name=\"Augit.App\" />\n"
                + "  <description>Augit</description>\n"
                + "  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">\n"
                + "    <application>\n"
                + "      <!-- Windows 10 and Windows 11 -->\n"
                + "      <supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\" />\n"
                + "    </application>\n"
                + "  </compatibility>\n"
                + "  <application xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n"
                + "    <windowsSettings>\n"
                + "      <dpiAware>true</dpiAware>\n"
                + "      <longPathAware>true</longPathAware>\n"
                + "    </windowsSettings>\n"
                + "  </application>\n"
                + "  <dependency>\n"
                + "    <dependentAssembly>\n"
                + "      <assemblyIdentity type=\"win32\"\n"
                + "        name=\"Microsoft.Windows.Common-Controls\"\n"
                + "        version=\"6.0.0.0\" processorArchitecture=\"*\" />\n"
                + "    </dependentAssembly>\n"
                + "  </dependency>\n"
                + "</assembly>\n";
        }

        private static string CreateVisualManifestInitial()
        {
            return CreateVisualManifestBaseline().Replace(
                "      <dpiAware>true</dpiAware>\n",
                "      <dpiAware>false</dpiAware>\n",
                StringComparison.Ordinal);
        }

        private static string CreateVisualManifestCurrent()
        {
            return CreateVisualManifestBaseline().Replace(
                "      <dpiAware>true</dpiAware>\n",
                "      <dpiAware>true/pm</dpiAware>\n      <dpiAwareness>PerMonitorV2</dpiAwareness>\n",
                StringComparison.Ordinal);
        }

        private static void AssertChangesRepositoryStatus(string directory, string gitExecutable)
        {
            string output = RunGitWithOutput(
                directory,
                gitExecutable,
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
                "--ignore-submodules=all");
            string[] records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            int tracked = records.Count(record => record.Length >= 3 && record[0] != '?' && record[1] != '?');
            int untracked = records.Count(record => record.StartsWith("?? ", StringComparison.Ordinal));
            if (tracked != 35 || untracked != 7)
            {
                throw new InvalidOperationException(
                    $"视觉审计 Changes 基线数量错误：已跟踪 {tracked}，未跟踪 {untracked}。\n{output}");
            }
        }

        private static void AssertChangesRepositoryHistory(string directory, string gitExecutable)
        {
            string[] commits = RunGitWithOutput(
                    directory,
                    gitExecutable,
                    "log",
                    "--format=%s%x1f%an%x1f%aI")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            string[] expectedSubjects =
            [
                "fix: 精确恢复安装前系统 PATH",
                "fix: 避免强制更新兼容的 .NET 10",
                "fix: 提升安装卸载与 Git 取消可靠性",
                "fix: 补充原生 Windows 应用清单",
                "feat: 实现 Augit 阶段零至五功能",
            ];
            string[] expectedDates =
            [
                "2026-08-28T22:55:00+08:00",
                "2026-08-28T21:32:00+08:00",
                "2026-08-28T21:25:00+08:00",
                "2026-08-28T08:45:00+08:00",
                "2026-08-28T08:25:00+08:00",
            ];
            if (commits.Length != expectedSubjects.Length)
            {
                throw new InvalidOperationException($"视觉审计历史数量错误：实际 {commits.Length} 条。");
            }

            for (int index = 0; index < commits.Length; index++)
            {
                string[] fields = commits[index].Split('\x1f');
                if (fields.Length != 3
                    || !fields[0].Equals(expectedSubjects[index], StringComparison.Ordinal)
                    || !fields[1].Equals("I49", StringComparison.Ordinal)
                    || !fields[2].Equals(expectedDates[index], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"视觉审计第 {index + 1} 条历史与视觉稿不一致：{commits[index]}");
                }
            }

            string[] selectedCommitFiles = RunGitWithOutput(
                    directory,
                    gitExecutable,
                    "diff-tree",
                    "--no-commit-id",
                    "--name-only",
                    "-r",
                    "HEAD~1")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            string[] expectedVisibleFiles =
            [
                "docs/architecture.md",
                "docs/performance-report.md",
                "docs/roadmap.md",
                "docs/runtime-dependencies.md",
            ];
            if (selectedCommitFiles.Length != 6
                || expectedVisibleFiles.Any(expected => !selectedCommitFiles.Contains(
                    expected,
                    StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"视觉审计选中提交的变化文件不一致：{string.Join(", ", selectedCommitFiles)}");
            }
        }

        private static string RunGitWithOutput(
            string directory,
            string executable,
            params string[] arguments)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = executable,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动视觉审计所需的 git.exe。");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"视觉审计仓库查询失败：{string.Concat(output, error).Trim()}");
            }

            return output;
        }

        private static void CreatePushRepository(
            string directory,
            string cleanupDirectory,
            string gitExecutable,
            bool withRemote)
        {
            RunGit(directory, gitExecutable, 0, "init", "-b", "main");
            RunGit(directory, gitExecutable, 0, "config", "core.autocrlf", "false");
            RunGit(directory, gitExecutable, 0, "config", "user.name", "Augit Visual Audit");
            RunGit(directory, gitExecutable, 0, "config", "user.email", "audit@example.invalid");
            RunGit(directory, gitExecutable, 0, "config", "commit.gpgsign", "false");

            Dictionary<string, string> files = new(StringComparer.Ordinal)
            {
                ["THIRD-PARTY-NOTICES.md"] = "# 第三方组件声明\n\n视觉审计使用本地测试数据。\n",
                ["README.md"] = "# Augit\n\n轻量 Git 编辑器视觉审计仓库。\n",
                ["global.json"] = "{\n  \"sdk\": {\n    \"version\": \"10.0.100\"\n  }\n}\n",
                ["docs/architecture.md"] = "# 架构说明\n\n主界面使用 C#、.NET 10 和 Win32。\n",
                ["docs/product-spec.md"] = "# 产品规格\n\nAugit 提供轻量、只读的项目与 Git 工作流。\n",
                ["docs/roadmap.md"] = "# 实施路线\n\n按 UX 规格逐页完成视觉和交互验收。\n",
                ["src/Augit.App/MainWindow.cs"] = "namespace Augit.App;\n\ninternal sealed class MainWindow\n{\n}\n",
                ["src/Augit.App/NativeGitPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeGitPanel\n{\n}\n",
                ["src/Augit.App/app.manifest"] = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<assembly manifestVersion=\"1.0\" xmlns=\"urn:schemas-microsoft-com:asm.v1\">\n  <assemblyIdentity version=\"1.0.0.0\" name=\"Augit.App\" />\n  <description>Augit 视觉审计基线</description>\n  <trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\">\n    <security>\n      <requestedPrivileges>\n        <requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\" />\n      </requestedPrivileges>\n    </security>\n  </trustInfo>\n  <compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\">\n    <application>\n      <supportedOS Id=\"{4f476546-9373-4a2f-8c03-7c8d4053218e}\" />\n    </application>\n  </compatibility>\n</assembly>\n",
            };
            foreach ((string relativePath, string content) in files)
            {
                WriteAuditFile(directory, relativePath, content);
            }

            RunGit(directory, gitExecutable, 0, "add", ".");
            RunGit(directory, gitExecutable, 0, "commit", "-m", "chore: 创建视觉审计基线");

            if (withRemote)
            {
                string remoteDirectory = Path.Combine(cleanupDirectory, "origin.git");
                RunGit(cleanupDirectory, gitExecutable, 0, "init", "--bare", "-b", "main", remoteDirectory);
                RunGit(directory, gitExecutable, 0, "remote", "add", "origin", remoteDirectory);
                RunGit(directory, gitExecutable, 0, "push", "-u", "origin", "main");
            }

            File.AppendAllText(
                Path.Combine(directory, "docs", "roadmap.md"),
                "\n## 运行时\n\n避免在安装时强制更新 .NET 10。\n",
                new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "add", "docs/roadmap.md");
            RunGit(directory, gitExecutable, 0, "commit", "-m", "fix: 避免强制更新 .NET 10");

            File.AppendAllText(
                Path.Combine(directory, "docs", "product-spec.md"),
                "\n## 安装恢复\n\n卸载时精确恢复安装程序写入的 PATH。\n",
                new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "add", "docs/product-spec.md");
            RunGit(directory, gitExecutable, 0, "commit", "-m", "fix: 精确恢复系统 PATH");

            Dictionary<string, string> workingFiles = new(files, StringComparer.Ordinal)
            {
                ["THIRD-PARTY-NOTICES.md"] = files["THIRD-PARTY-NOTICES.md"] + "\n当前工作区增加一项组件说明。\n",
                ["README.md"] = files["README.md"] + "\n当前版本正在执行 UX 视觉验收。\n",
                ["global.json"] = "{\n  \"sdk\": {\n    \"version\": \"10.0.100\",\n    \"rollForward\": \"latestPatch\"\n  }\n}\n",
                ["docs/architecture.md"] = files["docs/architecture.md"] + "\n界面变化只更新对应区域。\n",
                ["docs/product-spec.md"] = File.ReadAllText(
                    Path.Combine(directory, "docs", "product-spec.md"),
                    Encoding.UTF8) + "\n工作区补充：Push 保留当前文件状态。\n",
                ["docs/roadmap.md"] = File.ReadAllText(
                    Path.Combine(directory, "docs", "roadmap.md"),
                    Encoding.UTF8) + "\n工作区补充：完成 Push 视觉验收。\n",
                ["src/Augit.App/MainWindow.cs"] = "namespace Augit.App;\n\ninternal sealed class MainWindow\n{\n    internal bool IsVisualAuditReady => true;\n}\n",
                ["src/Augit.App/NativeGitPanel.cs"] = "namespace Augit.App;\n\ninternal sealed class NativeGitPanel\n{\n    internal int ChangedFileCount => 9;\n}\n",
                ["src/Augit.App/app.manifest"] = files["src/Augit.App/app.manifest"].Replace(
                    "Augit 视觉审计基线",
                    "Augit 视觉审计工作区",
                    StringComparison.Ordinal),
            };
            foreach ((string relativePath, string content) in workingFiles)
            {
                WriteAuditFile(directory, relativePath, content);
            }
        }

        private static void ConfigureBranchAuditRepository(string directory, string gitExecutable)
        {
            // 分支弹层需要至少一个非当前本地分支和一个标签，才能真实展示引用分组及二级动作。
            RunGit(directory, gitExecutable, 0, "branch", "feature/ux");
            RunGit(directory, gitExecutable, 0, "tag", "v0.1.0");
        }

        private static void ConfigureStashAuditRepository(string directory, string gitExecutable)
        {
            // 先创建旧 Stash，再创建最新 Stash，保证列表顺序与视觉稿一致。
            RunGit(directory, gitExecutable, 0, "stash", "push", "--include-untracked", "-m", "调整安装器");
            File.AppendAllText(
                Path.Combine(directory, "README.md"),
                "\n当前工作区准备进行分支切换。\n",
                new UTF8Encoding(false));
            RunGit(directory, gitExecutable, 0, "stash", "push", "--include-untracked", "-m", "工作区切换前");
        }

        private static void ConfigureWorktreeAuditRepository(
            string directory,
            string cleanupDirectory,
            string gitExecutable)
        {
            string worktreePath = Path.Combine(cleanupDirectory, "Augit-ux");
            RunGit(
                directory,
                gitExecutable,
                0,
                "worktree",
                "add",
                "-b",
                "feature/ux",
                worktreePath,
                "HEAD");
        }

        private static void ConfigureRemoteAuditRepository(string directory, string gitExecutable)
        {
            // 远端管理页只读取本机 Git 配置，不执行网络操作；使用示例 URL 保持审计完全离线。
            RunGit(
                directory,
                gitExecutable,
                0,
                "remote",
                "set-url",
                "origin",
                "https://example.com/team/Augit.git");
            RunGit(
                directory,
                gitExecutable,
                0,
                "remote",
                "set-url",
                "--push",
                "origin",
                "https://example.com/team/Augit.git");
            RunGit(
                directory,
                gitExecutable,
                0,
                "remote",
                "add",
                "backup",
                "https://example.com/archive/Augit.git");
        }

        private static void WriteAuditFile(string directory, string relativePath, string content)
        {
            string path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? parent = Path.GetDirectoryName(path);
            if (parent is not null)
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static void RunGit(string directory, string executable, int expectedExitCode, params string[] arguments)
        {
            RunGitCore(directory, executable, expectedExitCode, null, arguments);
        }

        private static void RunGitAt(
            string directory,
            string executable,
            int expectedExitCode,
            string date,
            params string[] arguments)
        {
            RunGitCore(directory, executable, expectedExitCode, date, arguments);
        }

        private static void RunGitCore(
            string directory,
            string executable,
            int expectedExitCode,
            string? commitDate,
            params string[] arguments)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = executable,
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            if (commitDate is not null)
            {
                startInfo.Environment["GIT_AUTHOR_DATE"] = commitDate;
                startInfo.Environment["GIT_COMMITTER_DATE"] = commitDate;
            }
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动视觉审计所需的 git.exe。");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != expectedExitCode)
            {
                throw new InvalidOperationException(
                    $"视觉审计仓库创建失败：{string.Concat(output, error).Trim()}");
            }
        }
    }

    private sealed class VisualAuditAssetScope : IDisposable
    {
        private VisualAuditAssetScope(string directory, string? imagePath, string? unsupportedImagePath)
        {
            DirectoryPath = directory;
            ImagePath = imagePath;
            UnsupportedImagePath = unsupportedImagePath;
        }

        public string DirectoryPath { get; }

        public string? ImagePath { get; }

        public string? UnsupportedImagePath { get; }

        public static VisualAuditAssetScope? Create(string workspace, string? surface)
        {
            if (surface is null
                || (!surface.Equals("image-preview", StringComparison.OrdinalIgnoreCase)
                    && !surface.Equals("file-limit", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            string directory = Path.Combine(
                workspace,
                "obj",
                $"Augit.VisualAudit.{Environment.ProcessId}");
            Directory.CreateDirectory(directory);
            if (surface.Equals("image-preview", StringComparison.OrdinalIgnoreCase))
            {
                string imagePath = Path.Combine(directory, "image-sample.png");
                File.Copy(Path.Combine(AppContext.BaseDirectory, "image-sample.png"), imagePath);
                return new(directory, imagePath, null);
            }

            string unsupportedImagePath = Path.Combine(directory, "animation.webp");
            byte[] content = new byte[5_033_165];
            "RIFF"u8.CopyTo(content);
            BitConverter.GetBytes(content.Length - 8).CopyTo(content, 4);
            "WEBP"u8.CopyTo(content.AsSpan(8));
            File.WriteAllBytes(unsupportedImagePath, content);
            return new(directory, null, unsupportedImagePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }

    }
}
