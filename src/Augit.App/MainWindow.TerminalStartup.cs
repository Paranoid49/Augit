namespace Augit.App;

internal sealed partial class MainWindow
{
    private readonly HashSet<Task<bool>> _terminalStarts = [];
    private Task<bool>? _terminalOpeningTask;
    private bool _terminalClosePending;
    private bool _disposeAfterTerminalClose;
    private bool _restoreHistoryAfterTerminal;

    private async Task ObserveTerminalStartAsync(Task<bool> opening)
    {
        try { await opening; }
        finally
        {
            _terminalStarts.Remove(opening);
            if (ReferenceEquals(_terminalOpeningTask, opening)) _terminalOpeningTask = null;
        }
    }

    private bool DeferCloseForTerminalStartup()
    {
        if (_terminalClosePending) return true;
        Task[] pending = _terminalStarts.Where(task => !task.IsCompleted).Cast<Task>().ToArray();
        if (pending.Length == 0) return false;
        _terminalClosePending = true;
        CloseTerminal(requireConfirmation: false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowHide);
        _ = CompleteCloseAfterTerminalStartupAsync(pending);
        return true;
    }

    private async Task CompleteCloseAfterTerminalStartupAsync(Task[] pending)
    {
        // 保留原 STA 消息循环接收 WebView2 创建回调；不另建线程，不同步阻塞或丢弃未完成的 COM 调用。
        try { await Task.WhenAll(pending); }
        finally
        {
            _terminalClosePending = false;
            if (_disposeAfterTerminalClose) Dispose();
            else Close();
        }
    }
}
