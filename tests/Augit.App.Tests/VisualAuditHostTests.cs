using System.Buffers.Binary;
using System.Diagnostics;
using Augit.Core.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VisualAuditHostTests
{
    private static readonly string[] DocumentAuditSurfaces =
        ["text-viewer", "markdown-preview", "json-preview", "image-preview", "file-limit"];

    private static readonly string[] ImplementedAuditSurfaces =
    [
        "blame",
        "branches",
        "clone",
        "commit-changes",
        "commit-diff",
        "commit-empty",
        "changes-context-menu",
        "conflict-list",
        "conflict-resolver",
        "diff-loading",
        "diff-boundary",
        "file-history",
        "file-limit",
        "git-compare",
        "git-history",
        "git-history-graph",
        "git-history-menu",
        "history-diff-loading",
        "history-diff-failure",
        "history-diff-cancelled",
        "git-unavailable",
        "image-preview",
        "json-preview",
        "main-project",
        "markdown-preview",
        "operation-progress",
        "operation-result",
        "project-context-menu",
        "push",
        "push-no-remote",
        "quick-open",
        "quick-open-empty",
        "remote",
        "repository-init",
        "repository-search",
        "reset",
        "rollback",
        "search-limited",
        "settings",
        "smart-checkout",
        "stash",
        "stash-manager",
        "terminal",
        "terminal-close",
        "text-viewer",
        "go-to-line",
        "workspace-open",
        "worktrees",
    ];

    [TestMethod]
    public void 冲突操作会话优先显示真实Rebase步骤()
    {
        GitOperationSession rebase = new(
            GitOperationKind.Rebase,
            true,
            true,
            null,
            [new("NativeGitPanel.cs", true, true, true)],
            false,
            true,
            true,
            2,
            4);
        GitOperationSession merge = new(
            GitOperationKind.Merge,
            true,
            true,
            "main",
            [new("MainWindow.cs", true, true, true)],
            false,
            false,
            true);

        Assert.AreEqual("当前步骤 2/4", NativeGitOperationDialog.ConflictSessionContextForTest(rebase));
        Assert.AreEqual(
            "Merge 进行中  ·  当前分支 main",
            NativeGitOperationDialog.ConflictSessionContextForTest(merge));
    }

    [TestMethod]
    public void 原生界面字体基线与视觉稿一致()
    {
        Assert.AreEqual(13, NativeTheme.UiFontLogicalSizeForTest);
        Assert.AreEqual((13, 13, 13, 400, 600), NativeTheme.UiFontRolesForTest);
    }

    [TestMethod]
    public void 文件树名称表达Git状态且无状态时保持正文色()
    {
        Assert.AreEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Modified, dark: false),
            MainWindow.TreeFileTextColorForTest(dark: false, GitChangeKind.Modified));
        Assert.AreEqual(
            NativeGitStatusPalette.Resolve(GitChangeKind.Untracked, dark: true),
            MainWindow.TreeFileTextColorForTest(dark: true, GitChangeKind.Untracked));
        Assert.AreEqual(NativeTheme.Palette(false).Text, MainWindow.TreeFileTextColorForTest(false, null));
        Assert.AreNotEqual(
            MainWindow.TreeFileTextColorForTest(false, GitChangeKind.Modified),
            MainWindow.TreeFileTextColorForTest(false, GitChangeKind.Deleted));
    }

    [TestMethod]
    public async Task 普通Git错误使用局部通知而不是全局消息框()
    {
        string sourceRoot = FindRepositoryFile("src", "Augit.App", "NativeGitHistoryPanel.cs");
        string appRoot = Path.GetDirectoryName(sourceRoot)!
            .Replace("NativeGitHistoryPanel.cs", string.Empty, StringComparison.OrdinalIgnoreCase);
        string[] sourcePaths =
        [
            Path.Combine(appRoot, "NativeGitHistoryPanel.cs"),
            Path.Combine(appRoot, "NativeGitPanel.cs"),
            Path.Combine(appRoot, "NativeGitOperationDialog.cs"),
            Path.Combine(appRoot, "NativeConflictResolverDialog.cs"),
            Path.Combine(appRoot, "NativeGitReferenceDialog.cs"),
            Path.Combine(appRoot, "NativeRemoteDialog.cs"),
            Path.Combine(appRoot, "NativeStashManagerDialog.cs"),
            Path.Combine(appRoot, "NativeWorktreeManagerDialog.cs"),
            Path.Combine(appRoot, "MainWindow.cs"),
        ];

        foreach (string sourcePath in sourcePaths)
        {
            string source = await File.ReadAllTextAsync(sourcePath);
            Assert.DoesNotContain("MessageBox(Handle, message", source, sourcePath);
            Assert.DoesNotContain("MessageBox(_handle, message", source, sourcePath);
            Assert.DoesNotContain("MessageBox(_handle", source, sourcePath);
        }
    }

    [TestMethod]
    public void 窗口尺寸逻辑像素缩放可逆()
    {
        int physicalWidth = NativeTheme.Scale(1180);
        int physicalHeight = NativeTheme.Scale(760);

        Assert.AreEqual(1180d, NativeTheme.Unscale(physicalWidth), 1d);
        Assert.AreEqual(760d, NativeTheme.Unscale(physicalHeight), 1d);
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(120)]
    [DataRow(144)]
    public void 视觉审计Dpi覆盖按逻辑像素缩放且在释放后恢复(int dpi)
    {
        int before = NativeTheme.ActiveDpiForTest;

        using (NativeTheme.PushVisualAuditDpiOverride(dpi))
        {
            Assert.AreEqual(dpi, NativeTheme.ActiveDpiForTest);
            Assert.AreEqual(300 * dpi / 96, NativeTheme.Scale(300));
            Assert.AreEqual(300d, NativeTheme.Unscale(NativeTheme.Scale(300)), 0.5d);
            Assert.IsTrue(NativeTheme.VisualAuditDpiOverrideActiveForTest);
            Assert.AreEqual(dpi / 96d, NativeTheme.EmbeddedContentRasterizationScaleForTest, 0.001d);
            Assert.AreEqual(
                dpi / 168d,
                NativeTheme.CalculateEmbeddedContentDpiAdjustmentForTest(168),
                0.001d);
        }

        Assert.AreEqual(before, NativeTheme.ActiveDpiForTest);
    }

    [TestMethod]
    public async Task 视觉稿紧凑视口遵守UxSpec最小尺寸()
    {
        string cssPath = FindRepositoryFile("docs", "ux-mockups", "mockup.css");
        string css = await File.ReadAllTextAsync(cssPath);

        StringAssert.Contains(css, "font-size: 13px;");
        StringAssert.Contains(css, "--augit-side-width: 300px;");
        StringAssert.Contains(css, "--augit-bottom-height: clamp(180px, 31vh, 305px);");
        StringAssert.Contains(css, "clamp(160px, 29%, 180px)");
        StringAssert.Contains(css, "clamp(190px, 32%, 210px)");
        Assert.DoesNotContain("--augit-side-width: 260px;", css);
        Assert.DoesNotContain("--augit-side-width: 280px;", css);
        Assert.DoesNotContain("--augit-bottom-height: 180px;", css);
        Assert.DoesNotContain("--augit-bottom-height: 235px;", css);
    }

    [TestMethod]
    public async Task 设置视觉稿不引入未确认的产品能力()
    {
        string mockupPath = FindRepositoryFile("docs", "ux-mockups", "mockup.js");
        string mockup = await File.ReadAllTextAsync(mockupPath);

        Assert.DoesNotContain("界面缩放", mockup);
        Assert.DoesNotContain("Islands Light", mockup);
        Assert.DoesNotContain(
            "<span class=\"fake-check checked\">✓</span>恢复上次打开的目录和标签",
            mockup);
        StringAssert.Contains(mockup, "启动时恢复上次打开的目录和标签");
    }

    [TestMethod]
    public async Task 文档页面同时具有真实审计入口和视觉稿()
    {
        string hostPath = FindRepositoryFile("tests", "Augit.App.VisualAuditHost", "Program.cs");
        string hostSource = await File.ReadAllTextAsync(hostPath);
        foreach (string surface in DocumentAuditSurfaces)
        {
            StringAssert.Contains(hostSource, $"case \"{surface}\":");
            Assert.IsTrue(
                File.Exists(FindRepositoryFile("docs", "ux-mockups", $"{surface}.html")),
                $"缺少 {surface} 视觉稿。");
        }
    }

    [TestMethod]
    public async Task 文件历史审计保留普通文本前台并单独限定历史路径()
    {
        string hostPath = FindRepositoryFile("tests", "Augit.App.VisualAuditHost", "Program.cs");
        string source = await File.ReadAllTextAsync(hostPath);

        StringAssert.Contains(
            source,
            "case \"file-history\":\n                // 文件历史视觉稿保留前台普通文本标签");
        StringAssert.Contains(
            source,
            "await OpenWorkspaceDocumentAsync(window, \"src\", \"Augit.App\", \"MainWindow.cs\");");
        StringAssert.Contains(
            source,
            "window.SelectTreePathForTest(Path.Combine(\n                    window.WorkspaceRoot,\n                    \"docs\",\n                    \"product-spec.md\"));");
    }

    [TestMethod]
    public async Task 已接入的视觉审计入口与视觉稿严格同名()
    {
        string hostPath = FindRepositoryFile("tests", "Augit.App.VisualAuditHost", "Program.cs");
        string hostSource = await File.ReadAllTextAsync(hostPath);
        foreach (string surface in ImplementedAuditSurfaces)
        {
            StringAssert.Contains(hostSource, $"case \"{surface}\":");
            Assert.IsTrue(
                File.Exists(FindRepositoryFile("docs", "ux-mockups", $"{surface}.html")),
                $"缺少 {surface} 视觉稿。");
        }

        foreach (string removedAlias in new[] { "project", "commit", "history", "compare" })
        {
            Assert.DoesNotContain($"case \"{removedAlias}\":", hostSource);
        }
    }

    [TestMethod]
    public async Task Ux规格视觉稿与真实审计入口保持一一对应()
    {
        string uxSpecPath = FindRepositoryFile("docs", "ux-spec.md");
        string uxSpec = await File.ReadAllTextAsync(uxSpecPath);
        int sectionStart = uxSpec.IndexOf("### 12.5 场景验收关联", StringComparison.Ordinal);
        // 12.6 是连续工作流契约，不属于单页审计入口映射；不能把工作流名称混入页面场景集合。
        int sectionEnd = uxSpec.IndexOf("### 12.6 连续工作流验收", sectionStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, sectionStart, "UX 规格缺少场景验收关联章节。");
        Assert.IsGreaterThan(sectionStart, sectionEnd, "UX 规格的场景验收关联章节不完整。");

        string[] specSurfaces = uxSpec[sectionStart..sectionEnd]
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('`')[1])
            .ToArray();
        string mockupDirectory = Path.GetDirectoryName(
            FindRepositoryFile("docs", "ux-mockups", "index.html"))!;
        string[] mockupSurfaces = Directory.GetFiles(mockupDirectory, "*.html")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.Equals(name, "index", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        Assert.HasCount(
            ImplementedAuditSurfaces.Length,
            specSurfaces,
            "UX 规格必须逐项列出全部真实审计场景，连续工作流另行校验。");
        Assert.HasCount(
            specSurfaces.Length,
            specSurfaces.Distinct(StringComparer.Ordinal),
            "UX 规格不能重复登记同一审计场景。");
        CollectionAssert.AreEquivalent(ImplementedAuditSurfaces, specSurfaces);
        CollectionAssert.AreEquivalent(ImplementedAuditSurfaces, mockupSurfaces);
    }

    [TestMethod]
    public async Task 视觉稿文件名与HTML场景标识一致()
    {
        string mockupDirectory = Path.GetDirectoryName(
            FindRepositoryFile("docs", "ux-mockups", "index.html"))!;
        foreach (string path in Directory.GetFiles(mockupDirectory, "*.html"))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(path), "index", StringComparison.Ordinal))
            {
                continue;
            }

            string html = await File.ReadAllTextAsync(path);
            const string marker = "data-scene=\"";
            int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, markerIndex, $"视觉稿缺少 data-scene：{path}");
            int valueStart = markerIndex + marker.Length;
            int valueEnd = html.IndexOf('"', valueStart);
            Assert.IsGreaterThan(valueStart, valueEnd, $"视觉稿 data-scene 格式错误：{path}");
            string scene = html[valueStart..valueEnd];
            Assert.AreEqual(
                Path.GetFileNameWithoutExtension(path),
                scene,
                $"视觉稿文件名和 data-scene 不一致：{path}");
        }
    }

    [TestMethod]
    public async Task 上下文菜单视觉稿与真实菜单文案一致()
    {
        string mockup = await File.ReadAllTextAsync(
            FindRepositoryFile("docs", "ux-mockups", "mockup.js"));
        foreach (string label in MainWindow.ProjectContextMenuLabelsForTest(directory: false))
        {
            StringAssert.Contains(mockup, label, $"项目右键菜单视觉稿缺少：{label}");
        }

        foreach (string label in NativeGitPanel.ChangesContextMenuLabelsForTest)
        {
            StringAssert.Contains(mockup, label, $"Changes 右键菜单视觉稿缺少：{label}");
        }

        foreach (string label in NativeGitHistoryPanel.HistoryContextMenuLabelsForTest)
        {
            StringAssert.Contains(mockup, label, $"Git 历史右键菜单视觉稿缺少：{label}");
        }

        Assert.DoesNotContain("在资源管理器中显示", mockup);
        Assert.DoesNotContain("从磁盘重新加载", mockup);
    }

    [TestMethod]
    public async Task Ux规格包含连续工作流验收契约()
    {
        string uxSpec = await File.ReadAllTextAsync(
            FindRepositoryFile("docs", "ux-spec.md"));
        StringAssert.Contains(uxSpec, "### 12.6 连续工作流验收");
        foreach (string workflow in new[]
        {
            "workspace-to-document",
            "commit-to-diff",
            "history-to-detail",
            "tool-window-switch",
            "popup-and-modal",
            "operation-feedback",
            "external-change",
        })
        {
            StringAssert.Contains(uxSpec, $"`{workflow}`");
        }

        StringAssert.Contains(uxSpec, "单个场景截图只能证明某个稳定帧存在");
        StringAssert.Contains(uxSpec, "不能单独证明连续工作流通过");
    }

    [TestMethod]
    public async Task PyCharm黑盒参考包含可执行体验契约()
    {
        string reference = await File.ReadAllTextAsync(
            FindRepositoryFile("docs", "pycharm-blackbox-reference.md"));
        StringAssert.Contains(reference, "## 9. 可执行探索记录格式");
        StringAssert.Contains(reference, "## 10. 核心工作流证据契约");
        foreach (string field in new[]
        {
            "前置状态",
            "用户动作",
            "即时反馈",
            "最终状态",
            "允许变化区域",
            "禁止变化区域",
            "返回路径",
            "证据等级",
        })
        {
            StringAssert.Contains(reference, field);
        }

        foreach (string scene in new[]
        {
            "PY-FRAME-01",
            "PY-PROJECT-01",
            "PY-COMMIT-01",
            "PY-COMMIT-02",
            "PY-COMMIT-03",
            "PY-HISTORY-01",
            "PY-HISTORY-02",
            "PY-SWITCH-01",
            "PY-POPUP-01",
            "PY-MODAL-01",
        })
        {
            StringAssert.Contains(reference, $"`{scene}`");
        }

        StringAssert.Contains(reference, "不能用测试钩子绕过被验收的点击、按键和页面跳转");
        StringAssert.Contains(reference, "不得为了复刻 PyCharm 引入已排除能力");
    }

    [TestMethod]
    public async Task 局部加载和终端确认审计使用真实窗口入口()
    {
        string hostPath = FindRepositoryFile("tests", "Augit.App.VisualAuditHost", "Program.cs");
        string hostSource = await File.ReadAllTextAsync(hostPath);
        StringAssert.Contains(hostSource, "ShowGitDiffLoadingForTest");
        StringAssert.Contains(hostSource, "ShowTerminalCloseConfirmationForTest");
        StringAssert.Contains(hostSource, "ShowOperationProgressForTest");
        StringAssert.Contains(hostSource, "ShowOperationResultForTest");
    }

    [TestMethod]
    public void Git操作状态映射为局部进度和结果通知()
    {
        var progress = MainWindow.ResolveOperationFeedbackForTest(UiText.SmartCheckoutRunning);
        Assert.IsNotNull(progress);
        Assert.IsTrue(progress.Value.IsProgress);
        Assert.IsTrue(progress.Value.CanCancel);
        Assert.AreEqual("正在恢复临时 Stash。", progress.Value.Detail);

        var failure = MainWindow.ResolveOperationFeedbackForTest("推送失败：当前仓库未配置远端。");
        Assert.IsNotNull(failure);
        Assert.IsFalse(failure.Value.IsProgress);
        Assert.IsTrue(failure.Value.IsError);
        Assert.AreEqual("推送失败", failure.Value.Title);
        Assert.AreEqual("当前仓库未配置远端。", failure.Value.Detail);

        Assert.IsNull(MainWindow.ResolveOperationFeedbackForTest(UiText.GitReady));
    }

    [TestMethod]
    [DataRow("image-preview")]
    [DataRow("file-limit")]
    public async Task 图片审计资产随宿主生命周期创建并清理(string surface)
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string settingsPath = temporary.GetPath("settings.json");
        Directory.CreateDirectory(workspace);
        await new SettingsStore(settingsPath).SaveAsync(new() { LastWorkspace = workspace });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add(surface);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        string? artifactDirectory = null;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (!process.HasExited && DateTime.UtcNow < deadline)
            {
                process.Refresh();
                string objectDirectory = Path.Combine(workspace, "obj");
                artifactDirectory = Directory.Exists(objectDirectory)
                    ? Directory.EnumerateDirectories(objectDirectory, "Augit.VisualAudit.*").SingleOrDefault()
                    : null;
                if (process.MainWindowHandle != 0 && artifactDirectory is not null)
                {
                    break;
                }

                await Task.Delay(25);
            }

            Assert.AreNotEqual(0, process.MainWindowHandle, "视觉审计宿主没有创建主窗口。");
            Assert.IsNotNull(artifactDirectory, "视觉审计资产目录没有创建。");
            string artifactPath = Directory.EnumerateFiles(artifactDirectory).Single();
            if (surface.Equals("image-preview", StringComparison.Ordinal))
            {
                byte[] header = new byte[29];
                await using FileStream stream = File.OpenRead(artifactPath);
                await stream.ReadExactlyAsync(header);
                Assert.AreEqual(0x89504E470D0A1A0AUL, BinaryPrimitives.ReadUInt64BigEndian(header));
                Assert.AreEqual(13, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8)));
                Assert.IsTrue(header.AsSpan(12, 4).SequenceEqual("IHDR"u8));
                Assert.AreEqual(1920, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)));
                Assert.AreEqual(1200, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
                Assert.AreEqual((byte)8, header[24]);
                Assert.AreEqual((byte)6, header[25], "审计样本使用 RGBA，以覆盖图片局部透明棋盘。");
            }
            else
            {
                Assert.AreEqual(5_033_165, new FileInfo(artifactPath).Length);
            }
            Assert.IsTrue(process.CloseMainWindow(), "无法关闭视觉审计宿主窗口。");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.AreEqual(0, process.ExitCode);
            Assert.IsFalse(Directory.Exists(artifactDirectory), "视觉审计资产目录没有随宿主退出清理。");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    public async Task 视觉审计宿主只使用指定设置文件()
    {
        using TemporaryDirectory temporary = new();
        string workspace = temporary.GetPath("workspace");
        string documentPath = Path.Combine(workspace, "文档.txt");
        string settingsPath = temporary.GetPath("isolated/settings.json");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(documentPath, "隔离审计");
        SettingsStore settingsStore = new(settingsPath);
        await settingsStore.SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = "Dark",
            OpenFiles = [documentPath],
            ActiveFile = documentPath,
        });

        string executablePath = FindAuditHostExecutable();
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add("main-project");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (process.MainWindowHandle == 0 && !process.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
                process.Refresh();
            }

            Assert.AreNotEqual(0, process.MainWindowHandle, "视觉审计宿主没有创建主窗口。");
            Assert.IsTrue(process.CloseMainWindow(), "无法关闭视觉审计宿主窗口。");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        ApplicationSettings saved = await settingsStore.LoadAsync();
        Assert.AreEqual(workspace, saved.LastWorkspace);
        Assert.AreEqual("Dark", saved.Theme);
        Assert.HasCount(1, saved.OpenFiles);
        Assert.AreEqual(documentPath, saved.ActiveFile);
        Assert.IsEmpty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(settingsPath)!,
                "*.audit",
                SearchOption.TopDirectoryOnly),
            "视觉审计宿主遗留了隔离设置副本。");
    }

    [TestMethod]
    [DataRow("commit-changes", "Changes", 35, 7)]
    [DataRow("git-history", "Changes", 35, 7)]
    [DataRow("git-history-graph", "Changes", 35, 7)]
    [DataRow("git-history-filter-overflow", "Changes", 35, 7)]
    [DataRow("git-history-toolbar-overflow", "Changes", 35, 7)]
    [DataRow("commit-empty", "CommitEmpty", 0, 0)]
    public async Task Git视觉审计使用固定状态并清理临时仓库(
        string surface,
        string auditKind,
        int expectedTracked,
        int expectedUntracked)
    {
        using TemporaryDirectory temporary = new();
        string workspace = Path.GetDirectoryName(FindRepositoryFile("Augit.slnx"))!;
        string settingsPath = temporary.GetPath("settings.json");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = "Light",
            Window = new() { Width = 1024, Height = 640 },
        });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add(surface);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        string? auditDirectory = null;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!process.HasExited && DateTime.UtcNow < deadline)
            {
                process.Refresh();
                auditDirectory = Directory.EnumerateDirectories(
                        Path.GetTempPath(),
                        $"Augit.VisualAudit.{auditKind}.{process.Id}.*")
                    .SingleOrDefault();
                if (process.MainWindowHandle != 0 && auditDirectory is not null)
                {
                    break;
                }

                await Task.Delay(25);
            }

            string diagnostic = process.HasExited
                ? await standardError
                : "视觉审计宿主仍在运行，但尚未创建主窗口。";
            Assert.AreNotEqual(
                0,
                process.MainWindowHandle,
                $"视觉审计宿主没有创建主窗口。{Environment.NewLine}{diagnostic}");
            Assert.IsNotNull(auditDirectory, "提交视觉审计没有创建隔离仓库。");
            string repository = Path.Combine(auditDirectory, "Augit");
            (int tracked, int untracked) = await ReadGitStatusCountsAsync(repository);
            Assert.AreEqual(expectedTracked, tracked, "隔离仓库的 Changes 数量不稳定。");
            Assert.AreEqual(expectedUntracked, untracked, "隔离仓库的未跟踪文件数量不稳定。");

            Assert.IsTrue(process.CloseMainWindow(), "无法关闭视觉审计宿主窗口。");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.AreEqual(0, process.ExitCode);
            Assert.IsFalse(Directory.Exists(auditDirectory), "视觉审计宿主退出后遗留了隔离仓库。");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }

            if (auditDirectory is not null && Directory.Exists(auditDirectory))
            {
                DeleteDirectoryWithRetry(auditDirectory);
            }
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

        Assert.IsFalse(Directory.Exists(directory), $"无法清理视觉审计临时目录：{directory}");
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

    public static IEnumerable<object[]> 全部视觉审计场景主题组合()
    {
        foreach (string surface in ImplementedAuditSurfaces)
        {
            yield return [surface, "Light"];
            yield return [surface, "Dark"];
        }
    }

    public static IEnumerable<object[]> 全部视觉审计场景高Dpi主题组合()
    {
        foreach (string surface in ImplementedAuditSurfaces)
        {
            foreach (string theme in new[] { "Light", "Dark" })
            {
                yield return [surface, theme, 120];
                yield return [surface, theme, 144];
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(全部视觉审计场景主题组合))]
    public Task 全部视觉审计场景在最小窗口浅色和深色主题下可以截图后自动退出(
        string surface,
        string theme) =>
        验证视觉审计场景可以截图后退出Async(surface, theme, 96, 1024, 640);

    [TestMethod]
    [DynamicData(nameof(全部视觉审计场景主题组合))]
    public Task 全部视觉审计场景在推荐窗口浅色和深色主题下可以截图后自动退出(
        string surface,
        string theme) =>
        验证视觉审计场景可以截图后退出Async(surface, theme, 96, 1180, 760);

    [TestMethod]
    [DynamicData(nameof(全部视觉审计场景高Dpi主题组合))]
    public Task 全部视觉审计场景在最小窗口高Dpi主题矩阵下可以截图后自动退出(
        string surface,
        string theme,
        int dpi) =>
        验证视觉审计场景可以截图后退出Async(surface, theme, dpi, 1024, 640);

    [TestMethod]
    [DynamicData(nameof(全部视觉审计场景高Dpi主题组合))]
    public Task 全部视觉审计场景在推荐窗口高Dpi主题矩阵下可以截图后自动退出(
        string surface,
        string theme,
        int dpi) =>
        验证视觉审计场景可以截图后退出Async(surface, theme, dpi, 1180, 760);

    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 96)]
    [DataRow("Light", 120)]
    [DataRow("Dark", 120)]
    [DataRow("Light", 144)]
    [DataRow("Dark", 144)]
    public Task Git历史复杂提交图在主题和Dpi矩阵下保留双轨(
        string theme,
        int dpi) =>
        验证视觉审计场景可以截图后退出Async("git-history-graph", theme, dpi, 1180, 760);

    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 144)]
    public Task Git历史十二轨在窄窗口真实仓库中生成并清理(string theme, int dpi) =>
        验证视觉审计场景可以截图后退出Async("git-history-graph", theme, dpi, 1024, 760, wideGraph: true);

    public static IEnumerable<object[]> HistoryDiffNoticeMatrix()
    {
        foreach (string surface in new[] { "history-diff-loading", "history-diff-failure", "history-diff-cancelled" })
            foreach (string theme in new[] { "Light", "Dark" })
                foreach (int dpi in new[] { 96, 120, 144 })
                    foreach ((int width, int height) in new[] { (1024, 640), (1180, 760) })
                    {
                        yield return [surface, theme, dpi, width, height];
                    }
    }

    public static IEnumerable<object[]> DiffBoundaryMatrix()
    {
        foreach (string theme in new[] { "Light", "Dark" })
            foreach (int dpi in new[] { 96, 120, 144 })
                foreach ((int width, int height) in new[] { (1024, 640), (1180, 760) })
                {
                    yield return [theme, dpi, width, height];
                }
    }

    [TestMethod]
    [DynamicData(nameof(DiffBoundaryMatrix))]
    public Task Diff文件边界提示在主题Dpi和窗口矩阵中不越界(string theme, int dpi, int width, int height) =>
        验证视觉审计场景可以截图后退出Async("diff-boundary", theme, dpi, width, height);

    [TestMethod]
    [DynamicData(nameof(DiffBoundaryMatrix))]
    public Task 跳转行窗口在主题Dpi和窗口矩阵中居中且关闭后退出(string theme, int dpi, int width, int height) =>
        验证视觉审计场景可以截图后退出Async("go-to-line", theme, dpi, width, height);

    [TestMethod]
    [DynamicData(nameof(DiffBoundaryMatrix))]
    public Task 引用比较固定样本的工具栏在主题Dpi和窗口矩阵中可见且不越界(string theme, int dpi, int width, int height) =>
        验证视觉审计场景可以截图后退出Async("git-compare", theme, dpi, width, height);

    [TestMethod]
    [DynamicData(nameof(DiffBoundaryMatrix))]
    public async Task 单栏文件信息在工作区和引用比较矩阵中保持两行身份(string theme, int dpi, int width, int height)
    {
        await 验证视觉审计场景可以截图后退出Async("git-compare", theme, dpi, width, height, unified: true);
        await 验证视觉审计场景可以截图后退出Async("commit-diff", theme, dpi, width, height, unified: true);
    }

    [TestMethod]
    [DynamicData(nameof(HistoryDiffNoticeMatrix))]
    public Task 历史Diff中间状态可以截图并释放查询(string surface, string theme, int dpi, int width, int height) =>
        验证视觉审计场景可以截图后退出Async(surface, theme, dpi, width, height);

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public Task JSON原文审计经过实际模式切换且自动退出(string theme) =>
        验证视觉审计场景可以截图后退出Async("json-preview", theme, 96, 1024, 640, jsonSource: true);

    [TestMethod]
    [DataRow("Light", "left")]
    [DataRow("Dark", "left")]
    [DataRow("Light", "right")]
    [DataRow("Dark", "right")]
    public Task 首行空侧冲突的真实Git审计可以截图并退出(string theme, string emptySide) =>
        验证视觉审计场景可以截图后退出Async("conflict-resolver", theme, 96, 1440, 900, emptyConflictSide: emptySide);

    private static async Task 验证视觉审计场景可以截图后退出Async(
        string surface,
        string theme,
        int dpi,
        int logicalWidth,
        int logicalHeight,
        bool unified = false,
        bool jsonSource = false,
        bool wideGraph = false,
        string? emptyConflictSide = null)
    {
        using TemporaryDirectory temporary = new();
        string workspace = Path.GetDirectoryName(FindRepositoryFile("Augit.slnx"))!;
        string settingsPath = temporary.GetPath("settings.json");
        string capturePath = temporary.GetPath($"{surface}-{theme}-{dpi}-{logicalWidth}x{logicalHeight}.bmp");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = theme,
            Window = new()
            {
                Left = 100,
                Top = 100,
                Width = logicalWidth,
                Height = logicalHeight,
            },
        });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add(surface);
        startInfo.ArgumentList.Add(capturePath);
        startInfo.ArgumentList.Add($"--dpi={dpi}");
        if (unified) startInfo.ArgumentList.Add("--unified");
        if (jsonSource) startInfo.ArgumentList.Add("--json-source");
        if (wideGraph) startInfo.ArgumentList.Add("--wide-graph");
        if (emptyConflictSide is not null) startInfo.ArgumentList.Add($"--empty-{emptyConflictSide}");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(capturePath), "视觉审计宿主没有输出截图。");
            byte[] bitmap = await File.ReadAllBytesAsync(capturePath);
            if (wideGraph)
            {
                string rightPath = Path.ChangeExtension(capturePath, ".right.bmp");
                Assert.IsTrue(File.Exists(rightPath), "十二轨审计未生成横向滚动后的实际画面。");
                byte[] right = await File.ReadAllBytesAsync(rightPath);
                Assert.IsFalse(bitmap.SequenceEqual(right), "横向滚动后画面没有更新。");
            }
            Assert.AreEqual((byte)'B', bitmap[0]);
            Assert.AreEqual((byte)'M', bitmap[1]);
            Assert.IsGreaterThanOrEqualTo(logicalWidth * dpi / 96, BitConverter.ToInt32(bitmap, 18));
            Assert.IsGreaterThanOrEqualTo(logicalHeight * dpi / 96, Math.Abs(BitConverter.ToInt32(bitmap, 22)));
            if (surface.Equals("blame", StringComparison.OrdinalIgnoreCase) || emptyConflictSide is not null)
            {
                // 只有显式归档时保留截图，普通回归不在仓库生成永久产物。
                string? evidenceDirectory = Environment.GetEnvironmentVariable("AUGIT_VISUAL_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidenceDirectory))
                {
                    Directory.CreateDirectory(evidenceDirectory);
                    string fileName = emptyConflictSide is null ? Path.GetFileName(capturePath)
                        : $"conflict-empty-{emptyConflictSide}-{theme.ToLowerInvariant()}.bmp";
                    File.Copy(capturePath, Path.Combine(evidenceDirectory, fileName), overwrite: true);
                }
            }
            if (surface is "main-project" or "commit-empty" or "commit-changes")
            {
                AssertWorkspaceSurfaceCorners(bitmap, logicalWidth, theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }
            if (surface.Equals("git-unavailable", StringComparison.OrdinalIgnoreCase))
            {
                AssertGitUnavailableNotificationRendered(bitmap);
            }

            if (surface.Equals("markdown-preview", StringComparison.OrdinalIgnoreCase))
            {
                AssertMarkdownModeToolbarRendered(bitmap, logicalWidth, theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }

            if (surface.Equals("main-project", StringComparison.OrdinalIgnoreCase)
                || surface.Equals("git-history", StringComparison.OrdinalIgnoreCase)
                || surface.Equals("diff-loading", StringComparison.OrdinalIgnoreCase))
            {
                AssertGitHistorySideToolbarRendered(bitmap, logicalWidth, logicalHeight,
                    dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }

            if (surface.Equals("git-history-graph", StringComparison.OrdinalIgnoreCase))
            {
                AssertGitHistoryGraphRendered(
                    bitmap,
                    logicalWidth,
                    logicalHeight,
                    dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }

            if (surface.Equals("diff-loading", StringComparison.OrdinalIgnoreCase)
                || surface.Equals("history-diff-loading", StringComparison.OrdinalIgnoreCase))
            {
                AssertDiffLoadingSkeletonRendered(
                    bitmap,
                    dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase),
                    logicalWidth,
                    logicalHeight);
            }
            if (surface is "history-diff-failure" or "history-diff-cancelled")
            {
                AssertHistoryComparisonNoticeRendered(bitmap, logicalWidth,
                    dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase),
                    failed: surface == "history-diff-failure");
            }
            if (surface.Equals("git-compare", StringComparison.OrdinalIgnoreCase))
            {
                AssertComparisonSummaryRendered(bitmap, logicalWidth,
                    dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }
            if (surface is "git-compare" or "commit-diff" or "diff-boundary")
            {
                AssertDiffModeGroupRendered(bitmap, logicalWidth, theme.Equals("Dark", StringComparison.OrdinalIgnoreCase), !unified);
                AssertDiffFileHeaderRendered(bitmap, logicalWidth, theme.Equals("Dark", StringComparison.OrdinalIgnoreCase), !unified);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        if (surface.StartsWith("push", StringComparison.OrdinalIgnoreCase))
        {
            Assert.IsEmpty(
                Directory.EnumerateDirectories(
                    Path.GetTempPath(),
                    $"Augit.VisualAudit.Push.{process.Id}.*"),
                "Push 视觉审计宿主遗留了临时仓库。");
        }
        if (surface.StartsWith("history-diff-", StringComparison.OrdinalIgnoreCase)
            || surface.Equals("git-compare", StringComparison.OrdinalIgnoreCase))
        {
            Assert.IsEmpty(
                Directory.EnumerateDirectories(
                    Path.GetTempPath(),
                    $"Augit.VisualAudit.Changes.{process.Id}.*"),
                "比较视觉审计宿主遗留了临时仓库。");
        }
    }

    [TestMethod]
    [DataRow("Light", 96, 1024, 640)]
    [DataRow("Dark", 96, 1024, 640)]
    [DataRow("Light", 120, 1024, 640)]
    [DataRow("Dark", 120, 1024, 640)]
    [DataRow("Light", 144, 1024, 640)]
    [DataRow("Dark", 144, 1024, 640)]
    [DataRow("Light", 96, 1180, 760)]
    [DataRow("Dark", 96, 1180, 760)]
    public async Task 主流程视觉审计在主题窗口和Dpi矩阵下生成真实截图并退出(
        string theme,
        int dpi,
        int logicalWidth,
        int logicalHeight)
    {
        using TemporaryDirectory temporary = new();
        string workspace = Path.GetDirectoryName(FindRepositoryFile("Augit.slnx"))!;
        string settingsPath = temporary.GetPath("settings.json");
        string capturePath = temporary.GetPath($"main-project-{theme}-{dpi}.bmp");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = theme,
            Window = new()
            {
                Left = 100,
                Top = 100,
                Width = logicalWidth,
                Height = logicalHeight,
            },
        });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add("main-project");
        startInfo.ArgumentList.Add(capturePath);
        startInfo.ArgumentList.Add($"--dpi={dpi}");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
            Assert.AreEqual(0, process.ExitCode);
            byte[] bitmap = await File.ReadAllBytesAsync(capturePath);
            Assert.AreEqual((byte)'B', bitmap[0]);
            Assert.AreEqual((byte)'M', bitmap[1]);
            Assert.IsGreaterThanOrEqualTo(logicalWidth * dpi / 96, BitConverter.ToInt32(bitmap, 18));
            Assert.IsGreaterThanOrEqualTo(logicalHeight * dpi / 96, Math.Abs(BitConverter.ToInt32(bitmap, 22)));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(DiffBoundaryMatrix))]
    public Task Blame视觉审计在主题窗口和Dpi矩阵下生成真实截图并退出(
        string theme,
        int dpi,
        int logicalWidth,
        int logicalHeight) =>
        验证视觉审计场景可以截图后退出Async("blame", theme, dpi, logicalWidth, logicalHeight);

    [TestMethod]
    [DataRow("Light", 96)]
    [DataRow("Dark", 96)]
    [DataRow("Light", 120)]
    [DataRow("Dark", 120)]
    [DataRow("Light", 144)]
    [DataRow("Dark", 144)]
    public async Task Diff局部加载视觉审计在主题和Dpi矩阵下保留正文骨架(
        string theme,
        int dpi)
    {
        using TemporaryDirectory temporary = new();
        string workspace = Path.GetDirectoryName(FindRepositoryFile("Augit.slnx"))!;
        string settingsPath = temporary.GetPath("settings.json");
        string capturePath = temporary.GetPath($"diff-loading-{theme}-{dpi}.bmp");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            LastWorkspace = workspace,
            Theme = theme,
            Window = new()
            {
                Left = 100,
                Top = 100,
                Width = 1024,
                Height = 640,
            },
        });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add("diff-loading");
        startInfo.ArgumentList.Add(capturePath);
        startInfo.ArgumentList.Add($"--dpi={dpi}");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
            Assert.AreEqual(0, process.ExitCode);
            byte[] bitmap = await File.ReadAllBytesAsync(capturePath);
            AssertDiffLoadingSkeletonRendered(
                bitmap,
                dark: theme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static void AssertGitUnavailableNotificationRendered(byte[] bitmap)
    {
        int pixelOffset = BitConverter.ToInt32(bitmap, 10);
        int width = BitConverter.ToInt32(bitmap, 18);
        int storedHeight = BitConverter.ToInt32(bitmap, 22);
        int height = Math.Abs(storedHeight);
        int redPixelCount = 0;
        for (int y = height * 3 / 4; y < height - 20; y++)
        {
            int storedRow = storedHeight < 0 ? y : height - y - 1;
            for (int x = width * 3 / 5; x < width - 20; x++)
            {
                int pixel = pixelOffset + ((storedRow * width) + x) * 4;
                byte blue = bitmap[pixel];
                byte green = bitmap[pixel + 1];
                byte red = bitmap[pixel + 2];
                if (red >= 160 && red >= green + 40 && red >= blue + 40)
                {
                    redPixelCount++;
                }
            }
        }

        Assert.IsGreaterThan(
            20,
            redPixelCount,
            "Git 不可用截图中没有通知标题的错误色像素，通知可能再次被正文漏画。");
    }

    private static void AssertMarkdownModeToolbarRendered(byte[] bitmap, int logicalWidth, bool dark)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        // 分别缩放每个组件再累加，避免高 DPI 舍入改变按钮中心。
        double scale = surface.Width / (double)logicalWidth;
        int S(int value) => (int)Math.Round(value * scale);
        int buttonWidth = S(26);
        int segmentRight = surface.Width - S(8) - S(8) - S(28) - S(4);
        int segmentLeft = segmentRight - buttonWidth * 3;
        int top = S(44) + S(42) + (S(36) - buttonWidth) / 2;
        uint iconColor = NativeTheme.DocumentModeIconColor(dark);
        for (int index = 0; index < 3; index++)
        {
            int left = segmentLeft + index * buttonWidth;
            int iconPixels = surface.CountPixels(
                iconColor,
                left + S(4), top + S(4),
                left + buttonWidth - S(4), top + buttonWidth - S(4),
                tolerance: 80);
            // 允许细笔画的抗锯齿混色，但浅色选中底和深色面板均不能冒充图标。
            Assert.IsGreaterThan(
                Math.Max(8, (int)Math.Round(20 * scale)),
                iconPixels,
                $"Markdown 模式按钮 {index + 1} 内没有检测到图标绘制。");
        }
    }

    private static void AssertWorkspaceSurfaceCorners(byte[] bitmap, int logicalWidth, bool dark)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        using IDisposable dpiOverride = NativeTheme.PushVisualAuditDpiOverride((int)Math.Round(96 * scale));
        int S(int value) => (int)Math.Round(value * scale);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        int panelLeft = MainWindow.MainPanelLeftForTest;
        int documentLeft = panelLeft + NativeTheme.Scale(300) + NativeTheme.Scale(4);
        // 两个表面左边界必须与主窗口布局共用同一基准，不能在审计中复制旧坐标。
        // 9px 半径下 (2,2) 仍在圆角外，(10,2) 已进入白色/深色面板内部。
        foreach (int left in new[] { panelLeft, documentLeft })
        {
            int top = S(44);
            Assert.AreEqual(1, surface.CountPixels(palette.Chrome,
                left + S(2), top + S(2), left + S(2) + 1, top + S(2) + 1, tolerance: 3), "主面板圆角不能按半径的一半绘制");
            Assert.AreEqual(1, surface.CountPixels(palette.Panel,
                left + S(10), top + S(2), left + S(10) + 1, top + S(2) + 1), "圆角内必须连续填充面板背景");
        }
    }

    private static void AssertGitHistorySideToolbarRendered(
        byte[] bitmap,
        int logicalWidth = 1024,
        int logicalHeight = 640,
        bool dark = false)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        using IDisposable dpiOverride = NativeTheme.PushVisualAuditDpiOverride((int)Math.Round(96 * scale));
        // 宿主导出客户区：统一主面板左边界、300px 紧凑侧栏、4px 面板间隙。
        // 不能把工具按钮内部的 6px 留白再次算入面板位置，也不能检测已收入溢出菜单的按钮。
        int documentLeft = MainWindow.MainPanelLeftForTest
            + NativeTheme.Scale(300)
            + NativeTheme.Scale(4);
        int statusHeight = (int)Math.Round(22 * scale);
        int bottomHeight = Math.Clamp(
            (int)Math.Round(logicalHeight * 0.31d * scale),
            (int)Math.Round(180 * scale),
            (int)Math.Round(305 * scale));
        int panelTop = surface.Height - statusHeight - bottomHeight + 1;
        int sideWidth = NativeGitHistoryPanel.SideToolbarWidthForTest;
        int buttonSize = NativeTheme.Scale(28);
        int buttonLeft = documentLeft + 1 + Math.Max(0, (sideWidth - buttonSize) / 2);
        var positions = NativeGitHistoryPanel.CalculateSideToolbarButtonTopsForTest();
        int[] buttonTops = [positions.Back, positions.CreateReference, positions.DeleteReference,
            positions.Refresh, positions.Search, positions.Compare, positions.LocateHead];
        var layout = NativeGitHistoryPanel.CalculateSideToolbarLayout(bottomHeight - 2);
        // 对可见按钮和实际溢出入口分别检查，不能依靠降低阈值让错位采样通过。
        IEnumerable<int> visibleTops = buttonTops.Take(layout.VisibleCount);
        if (layout.OverflowTop >= 0) visibleTops = visibleTops.Append(layout.OverflowTop);
        foreach (int buttonTop in visibleTops)
        {
            int top = panelTop + buttonTop;
            int contrastingPixels = surface.CountPixelsDifferentFrom(NativeTheme.Palette(dark).Panel,
                buttonLeft + Math.Max(1, (int)Math.Round(7 * scale)),
                top + Math.Max(1, (int)Math.Round(6 * scale)),
                buttonLeft + buttonSize - Math.Max(1, (int)Math.Round(7 * scale)),
                top + buttonSize - Math.Max(1, (int)Math.Round(6 * scale)));
            Assert.IsGreaterThan(
                // 返回、刷新和搜索均为细线图标，抗锯齿后有效像素数不会随面积线性增长。
                Math.Max(6, (int)Math.Round(7 * scale)),
                contrastingPixels,
                $"Git History 侧边工具按钮（顶部 {buttonTop}）内没有检测到图标绘制。");
        }
    }

    private static void AssertDiffLoadingSkeletonRendered(
        byte[] bitmap,
        bool dark = false,
        int logicalWidth = 1024,
        int logicalHeight = 640)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        int left = (int)Math.Round(368 * scale);
        int top = (int)Math.Round(151 * scale);
        int right = surface.Width - (int)Math.Round(7 * scale);
        int statusHeight = (int)Math.Round(22 * scale);
        int bottomHeight = Math.Clamp(
            (int)Math.Round(logicalHeight * 0.31d * scale),
            (int)Math.Round(180 * scale),
            (int)Math.Round(305 * scale));
        int bottomPanelTop = surface.Height
            - statusHeight
            - bottomHeight
            + Math.Max(1, (int)Math.Round(scale));
        int bottom = Math.Min(
            bottomPanelTop,
            top + (int)Math.Round(360 * scale));
        int area = Math.Max(1, (right - left) * (bottom - top));
        NativeThemePalette palette = NativeTheme.Palette(dark);

        int gutterPixels = surface.CountPixels(palette.PanelMuted, left, top, right, bottom);
        int skeletonPixels = surface.CountPixels(palette.Hover, left, top, right, bottom);
        Assert.IsGreaterThan(
            area / 30,
            gutterPixels,
            "Diff 加载截图中没有稳定的中央行号栏，正文可能再次退回整块空白。");
        Assert.IsGreaterThan(
            area / 30,
            skeletonPixels,
            "Diff 加载截图中没有左右文本骨架，正文可能再次退回整块空白。");
    }

    private static void AssertComparisonSummaryRendered(byte[] bitmap, int logicalWidth, bool dark)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        int S(int value) => (int)Math.Round(value * scale);
        // 计数在右侧操作之前；取文字内部区域，排除按钮、文件栏和边框，检测空控件遮挡文字的回归。
        Assert.IsGreaterThan(20, surface.CountPixelsDifferentFrom(NativeTheme.Palette(dark).Panel,
            surface.Width - S(245), S(94), surface.Width - S(174), S(116)),
            "比较差异计数虽存在于控件文本中，但没有实际绘制或被其他控件遮挡。");
    }

    private static void AssertDiffFileHeaderRendered(byte[] bitmap, int logicalWidth, bool dark, bool sideBySide)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        using IDisposable dpi = NativeTheme.PushVisualAuditDpiOverride((int)Math.Round(scale * 96));
        NativeMethods.Rectangle bounds = new()
        {
            Left = NativeTheme.Scale(358),
            Top = NativeTheme.Scale(125),
            Right = surface.Width - NativeTheme.Scale(7),
            Bottom = NativeTheme.Scale(125) + NativeDiffFileHeader.Height(sideBySide)
        };
        NativeDiffFileHeaderLayout layout = NativeDiffFileHeader.Calculate(bounds, sideBySide, NativeTheme.Scale(80));
        NativeThemePalette palette = NativeTheme.Palette(dark);
        Assert.IsGreaterThan(15, surface.CountPixelsDifferentFrom(palette.Panel,
            layout.Source.Left, layout.Source.Top, layout.Source.Right, layout.Source.Bottom), "来源引用没有实际绘制。");
        Assert.IsGreaterThan(15, surface.CountPixelsDifferentFrom(palette.Panel,
            layout.Target.Left, layout.Target.Top, layout.Target.Right, layout.Target.Bottom), "目标引用没有在对应正文位置绘制。");
        Assert.IsGreaterThan(15, surface.CountPixelsDifferentFrom(palette.Panel,
            layout.Path.Left, layout.Path.Top, layout.Path.Right, layout.Path.Bottom), "路径没有实际绘制。");
    }

    private static void AssertDiffModeGroupRendered(byte[] bitmap, int logicalWidth, bool dark, bool sideBySide)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        int S(int value) => (int)Math.Round(value * scale);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        // 固定主框架的 Diff 顶边为 86px；检查父窗口外框和子按钮均实际绘制。
        Assert.IsGreaterThan(35, surface.CountPixels(palette.BorderStrong,
            surface.Width - S(119), S(89), surface.Width - S(61), S(93), tolerance: 4),
            "分段按钮的整体上边框没有绘制。");
        uint groupColor = dark ? palette.PanelMuted : 0x00F7F5F4;
        Assert.IsGreaterThan(15, surface.CountPixels(sideBySide ? groupColor : palette.Panel,
            surface.Width - S(83), S(98), surface.Width - S(77), S(108), tolerance: 1),
            "右侧模式按钮没有使用当前选中状态的底色。");
        Assert.IsGreaterThan(15, surface.CountPixels(sideBySide ? palette.Panel : groupColor,
            surface.Width - S(122), S(98), surface.Width - S(116), S(108), tolerance: 1),
            "左侧模式按钮没有使用当前选中状态的底色。");
    }

    private static void AssertHistoryComparisonNoticeRendered(byte[] bitmap, int logicalWidth, bool dark, bool failed)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        int S(int value) => (int)Math.Round(value * scale);
        NativeThemePalette palette = NativeTheme.Palette(dark);
        // 统一布局中比较正文起于 358/156px；检查局部提示图标和文字确实被绘制，不能只检查 BMP 存在。
        int left = S(358);
        int top = S(156);
        Assert.IsGreaterThan(6, surface.CountPixels(failed ? palette.Danger : palette.Muted,
            left + S(24), top + S(28), left + S(41), top + S(45), tolerance: 36),
            "比较提示的错误或取消状态图标没有绘制。");
        Assert.IsGreaterThan(30, surface.CountPixelsDifferentFrom(palette.Panel,
            left + S(48), top + S(27), left + S(310), top + S(53)),
            "比较提示没有绘制可见的原因文字。");
    }

    private static void AssertGitHistoryGraphRendered(
        byte[] bitmap,
        int logicalWidth,
        int logicalHeight,
        bool dark)
    {
        BitmapSurface surface = BitmapSurface.Read(bitmap);
        double scale = surface.Width / (double)logicalWidth;
        using IDisposable dpiOverride = NativeTheme.PushVisualAuditDpiOverride((int)Math.Round(96 * scale));
        int panelLeft = MainWindow.MainPanelLeftForTest;
        int documentLeft = panelLeft + NativeTheme.Scale(300) + NativeTheme.Scale(4);
        int statusHeight = (int)Math.Round(22 * scale);
        int bottomHeight = Math.Clamp(
            (int)Math.Round(logicalHeight * 0.31d * scale),
            (int)Math.Round(180 * scale),
            (int)Math.Round(305 * scale));
        int panelTop = surface.Height - statusHeight - bottomHeight + Math.Max(1, (int)Math.Round(scale));
        int contentWidth = Math.Max(
            0,
            surface.Width - documentLeft - NativeTheme.Scale(7) - NativeGitHistoryPanel.SideToolbarWidthForTest);
        (int branchWidth, _, _) = NativeGitHistoryPanel.GetColumnWidthsForTest(contentWidth);
        int graphLeft = documentLeft
            + NativeGitHistoryPanel.SideToolbarWidthForTest
            + branchWidth
            + NativeTheme.Scale(1);
        int graphTop = panelTop
            + NativeTheme.Scale(38 + 36);
        int graphRight = graphLeft + NativeTheme.Scale(45);
        int graphBottom = graphTop + NativeTheme.Scale(27 * 4);
        int primaryPixels = surface.CountPixels(
            NativeTheme.GitGraphColor(0, dark),
            graphLeft,
            graphTop,
            graphRight,
            graphBottom,
            tolerance: 36);
        int secondaryPixels = surface.CountPixels(
            NativeTheme.GitGraphColor(1, dark),
            graphLeft,
            graphTop,
            graphRight,
            graphBottom,
            tolerance: 36);
        Assert.IsGreaterThan(18, primaryPixels, "复杂 Git 历史截图缺少主轨提交图。");
        Assert.IsGreaterThan(8, secondaryPixels, "复杂 Git 历史截图缺少分叉次轨提交图。");
    }

    private sealed class BitmapSurface
    {
        private BitmapSurface(
            int width,
            int height,
            int pixelOffset,
            int bytesPerPixel,
            bool topDown,
            byte[] pixels)
        {
            Width = width;
            Height = height;
            PixelOffset = pixelOffset;
            BytesPerPixel = bytesPerPixel;
            TopDown = topDown;
            Pixels = pixels;
        }

        internal int Width { get; }

        internal int Height { get; }

        private int PixelOffset { get; }

        private int BytesPerPixel { get; }

        private bool TopDown { get; }

        private byte[] Pixels { get; }

        internal static BitmapSurface Read(byte[] bitmap)
        {
            int width = BitConverter.ToInt32(bitmap, 18);
            int storedHeight = BitConverter.ToInt32(bitmap, 22);
            int height = Math.Abs(storedHeight);
            short bitsPerPixel = BitConverter.ToInt16(bitmap, 28);
            Assert.AreEqual(32, bitsPerPixel, "视觉审计截图必须是 32 位 BMP。");
            return new(width, height, BitConverter.ToInt32(bitmap, 10), 4, storedHeight < 0, bitmap);
        }

        internal int CountContrastingPixels(int left, int top, int right, int bottom)
        {
            int count = 0;
            left = Math.Clamp(left, 0, Width);
            top = Math.Clamp(top, 0, Height);
            right = Math.Clamp(right, left, Width);
            bottom = Math.Clamp(bottom, top, Height);
            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    int offset = PixelOffset + (StoredRow(y) * Width + x) * BytesPerPixel;
                    byte blue = Pixels[offset];
                    byte green = Pixels[offset + 1];
                    byte red = Pixels[offset + 2];
                    if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) >= 24
                        || red + green + blue <= 480)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        internal int CountPixels(uint color, int left, int top, int right, int bottom, int tolerance = 0)
        {
            int count = 0;
            byte expectedRed = (byte)(color & 0xFF);
            byte expectedGreen = (byte)((color >> 8) & 0xFF);
            byte expectedBlue = (byte)((color >> 16) & 0xFF);
            left = Math.Clamp(left, 0, Width);
            top = Math.Clamp(top, 0, Height);
            right = Math.Clamp(right, left, Width);
            bottom = Math.Clamp(bottom, top, Height);
            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    int offset = PixelOffset + (StoredRow(y) * Width + x) * BytesPerPixel;
                    if (Math.Abs(Pixels[offset] - expectedBlue) <= tolerance
                        && Math.Abs(Pixels[offset + 1] - expectedGreen) <= tolerance
                        && Math.Abs(Pixels[offset + 2] - expectedRed) <= tolerance)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        internal int CountPixelsDifferentFrom(uint background, int left, int top, int right, int bottom)
        {
            int count = 0;
            int red = (int)(background & 255);
            int green = (int)((background >> 8) & 255);
            int blue = (int)((background >> 16) & 255);
            for (int y = Math.Max(0, top); y < Math.Min(Height, bottom); y++)
            {
                for (int x = Math.Max(0, left); x < Math.Min(Width, right); x++)
                {
                    int offset = PixelOffset + (StoredRow(y) * Width + x) * BytesPerPixel;
                    // 相对当前主题底色计数：深色空白不能通过，浅色禁用笔画也不能被忽略。
                    if (Math.Max(Math.Abs(Pixels[offset] - blue),
                        Math.Max(Math.Abs(Pixels[offset + 1] - green), Math.Abs(Pixels[offset + 2] - red))) >= 24)
                        count++;
                }
            }
            return count;
        }

        private int StoredRow(int topDownRow)
        {
            return TopDown ? topDownRow : Height - topDownRow - 1;
        }
    }

    private static async Task<(int Tracked, int Untracked)> ReadGitStatusCountsAsync(string repository)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git.exe",
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[]
        {
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--ignore-submodules=all",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git.exe 读取视觉审计状态。");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.AreEqual(0, process.ExitCode, error);

        string[] records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        int tracked = records.Count(record => record.Length >= 3 && record[0] != '?' && record[1] != '?');
        int untracked = records.Count(record => record.StartsWith("?? ", StringComparison.Ordinal));
        return (tracked, untracked);
    }

    [TestMethod]
    public async Task 视觉审计宿主可以生成真实冲突页面并清理临时仓库()
    {
        using TemporaryDirectory temporary = new();
        string workspace = FindRepositoryFile("Augit.slnx") is { } solution
            ? Path.GetDirectoryName(solution)!
            : throw new InvalidOperationException("无法确定仓库路径。");
        string settingsPath = temporary.GetPath("settings.json");
        string capturePath = temporary.GetPath("conflict.bmp");
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            LastWorkspace = workspace,
            Window = new()
            {
                Left = 100,
                Top = 100,
                Width = 1024,
                Height = 640,
            },
        });

        ProcessStartInfo startInfo = new()
        {
            FileName = FindAuditHostExecutable(),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(settingsPath);
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add("conflict-resolver");
        startInfo.ArgumentList.Add(capturePath);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动视觉审计宿主。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.AreEqual(0, process.ExitCode);
            Assert.IsTrue(File.Exists(capturePath), "冲突页面没有输出截图。");
            byte[] header = new byte[26];
            await using (FileStream stream = File.OpenRead(capturePath))
            {
                await stream.ReadExactlyAsync(header);
            }

            Assert.AreEqual((byte)'B', header[0]);
            Assert.AreEqual((byte)'M', header[1]);
            Assert.IsGreaterThanOrEqualTo(1024, BitConverter.ToInt32(header, 18));
            Assert.IsGreaterThanOrEqualTo(640, Math.Abs(BitConverter.ToInt32(header, 22)));
            Assert.IsEmpty(
                Directory.EnumerateDirectories(
                    Path.GetTempPath(),
                    $"Augit.VisualAudit.Conflict.{process.Id}.*"));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
        }
    }

    private static string FindAuditHostExecutable()
    {
        DirectoryInfo? configurationDirectory = new(AppContext.BaseDirectory);
        while (configurationDirectory is not null
            && !string.Equals(configurationDirectory.Parent?.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            configurationDirectory = configurationDirectory.Parent;
        }

        string configuration = configurationDirectory?.Name
            ?? throw new InvalidOperationException("无法确定当前测试配置。");
        string repositoryFile = FindRepositoryFile("Augit.slnx");
        string repositoryRoot = Path.GetDirectoryName(repositoryFile)!;
        string executablePath = Path.Combine(
            repositoryRoot,
            "tests",
            "Augit.App.VisualAuditHost",
            "bin",
            configuration,
            "net10.0-windows",
            "win-x64",
            "Augit.App.VisualAuditHost.exe");
        Assert.IsTrue(File.Exists(executablePath), executablePath);
        return executablePath;
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"未找到仓库文件：{Path.Combine(relativeParts)}");
    }
}
