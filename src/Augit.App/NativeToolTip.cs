using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed class NativeToolTip : IDisposable
{
    private readonly Dictionary<nint, nint> _textBuffers = [];
    private nint _handle;
    private bool _disposed;

    internal NativeToolTip(nint owner)
    {
        if (owner == 0)
        {
            return;
        }

        _handle = NativeMethods.CreateWindow(
            NativeMethods.WindowExtendedStyleTopMost,
            NativeMethods.ToolTipClass,
            string.Empty,
            NativeMethods.WindowStylePopup
                | NativeMethods.ToolTipStyleAlwaysTip
                | NativeMethods.ToolTipStyleNoPrefix,
            NativeMethods.UseDefault,
            NativeMethods.UseDefault,
            NativeMethods.UseDefault,
            NativeMethods.UseDefault,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            return;
        }

        _ = NativeMethods.SetWindowPosition(
            _handle,
            NativeMethods.WindowPositionTopMost,
            0,
            0,
            0,
            0,
            NativeMethods.SetWindowPositionNoMove
                | NativeMethods.SetWindowPositionNoSize
                | NativeMethods.SetWindowPositionNoActivate);
        _ = NativeMethods.SendMessage(
            _handle,
            NativeMethods.ToolTipSetMaximumWidth,
            0,
            unchecked((nint)NativeTheme.Scale(320)));
    }

    internal int CountForTest => _textBuffers.Count;

    internal bool IsCreatedForTest => _handle != 0;

    internal bool ContainsForTest(nint control)
    {
        return control != 0 && _textBuffers.ContainsKey(control);
    }

    internal void Add(nint control, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == 0
            || control == 0
            || string.IsNullOrWhiteSpace(text)
            || _textBuffers.ContainsKey(control))
        {
            return;
        }

        nint buffer = Marshal.StringToHGlobalUni(text);
        NativeMethods.ToolInfo info = new()
        {
            Size = unchecked((uint)(Marshal.SizeOf<NativeMethods.ToolInfo>() - nint.Size)),
            Flags = NativeMethods.ToolTipFlagIdIsWindow | NativeMethods.ToolTipFlagSubclass,
            Window = NativeMethods.GetParent(control),
            Identifier = unchecked((nuint)control),
            Text = buffer,
        };
        if (NativeMethods.SendMessage(_handle, NativeMethods.ToolTipAddTool, 0, ref info) == 0)
        {
            Marshal.FreeHGlobal(buffer);
            return;
        }

        _textBuffers.Add(control, buffer);
        _ = NativeAccessibility.SetName(control, text);
    }

    internal void Update(nint control, string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == 0
            || control == 0
            || string.IsNullOrWhiteSpace(text)
            || !_textBuffers.TryGetValue(control, out nint previousBuffer))
        {
            return;
        }

        nint buffer = Marshal.StringToHGlobalUni(text);
        NativeMethods.ToolInfo info = new()
        {
            Size = unchecked((uint)(Marshal.SizeOf<NativeMethods.ToolInfo>() - nint.Size)),
            Flags = NativeMethods.ToolTipFlagIdIsWindow | NativeMethods.ToolTipFlagSubclass,
            Window = NativeMethods.GetParent(control),
            Identifier = unchecked((nuint)control),
            Text = buffer,
        };
        // TTM_UPDATETIPTEXT 没有定义稳定的返回值，发送后直接替换托管的文本缓冲区。
        _ = NativeMethods.SendMessage(_handle, NativeMethods.ToolTipUpdateTool, 0, ref info);
        Marshal.FreeHGlobal(previousBuffer);
        _textBuffers[control] = buffer;
        _ = NativeAccessibility.SetName(control, text);
    }

    internal void ApplyAppearance(bool dark)
    {
        if (_handle != 0)
        {
            NativeTheme.ApplyToControl(_handle, dark);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != 0 && NativeMethods.IsWindow(_handle))
        {
            _ = NativeMethods.DestroyWindow(_handle);
        }
        _handle = 0;

        foreach (nint buffer in _textBuffers.Values)
        {
            Marshal.FreeHGlobal(buffer);
        }
        _textBuffers.Clear();
    }
}
