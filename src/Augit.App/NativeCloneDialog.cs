using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Git;
using Augit.Infrastructure.Git;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeCloneDialog : IDisposable
{
    private const string WindowClassName = "Augit.CloneDialog.Native";
    private const int CommandBrowse = 1;
    private const int CommandClone = 2;
    private const int CommandCancel = 3;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeCloneDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly GitRepositoryService _repositoryService;
    private readonly ApplicationSettings _settings;
    private CancellationTokenSource? _operationCancellation;
    private nint _handle;
    private nint _sourceEdit;
    private nint _destinationEdit;
    private nint _browseButton;
    private nint _cloneButton;
    private nint _cancelButton;
    private nint _noticeLabel;
    private bool _operationRunning;
    private bool _closed;
    private string? _result;

    private NativeCloneDialog(nint owner, GitRuntimeInfo runtime, ApplicationSettings settings)
    {
        _owner = owner;
        _repositoryService = new(runtime);
        _settings = settings;
        EnsureWindowClass();
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 600) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 250) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.Clone,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu,
            x,
            y,
            600,
            250,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        ApplyAppearance();
    }

    internal static string? Show(nint owner, GitRuntimeInfo runtime, ApplicationSettings settings)
    {
        using NativeCloneDialog dialog = new(owner, runtime, settings);
        return dialog.Run();
    }

    public void Dispose()
    {
        Close(force: true);
        GC.SuppressFinalize(this);
    }

    private string? Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
        _ = NativeMethods.SetFocus(_sourceEdit);
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
                throw new Win32Exception(error, UiText.CloneWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeCloneDialog? instance;
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
            instance.HandleCommand(NativeMethods.LowWord(wordParameter));
            return 0;
        }

        if (message == NativeMethods.WindowMessageClose)
        {
            instance.CancelOrClose();
            return 0;
        }

        return NativeMethods.DefaultWindowProcedure(window, message, wordParameter, longParameter);
    }

    private void CreateControls()
    {
        CreateControl(NativeMethods.StaticClass, UiText.CloneSource, 0, NativeMethods.StaticLeft, 18, 22, 100, 24);
        _sourceEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            10,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            126,
            18,
            432,
            26);
        CreateControl(NativeMethods.StaticClass, UiText.CloneDestination, 0, NativeMethods.StaticLeft, 18, 66, 100, 24);
        _destinationEdit = CreateControl(
            NativeMethods.EditClass,
            string.Empty,
            11,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            126,
            62,
            334,
            26);
        _browseButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Browse,
            CommandBrowse,
            NativeMethods.ButtonPushButton,
            468,
            62,
            90,
            28);
        _noticeLabel = CreateControl(NativeMethods.StaticClass, string.Empty, 0, NativeMethods.StaticLeft, 18, 108, 540, 42);
        _cloneButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Clone,
            CommandClone,
            NativeMethods.ButtonPushButton,
            386,
            164,
            80,
            28);
        _cancelButton = CreateControl(
            NativeMethods.ButtonClass,
            UiText.Cancel,
            CommandCancel,
            NativeMethods.ButtonPushButton,
            474,
            164,
            80,
            28);
    }

    private nint CreateControl(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height)
    {
        nint control = NativeMethods.CreateWindow(
            0,
            className,
            text,
            NativeMethods.WindowStyleChild
                | NativeMethods.WindowStyleVisible
                | NativeMethods.WindowStyleTabStop
                | specificStyle,
            x,
            y,
            width,
            height,
            _handle,
            identifier,
            NativeMethods.GetModuleHandle(null),
            0);
        if (control == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.CloneControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(control, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        NativeTheme.ApplyToControl(control, NativeTheme.IsDark(_settings.Theme));
        return control;
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case CommandBrowse:
                BrowseDestination();
                break;
            case CommandClone:
                _ = CloneAsync();
                break;
            case CommandCancel:
                CancelOrClose();
                break;
        }
    }

    private void BrowseDestination()
    {
        if (_operationRunning)
        {
            return;
        }

        string? path = NativeFolderDialog.SelectFolder(_handle);
        if (path is not null)
        {
            _ = NativeMethods.SetWindowText(_destinationEdit, path);
        }
    }

    private async Task CloneAsync()
    {
        if (_operationRunning)
        {
            return;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new();
        _operationRunning = true;
        _ = NativeMethods.EnableWindow(_sourceEdit, false);
        _ = NativeMethods.EnableWindow(_destinationEdit, false);
        _ = NativeMethods.EnableWindow(_browseButton, false);
        _ = NativeMethods.EnableWindow(_cloneButton, false);
        _ = NativeMethods.SetWindowText(_cancelButton, UiText.CancelOperation);
        _ = NativeMethods.SetWindowText(_noticeLabel, UiText.Cloning);
        try
        {
            GitRepositoryOperationResult result = await _repositoryService.CloneAsync(
                NativeMethods.GetWindowTextValue(_sourceEdit).Trim(),
                NativeMethods.GetWindowTextValue(_destinationEdit).Trim(),
                _operationCancellation.Token);
            if (_closed)
            {
                return;
            }

            if (result.IsSuccess && result.Repository is not null)
            {
                _result = result.Repository.WorkspacePath;
                _ = NativeMethods.SetWindowText(_noticeLabel, UiText.CloneCompleted);
                Close(force: true);
                return;
            }

            string error = result.ErrorMessage ?? UiText.CloneFailed;
            _ = NativeMethods.SetWindowText(_noticeLabel, error);
            _ = NativeMethods.MessageBox(_handle, error, UiText.AppName, NativeMethods.MessageBoxIconWarning);
        }
        finally
        {
            _operationRunning = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            if (!_closed)
            {
                _ = NativeMethods.EnableWindow(_sourceEdit, true);
                _ = NativeMethods.EnableWindow(_destinationEdit, true);
                _ = NativeMethods.EnableWindow(_browseButton, true);
                _ = NativeMethods.EnableWindow(_cloneButton, true);
                _ = NativeMethods.SetWindowText(_cancelButton, UiText.Cancel);
            }
        }
    }

    private void CancelOrClose()
    {
        if (_operationRunning)
        {
            _operationCancellation?.Cancel();
        }
        else
        {
            Close(force: true);
        }
    }

    private void ApplyAppearance()
    {
        bool dark = NativeTheme.IsDark(_settings.Theme);
        NativeTheme.ApplyToWindow(_handle, dark);
        foreach (nint control in new[]
        {
            _sourceEdit,
            _destinationEdit,
            _browseButton,
            _cloneButton,
            _cancelButton,
            _noticeLabel,
        })
        {
            NativeTheme.ApplyToControl(control, dark);
        }
    }

    private void Close(bool force)
    {
        if (_closed || (!force && _operationRunning))
        {
            return;
        }

        _closed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
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
