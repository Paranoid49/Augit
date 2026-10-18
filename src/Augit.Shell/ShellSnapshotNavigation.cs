namespace Augit.Shell;

/// <summary>导航等待可由窗口线程取消，父窗销毁后不再依赖迟到的导航事件。</summary>
internal sealed class ShellSnapshotNavigation
{
    private readonly TaskCompletionSource<bool> _completion = new();
    internal Task<bool> Ready => _completion.Task;
    internal void Complete(bool success) => _completion.TrySetResult(success);
    internal void Cancel() => _completion.TrySetResult(false);
}
