using System.Reflection;

namespace Augit.App.Tests;

public sealed partial class NativeTitleBarInteractionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task 终端旧会话退出回调不覆盖后续状态(bool dispose) => RunAsync(96, false, 13, window =>
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        TerminalCallbackQueue callbacks = new();
        List<string> status = [];
        NativeTerminalPanel panel;
        try
        {
            SynchronizationContext.SetSynchronizationContext(callbacks);
            panel = new NativeTerminalPanel(window.Handle, 901, 902, 903, status.Add);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        using (panel)
        {
            typeof(NativeTerminalPanel).GetMethod("OnExited", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(panel, [dispose ? null : new object(), 7]);
            if (dispose) panel.Dispose();
            callbacks.Drain();
            Assert.IsEmpty(status, "旧来源或已释放面板的队列回调不能写状态栏。");
            Assert.AreEqual(0, panel.BrowserProcessId);
            Assert.AreEqual(0, panel.ShellProcessId);
        }
    });

    private sealed class TerminalCallbackQueue : SynchronizationContext
    {
        private readonly Queue<Action> _callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => _callbacks.Enqueue(() => callback(state));
        internal void Drain()
        {
            while (_callbacks.TryDequeue(out Action? callback)) callback();
        }
    }
}
