using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed class NativeTextPrompt
{
    private const string WindowClassName = "Augit.TextPrompt.Native";
    private const int CommandConfirm = 1;
    private const int CommandCancel = 2;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeTextPrompt> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private nint _handle;
    private nint _edit;
    private bool _closed;
    private string? _result;

    private NativeTextPrompt(nint owner, string title, string label)
    {
        _owner = owner;
        EnsureWindowClass();
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 380) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 150) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            title,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu,
            x,
            y,
            380,
            150,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InputWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateChild(NativeMethods.StaticClass, label, NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible, 0, 16, 16, 80, 24);
        _edit = CreateChild(
            NativeMethods.EditClass,
            string.Empty,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | NativeMethods.WindowStyleBorder
                | NativeMethods.EditAutoHorizontalScroll,
            3,
            92,
            14,
            260,
            26);
        CreateChild(
            NativeMethods.ButtonClass,
            UiText.Confirm,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop,
            CommandConfirm,
            196,
            64,
            74,
            28);
        CreateChild(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.WindowStyleTabStop,
            CommandCancel,
            278,
            64,
            74,
            28);
    }

    internal static string? Show(nint owner, string title, string label)
    {
        NativeTextPrompt prompt = new(owner, title, label);
        return prompt.Run();
    }

    private string? Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_edit);
        try
        {
            while (!_closed && NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            _ = NativeMethods.EnableWindow(_owner, true);
            _ = NativeMethods.SetForegroundWindow(_owner);
            Close();
        }

        return _result;
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            NativeMethods.WindowClass windowClass = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(0, NativeMethods.ArrowCursor),
                Background = NativeMethods.GetSystemColorBrush(NativeMethods.ColorButtonFace),
                ClassName = WindowClassName,
            };
            ushort atom = NativeMethods.RegisterClass(ref windowClass);
            int error = Marshal.GetLastWin32Error();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, UiText.InputWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeTextPrompt? instance;
        lock (InstancesGate)
        {
            Instances.TryGetValue(window, out instance);
        }

        if (instance is null)
        {
            return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
        }

        if (message == NativeMethods.WindowMessageCommand)
        {
            int command = NativeMethods.LowWord(wordParameter);
            if (command == CommandConfirm)
            {
                instance._result = NativeMethods.GetWindowTextValue(instance._edit);
                instance.Close();
                return 0;
            }

            if (command == CommandCancel)
            {
                instance.Close();
                return 0;
            }
        }

        if (message == NativeMethods.WindowMessageClose)
        {
            instance.Close();
            return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private nint CreateChild(string className, string text, uint style, int identifier, int x, int y, int width, int height)
    {
        nint child = NativeMethods.CreateWindow(
            0,
            className,
            text,
            style,
            x,
            y,
            width,
            height,
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (child == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.InputControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(child, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        return child;
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        nint handle = _handle;
        _handle = 0;
        lock (InstancesGate)
        {
            Instances.Remove(handle);
        }

        if (handle != 0 && NativeMethods.IsWindow(handle))
        {
            _ = NativeMethods.DestroyWindow(handle);
        }
    }
}
