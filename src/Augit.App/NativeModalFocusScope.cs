namespace Augit.App;

/// <summary>
/// 记录模态窗口打开前的焦点，并在窗口关闭后恢复到原来的工作区域。
/// </summary>
internal sealed class NativeModalFocusScope : IDisposable
{
    private readonly nint _owner;
    private readonly nint _initialFocus;
    private bool _restored;

    internal NativeModalFocusScope(nint owner)
    {
        _owner = owner;
        _initialFocus = NativeMethods.GetFocus();
    }

    /// <summary>
    /// 在模态窗口关闭后恢复焦点；如果原控件已经不存在，则退回到宿主窗口。
    /// </summary>
    internal void Restore()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        if (_initialFocus != 0
            && NativeMethods.IsWindow(_initialFocus)
            && (_initialFocus == _owner || NativeMethods.IsChild(_owner, _initialFocus)))
        {
            _ = NativeMethods.SetFocus(_initialFocus);
            return;
        }

        if (_owner != 0 && NativeMethods.IsWindow(_owner))
        {
            _ = NativeMethods.SetFocus(_owner);
        }
    }

    public void Dispose()
    {
        Restore();
        GC.SuppressFinalize(this);
    }
}
