using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal sealed class NativeAppearanceDialog
{
    private const string WindowClassName = "Augit.AppearanceDialog.Native";
    private const int CommandConfirm = 1;
    private const int CommandCancel = 2;
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, NativeAppearanceDialog> Instances = [];
    private static readonly NativeMethods.WindowProcedure Procedure = HandleWindowMessage;
    private static bool _classRegistered;
    private readonly nint _owner;
    private readonly ApplicationSettings _initialSettings;
    private nint _handle;
    private nint _themeCombo;
    private nint _textFontEdit;
    private nint _monospaceFontEdit;
    private nint _fontSizeEdit;
    private nint _gitExecutableEdit;
    private nint _terminalShellCombo;
    private nint _terminalCustomEdit;
    private bool _closed;
    private ApplicationSettings? _result;

    private NativeAppearanceDialog(nint owner, ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _owner = owner;
        _initialSettings = settings;
        EnsureWindowClass();
        int x = NativeMethods.UseDefault;
        int y = NativeMethods.UseDefault;
        if (NativeMethods.GetWindowRectangle(owner, out NativeMethods.Rectangle ownerRectangle))
        {
            x = ownerRectangle.Left + Math.Max(0, ((ownerRectangle.Right - ownerRectangle.Left) - 540) / 2);
            y = ownerRectangle.Top + Math.Max(0, ((ownerRectangle.Bottom - ownerRectangle.Top) - 530) / 2);
        }

        _handle = NativeMethods.CreateWindow(
            0,
            WindowClassName,
            UiText.Settings,
            NativeMethods.WindowStylePopup | NativeMethods.WindowStyleCaption | NativeMethods.WindowStyleSystemMenu,
            x,
            y,
            540,
            530,
            owner,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceWindowCreateFailed);
        }

        lock (InstancesGate)
        {
            Instances.Add(_handle, this);
        }

        CreateControls();
        NativeTheme.ApplyToWindow(_handle, NativeTheme.IsDark(settings.Theme));
    }

    internal static ApplicationSettings? Show(nint owner, ApplicationSettings settings)
    {
        NativeAppearanceDialog dialog = new(owner, settings);
        return dialog.Run();
    }

    private ApplicationSettings? Run()
    {
        _ = NativeMethods.EnableWindow(_owner, false);
        _ = NativeMethods.ShowWindow(_handle, NativeMethods.ShowNormal);
        _ = NativeMethods.UpdateWindow(_handle);
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
                throw new Win32Exception(error, UiText.AppearanceWindowClassRegisterFailed);
            }

            _classRegistered = true;
        }
    }

    private static nint HandleWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        NativeAppearanceDialog? instance;
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
                instance.Confirm();
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

    private void CreateControls()
    {
        CreateChild(NativeMethods.StaticClass, UiText.Theme, 0, NativeMethods.StaticLeft, 22, 22, 110, 24);
        _themeCombo = CreateChild(
            NativeMethods.ComboBoxClass,
            string.Empty,
            10,
            NativeMethods.ComboBoxDropDownList,
            142,
            18,
            280,
            120);
        foreach (string theme in new[] { UiText.FollowWindows, UiText.Light, UiText.Dark })
        {
            _ = NativeMethods.SendMessage(_themeCombo, NativeMethods.ComboBoxAddString, 0, theme);
        }

        int themeIndex = _initialSettings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? 1
            : _initialSettings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        _ = NativeMethods.SendMessage(
            _themeCombo,
            NativeMethods.ComboBoxSetCurrentSelection,
            unchecked((nuint)themeIndex),
            0);

        CreateChild(NativeMethods.StaticClass, UiText.InterfaceFont, 0, NativeMethods.StaticLeft, 22, 66, 110, 24);
        _textFontEdit = CreateChild(
            NativeMethods.EditClass,
            _initialSettings.TextFontFamily,
            11,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            142,
            62,
            280,
            26);
        CreateChild(NativeMethods.StaticClass, UiText.MonospaceFont, 0, NativeMethods.StaticLeft, 22, 108, 110, 24);
        _monospaceFontEdit = CreateChild(
            NativeMethods.EditClass,
            _initialSettings.MonospaceFontFamily,
            12,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            142,
            104,
            280,
            26);
        CreateChild(NativeMethods.StaticClass, UiText.FontSize, 0, NativeMethods.StaticLeft, 22, 150, 110, 24);
        _fontSizeEdit = CreateChild(
            NativeMethods.EditClass,
            _initialSettings.FontSize.ToString(CultureInfo.InvariantCulture),
            13,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            142,
            146,
            100,
            26);
        CreateChild(
            NativeMethods.StaticClass,
            UiText.AppearanceDescription,
            0,
            NativeMethods.StaticLeft,
            22,
            188,
            410,
            42);
        CreateChild(NativeMethods.StaticClass, UiText.GitExecutable, 0, NativeMethods.StaticLeft, 22, 238, 110, 24);
        _gitExecutableEdit = CreateChild(
            NativeMethods.EditClass,
            _initialSettings.GitExecutablePath ?? string.Empty,
            14,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            142,
            234,
            280,
            26);
        CreateChild(
            NativeMethods.StaticClass,
            UiText.GitExecutableDescription,
            0,
            NativeMethods.StaticLeft,
            22,
            270,
            480,
            38);
        CreateChild(NativeMethods.StaticClass, UiText.TerminalShell, 0, NativeMethods.StaticLeft, 22, 316, 110, 24);
        _terminalShellCombo = CreateChild(
            NativeMethods.ComboBoxClass,
            string.Empty,
            15,
            NativeMethods.ComboBoxDropDownList,
            142,
            312,
            340,
            160);
        foreach (string shell in new[]
        {
            UiText.WindowsPowerShell,
            UiText.PowerShell7,
            UiText.CommandPrompt,
            UiText.GitBash,
            UiText.Wsl,
            UiText.CustomTerminal,
        })
        {
            _ = NativeMethods.SendMessage(_terminalShellCombo, NativeMethods.ComboBoxAddString, 0, shell);
        }

        int shellIndex = TerminalShellIds.Normalize(_initialSettings.TerminalShell) switch
        {
            TerminalShellIds.PowerShell7 => 1,
            TerminalShellIds.CommandPrompt => 2,
            TerminalShellIds.GitBash => 3,
            TerminalShellIds.Wsl => 4,
            TerminalShellIds.Custom => 5,
            _ => 0,
        };
        _ = NativeMethods.SendMessage(
            _terminalShellCombo,
            NativeMethods.ComboBoxSetCurrentSelection,
            unchecked((nuint)shellIndex),
            0);
        CreateChild(NativeMethods.StaticClass, UiText.TerminalCustomCommand, 0, NativeMethods.StaticLeft, 22, 358, 110, 24);
        _terminalCustomEdit = CreateChild(
            NativeMethods.EditClass,
            _initialSettings.TerminalCustomCommand ?? string.Empty,
            16,
            NativeMethods.WindowStyleBorder | NativeMethods.EditAutoHorizontalScroll,
            142,
            354,
            340,
            26);
        CreateChild(
            NativeMethods.StaticClass,
            UiText.TerminalDescription,
            0,
            NativeMethods.StaticLeft,
            22,
            392,
            470,
            42);
        CreateChild(NativeMethods.ButtonClass, UiText.Confirm, CommandConfirm, NativeMethods.ButtonPushButton, 326, 452, 74, 28);
        CreateChild(NativeMethods.ButtonClass, UiText.Cancel, CommandCancel, NativeMethods.ButtonPushButton, 408, 452, 74, 28);
    }

    private nint CreateChild(
        string className,
        string text,
        int identifier,
        uint specificStyle,
        int x,
        int y,
        int width,
        int height)
    {
        nint child = NativeMethods.CreateWindow(
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
        if (child == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.AppearanceControlCreateFailed);
        }

        nint font = NativeMethods.GetStockObject(NativeMethods.DefaultGuiFont);
        _ = NativeMethods.SendMessage(child, NativeMethods.WindowMessageSetFont, unchecked((nuint)font), 1);
        NativeTheme.ApplyToControl(child, NativeTheme.IsDark(_initialSettings.Theme));
        return child;
    }

    private void Confirm()
    {
        string textFont = NativeMethods.GetWindowTextValue(_textFontEdit).Trim();
        string monospaceFont = NativeMethods.GetWindowTextValue(_monospaceFontEdit).Trim();
        string sizeText = NativeMethods.GetWindowTextValue(_fontSizeEdit);
        if (textFont.Length == 0
            || monospaceFont.Length == 0
            || !double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double fontSize)
            || fontSize is < 9 or > 40)
        {
            _ = NativeMethods.MessageBox(
                _handle,
                UiText.AppearanceFontSizeError,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
            return;
        }

        int themeIndex = checked((int)NativeMethods.SendMessage(
            _themeCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        string theme = themeIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
        int shellIndex = checked((int)NativeMethods.SendMessage(
            _terminalShellCombo,
            NativeMethods.ComboBoxGetCurrentSelection,
            0,
            0));
        string terminalShell = shellIndex switch
        {
            1 => TerminalShellIds.PowerShell7,
            2 => TerminalShellIds.CommandPrompt,
            3 => TerminalShellIds.GitBash,
            4 => TerminalShellIds.Wsl,
            5 => TerminalShellIds.Custom,
            _ => TerminalShellIds.WindowsPowerShell,
        };
        string? customCommand = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_terminalCustomEdit));
        if (terminalShell == TerminalShellIds.Custom && customCommand is null)
        {
            _ = NativeMethods.MessageBox(
                _handle,
                UiText.TerminalCustomCommandRequired,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
            return;
        }

        _result = _initialSettings with
        {
            Theme = theme,
            TextFontFamily = textFont,
            MonospaceFontFamily = monospaceFont,
            FontSize = fontSize,
            GitExecutablePath = NullIfWhiteSpace(NativeMethods.GetWindowTextValue(_gitExecutableEdit)),
            TerminalShell = terminalShell,
            TerminalCustomCommand = customCommand,
        };
        Close();
    }

    private static string? NullIfWhiteSpace(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
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
