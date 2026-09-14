using System.Diagnostics;
using System.Text.Json;

namespace Augit.App.VisualAuditHost;

internal static partial class Program
{
    private static Action? _afterTerminalStartupClose;

    private static async Task VerifyTerminalStartupAsync(MainWindow window, string scenario, string resultPath)
    {
        string[] parts = scenario.Split('-');
        string action = parts[0], stage = parts[1];
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.TerminalStartupCheckpointForTest = current =>
        {
            if (current != stage) return Task.CompletedTask;
            reached.TrySetResult();
            return resume.Task;
        };
        Task<bool>? opening = null;
        Task<bool>? reopened = null;
        bool closingWindow = false;
        string? error = null;
        HashSet<int> shellPids = [], browserPids = [];
        long shellReadyMilliseconds = 0;
        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            opening = window.OpenTerminalForTestAsync();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            RecordProcesses();
            if (action is "appclose" or "dispose")
            {
                nint handle = window.Handle;
                _afterTerminalStartupClose = () =>
                {
                    try
                    {
                        Require(opening.IsCompletedSuccessfully && !opening.Result,
                            "消息循环只能在已取消启动和晚到资源完成回收后结束。");
                        Require(!NativeMethods.IsWindow(handle), "主窗口必须销毁。");
                    }
                    catch (Exception exception) { error = exception.ToString(); }
                    File.WriteAllText(resultPath, SerializeResult());
                };
                if (action == "dispose") window.Dispose();
                else window.Close();
                closingWindow = true;
                Require(NativeMethods.IsWindow(handle) && !NativeMethods.IsWindowVisible(handle),
                    "窗口应先隐藏并继续处理尚未完成的 WebView2 创建回调。");
                resume.TrySetResult();
                return;
            }
            switch (action)
            {
                case "loading":
                    Require(window.TerminalCreatedForTest && window.TerminalPanelVisibleForTest,
                        "创建浏览器前必须显示终端标题与可用的隐藏、关闭入口。");
                    Require(stage == "panel" ? !window.TerminalRunningForTest : window.TerminalRunningForTest,
                        stage == "panel" ? "加载壳不应提前启动 Shell。" : "WebView2 导航期间应已并行启动 Shell。");
                    break;
                case "hide":
                    window.HideTerminalForTest();
                    break;
                case "history":
                    window.ShowHistoryForTest();
                    Require(window.HistoryPanelVisibleForTest, "必须先切换到历史。");
                    break;
                case "focus":
                    _ = NativeMethods.SetFocus(window.FileTreeHandleForTest);
                    break;
                case "close":
                    Require(window.CloseTerminalForTest(), "在途启动应允许直接关闭。");
                    break;
                case "workspace":
                    string nextWorkspace = Path.Combine(Path.GetDirectoryName(window.WorkspaceRoot!)!, "next-workspace");
                    Directory.CreateDirectory(nextWorkspace);
                    Require(await window.OpenWorkspaceAsync(nextWorkspace), "无法切换隔离工作区。");
                    break;
                case "duplicate":
                    reopened = window.OpenTerminalForTestAsync();
                    Require(ReferenceEquals(opening, reopened), "重复打开必须复用同一个启动请求。");
                    break;
                case "reopen":
                    Require(window.CloseTerminalForTest(), "在途启动应允许直接关闭。");
                    window.TerminalStartupCheckpointForTest = null;
                    reopened = window.OpenTerminalForTestAsync();
                    Require(await reopened.WaitAsync(TimeSpan.FromSeconds(15)), "新会话应能独立完成。");
                    RecordProcesses();
                    break;
                case "restore":
                    window.ShowHistoryForTest();
                    window.ShowTerminalForTest();
                    break;
                case "failure":
                    resume.TrySetException(new IOException("隔离测试：终端启动失败"));
                    break;
                default:
                    throw new InvalidOperationException($"未知终端启动场景：{action}");
            }

            nint focus = NativeMethods.GetFocus();
            resume.TrySetResult();
            bool opened = await opening.WaitAsync(TimeSpan.FromSeconds(15));
            shellReadyMilliseconds = elapsed.ElapsedMilliseconds;
            RecordProcesses();
            if (action is "close" or "workspace" or "failure")
            {
                Require(!opened && !window.TerminalCreatedForTest, "已关闭或旧工作区的启动不得复活终端。");
            }
            else if (action == "reopen")
            {
                Require(!opened && window.TerminalRunningForTest, "旧启动完成不得覆盖新会话。");
                Require(shellPids.Count <= 2
                    && shellPids.All(pid => pid == window.TerminalShellProcessIdForTest || !IsRunning(pid))
                    && browserPids.Contains(window.TerminalBrowserProcessIdForTest),
                    "重开后只能保留新会话的一个 Shell。");
            }
            else
            {
                Require(opened && window.TerminalRunningForTest, "终端应只完成原会话。");
                if (action is "hide" or "history")
                    Require(!window.TerminalPanelVisibleForTest, "完成启动不能覆盖后来的隐藏或历史选择。");
                if (action == "history") Require(window.HistoryPanelVisibleForTest, "完成启动不能隐藏 Git 历史。");
                if (action is "hide" or "history" or "focus")
                    Require(focus == NativeMethods.GetFocus(), "完成启动不能抢走后续选择的焦点。");
                if (action == "restore")
                {
                    window.CloseTerminalForTest();
                    Require(window.HistoryPanelVisibleForTest, "关闭终端应恢复此前的 Git 历史。");
                }
            }
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally
        {
            resume.TrySetResult();
            if (!closingWindow)
            {
                try
                {
                    if (opening is not null) await opening.WaitAsync(TimeSpan.FromSeconds(20));
                    if (reopened is not null) await reopened.WaitAsync(TimeSpan.FromSeconds(20));
                    RecordProcesses();
                    window.CloseTerminalForTest();
                    await File.WriteAllTextAsync(resultPath, SerializeResult());
                }
                finally { window.Close(); }
            }
        }

        string SerializeResult() => JsonSerializer.Serialize(new
        {
            Error = error,
            Scenario = scenario,
            ShellReadyMilliseconds = shellReadyMilliseconds,
            ShellPids = shellPids,
            BrowserPids = browserPids,
        });

        void RecordProcesses()
        {
            if (window.TerminalShellProcessIdForTest > 0) shellPids.Add(window.TerminalShellProcessIdForTest);
            if (window.TerminalBrowserProcessIdForTest > 0) browserPids.Add(window.TerminalBrowserProcessIdForTest);
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static bool IsRunning(int pid)
        {
            try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }
}
