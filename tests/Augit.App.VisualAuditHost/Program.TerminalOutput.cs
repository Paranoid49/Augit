using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Augit.Infrastructure.Terminal;

namespace Augit.App.VisualAuditHost;

internal static partial class Program
{
    private static async Task VerifyTerminalOutputAsync(MainWindow window, string resultPath)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        int shellPid = 0, browserPid = 0;
        long opened = 0, prompt = 0;
        string[] initial = [], result = [];
        string? error = null;
        try
        {
            if (!await window.OpenTerminalForTestAsync()) throw new InvalidOperationException("无法打开终端。");
            opened = elapsed.ElapsedMilliseconds;
            NativeTerminalPanel panel = TerminalField<NativeTerminalPanel>(window, "_terminalPanel");
            TerminalWebViewHost host = TerminalField<TerminalWebViewHost>(panel, "_webView");
            object page = TerminalField<object>(host, "_webView");
            shellPid = panel.ShellProcessId;
            browserPid = panel.BrowserProcessId;
            initial = await WaitForTerminalRowsAsync(page,
                rows => string.Concat(rows).Contains(window.WorkspaceRoot!, StringComparison.OrdinalIgnoreCase));
            prompt = elapsed.ElapsedMilliseconds;
            if (!string.Concat(initial).Contains(window.WorkspaceRoot!, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"打开后 8 秒内没有初始提示符，网页初始尺寸为 {host.InitialSize}。");

            string marker = $"AUGIT_OUTPUT_{Guid.NewGuid():N}";
            // 经真实页面消息桥输入，断言独立输出行，不能把输入回显当成执行结果。
            await ExecuteTerminalScriptAsync(page,
                $"window.chrome.webview.postMessage({{type:'input',data:{JsonSerializer.Serialize($"echo {marker}\r")}}})");
            result = await WaitForTerminalRowsAsync(page, rows => rows.Any(row => row.Trim() == marker));
            if (!result.Any(row => row.Trim() == marker)) throw new InvalidOperationException("命令没有生成独立输出行。");

            ConPtyTerminalSession session = TerminalField<ConPtyTerminalSession>(panel, "_session");
            string hiddenMarker = $"AUGIT_HIDDEN_{Guid.NewGuid():N}";
            string hiddenTail = string.Empty;
            TaskCompletionSource hiddenOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<string> observe = (_, text) =>
            {
                hiddenTail += text;
                if (hiddenTail.Contains(hiddenMarker, StringComparison.Ordinal)) hiddenOutput.TrySetResult();
                if (hiddenTail.Length > 512) hiddenTail = hiddenTail[^512..];
            };
            session.OutputReceived += observe;
            try
            {
                window.HideTerminalForTest();
                await session.WriteAsync($"echo {hiddenMarker}\r");
                await hiddenOutput.Task.WaitAsync(TimeSpan.FromSeconds(8));
            }
            finally
            {
                session.OutputReceived -= observe;
                window.ShowTerminalForTest();
            }
            result = await WaitForTerminalRowsAsync(page, rows => rows.Any(row => row.Trim() == hiddenMarker));
            if (!result.Any(row => row.Trim() == hiddenMarker)) throw new InvalidOperationException("恢复工具窗口后丢失隐藏期间的输出。");
            window.ResizeBottomPanelForTest(270);
            string finalMarker = $"AUGIT_RESIZED_{Guid.NewGuid():N}";
            await session.WriteAsync($"echo {finalMarker}\r");
            result = await WaitForTerminalRowsAsync(page, rows => rows.Any(row => row.Trim() == finalMarker));
            if (!result.Any(row => row.Trim() == finalMarker)) throw new InvalidOperationException("调整终端高度后没有收到输出。");
            if (panel.ShellProcessId != shellPid || panel.BrowserProcessId != browserPid)
                throw new InvalidOperationException("隐藏恢复或调整大小不应重新创建会话。");
            await CaptureTerminalPreviewAsync(page, Path.ChangeExtension(resultPath, ".terminal.png"));
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            try
            {
                window.CloseTerminalForTest();
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
                {
                    Error = error,
                    OpenedMilliseconds = opened,
                    PromptMilliseconds = prompt,
                    Initial = initial,
                    Output = result,
                    ShellPid = shellPid,
                    BrowserPid = browserPid,
                }));
            }
            finally { window.Close(); }
        }
    }

    private static async Task<string[]> WaitForTerminalRowsAsync(object page, Func<string[], bool> expected)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        string[] rows;
        do
        {
            rows = JsonSerializer.Deserialize<string[]>(await ExecuteTerminalScriptAsync(page,
                "Array.from(document.querySelectorAll('.xterm-rows > div'), row => row.textContent)")) ?? [];
            if (expected(rows)) return rows;
            await Task.Delay(25);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(8));
        return rows;
    }

    // 审计仅通过 WebView2 的公开脚本执行接口观察真实 DOM，不读取 xterm 私有状态。
    private static Task<string> ExecuteTerminalScriptAsync(object page, string script) =>
        (Task<string>)page.GetType().GetMethod("ExecuteScriptAsync", [typeof(string)])!.Invoke(page, [script])!;

    private static T TerminalField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private static async Task CaptureTerminalPreviewAsync(object page, string path)
    {
        MethodInfo capture = page.GetType().GetMethod("CapturePreviewAsync")!;
        object png = Enum.Parse(capture.GetParameters()[0].ParameterType, "Png");
        await using FileStream stream = File.Create(path);
        await (Task)capture.Invoke(page, [png, stream])!;
    }
}
