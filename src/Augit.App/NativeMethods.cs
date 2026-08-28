using System.Runtime.InteropServices;

namespace Augit.App;

internal static partial class NativeMethods
{
    internal const uint CommonControlsTreeView = 0x00000002;
    internal const uint CommonControlsBar = 0x00000004;
    internal const uint CommonControlsTab = 0x00000008;
    internal const uint CommonControlsStandard = 0x00004000;
    internal const uint CommonControlsLink = 0x00008000;
    internal const uint ClassRedrawOnHorizontalChange = 0x0002;
    internal const uint ClassRedrawOnVerticalChange = 0x0001;
    internal const uint WindowStyleOverlappedWindow = 0x00CF0000;
    internal const uint WindowStylePopup = 0x80000000;
    internal const uint WindowStyleCaption = 0x00C00000;
    internal const uint WindowStyleSystemMenu = 0x00080000;
    internal const uint WindowStyleThickFrame = 0x00040000;
    internal const uint WindowStyleChild = 0x40000000;
    internal const uint WindowStyleVisible = 0x10000000;
    internal const uint WindowStyleClipChildren = 0x02000000;
    internal const uint WindowStyleClipSiblings = 0x04000000;
    internal const uint WindowStyleTabStop = 0x00010000;
    internal const uint WindowStyleBorder = 0x00800000;
    internal const uint WindowStyleVerticalScroll = 0x00200000;
    internal const uint WindowStyleHorizontalScroll = 0x00100000;
    internal const uint ButtonPushButton = 0x00000000;
    internal const uint ButtonAutoCheckbox = 0x00000003;
    internal const uint StaticLeft = 0x00000000;
    internal const uint StaticCenter = 0x00000001;
    internal const uint StaticCenterImage = 0x00000200;
    internal const uint EditAutoHorizontalScroll = 0x00000080;
    internal const uint EditMultiline = 0x00000004;
    internal const uint EditAutoVerticalScroll = 0x00000040;
    internal const uint EditWantReturn = 0x00001000;
    internal const uint TreeViewHasButtons = 0x0001;
    internal const uint TreeViewHasLines = 0x0002;
    internal const uint TreeViewLinesAtRoot = 0x0004;
    internal const uint TreeViewShowSelectionAlways = 0x0020;
    internal const uint TreeViewDoubleBuffer = 0x0004;
    internal const uint TabControlFocusNever = 0x8000;
    internal const uint ListBoxNotify = 0x0001;
    internal const uint ListBoxNoIntegralHeight = 0x0100;
    internal const uint ComboBoxDropDownList = 0x0003;
    internal const int UseDefault = unchecked((int)0x80000000);
    internal const int ShowNormal = 1;
    internal const int ShowMaximized = 3;
    internal const int ShowHide = 0;
    internal const int ShowWithoutActivate = 4;
    internal const uint WindowMessageCreate = 0x0001;
    internal const uint WindowMessageSize = 0x0005;
    internal const uint WindowMessageSetFocus = 0x0007;
    internal const uint WindowMessageClose = 0x0010;
    internal const uint WindowMessageSettingChange = 0x001A;
    internal const uint WindowMessageDestroy = 0x0002;
    internal const uint WindowMessageCommand = 0x0111;
    internal const uint WindowMessageNotify = 0x004E;
    internal const uint WindowMessageContextMenu = 0x007B;
    internal const uint WindowMessageKeyDown = 0x0100;
    internal const uint WindowMessageApp = 0x8000;
    internal const uint WindowMessageAppDispatch = WindowMessageApp + 1;
    internal const uint WindowMessageAppActivate = WindowMessageApp + 2;
    internal const uint WindowMessageThemeChanged = 0x031A;
    internal const uint WindowMessageSetFont = 0x0030;
    internal const uint ButtonMessageGetCheck = 0x00F0;
    internal const uint ButtonMessageSetCheck = 0x00F1;
    internal const nuint ButtonUnchecked = 0;
    internal const nuint ButtonChecked = 1;
    internal const uint TreeViewFirst = 0x1100;
    internal const uint TreeViewInsertItem = TreeViewFirst + 50;
    internal const uint TreeViewDeleteItem = TreeViewFirst + 1;
    internal const uint TreeViewExpand = TreeViewFirst + 2;
    internal const uint TreeViewGetItemRectangle = TreeViewFirst + 4;
    internal const uint TreeViewGetNextItem = TreeViewFirst + 10;
    internal const uint TreeViewSelectItem = TreeViewFirst + 11;
    internal const uint TreeViewGetItem = TreeViewFirst + 62;
    internal const uint TreeViewSetExtendedStyle = TreeViewFirst + 44;
    internal const nuint TreeViewRoot = 0xFFFF0000;
    internal const nuint TreeViewLast = 0xFFFF0002;
    internal const nuint TreeViewCaret = 0x0009;
    internal const nuint TreeViewChild = 0x0004;
    internal const nuint TreeViewFirstVisible = 0x0005;
    internal const nuint TreeViewExpandItem = 0x0002;
    internal const uint TreeViewItemText = 0x0001;
    internal const uint TreeViewItemParameter = 0x0004;
    internal const int TreeViewNotificationFirst = -400;
    internal const int TreeViewNotificationSelectionChanged = TreeViewNotificationFirst - 51;
    internal const int TreeViewNotificationItemExpanding = TreeViewNotificationFirst - 54;
    internal const int TreeViewNotificationRightClick = -5;
    internal const uint TabControlFirst = 0x1300;
    internal const uint TabControlGetCurrentSelection = TabControlFirst + 11;
    internal const uint TabControlSetCurrentSelection = TabControlFirst + 12;
    internal const uint TabControlInsertItem = TabControlFirst + 62;
    internal const uint TabControlDeleteItem = TabControlFirst + 8;
    internal const uint TabControlAdjustRectangle = TabControlFirst + 40;
    internal const uint TabItemText = 0x0001;
    internal const int TabControlNotificationFirst = -550;
    internal const int TabControlNotificationSelectionChanged = TabControlNotificationFirst - 1;
    internal const uint StatusBarSetText = 0x0400 + 11;
    internal const uint ListBoxAddString = 0x0180;
    internal const uint ListBoxResetContent = 0x0184;
    internal const uint ListBoxGetCurrentSelection = 0x0188;
    internal const uint ListBoxSetCurrentSelection = 0x0186;
    internal const int ListBoxNotificationDoubleClick = 2;
    internal const int ListBoxNotificationSelectionChanged = 1;
    internal const uint ComboBoxAddString = 0x0143;
    internal const uint ComboBoxGetCurrentSelection = 0x0147;
    internal const uint ComboBoxSetCurrentSelection = 0x014E;
    internal const uint MenuString = 0x00000000;
    internal const uint MenuSeparator = 0x00000800;
    internal const uint TrackPopupReturnCommand = 0x0100;
    internal const uint TrackPopupRightButton = 0x0002;
    internal const uint FormatMessageFromSystem = 0x00001000;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const int ColorWindow = 5;
    internal const int ColorWindowText = 8;
    internal const int ColorButtonFace = 15;
    internal const uint MessageBoxIconError = 0x00000010;
    internal const uint MessageBoxIconInformation = 0x00000040;
    internal const uint MessageBoxIconWarning = 0x00000030;
    internal const uint MessageBoxOkCancel = 0x00000001;
    internal const uint MessageBoxYesNo = 0x00000004;
    internal const int DialogResultOk = 1;
    internal const int DialogResultYes = 6;
    internal const uint ClipboardUnicodeText = 13;
    internal const uint GlobalMemoryMoveable = 0x0002;
    internal const int VirtualKeyControl = 0x11;
    internal const int VirtualKeyShift = 0x10;
    internal const int VirtualKeyEscape = 0x1B;
    internal const int VirtualKeyF5 = 0x74;
    internal const uint GetAncestorRoot = 2;
    internal const int ErrorClassAlreadyExists = 1410;
    internal const int DefaultGuiFont = 17;
    internal static readonly nint ArrowCursor = (nint)32512;
    internal static readonly nint TreeViewInsertRoot = unchecked((nint)(-65536));
    internal static readonly nint TreeViewInsertLast = unchecked((nint)(-65534));

    internal const string ButtonClass = "BUTTON";
    internal const string EditClass = "EDIT";
    internal const string StaticClass = "STATIC";
    internal const string TreeViewClass = "SysTreeView32";
    internal const string TabControlClass = "SysTabControl32";
    internal const string StatusBarClass = "msctls_statusbar32";
    internal const string ListBoxClass = "LISTBOX";
    internal const string ComboBoxClass = "COMBOBOX";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint MessageId;
        internal nuint WordParameter;
        internal nint LongParameter;
        internal uint Time;
        internal Point Position;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        internal uint Length;
        internal uint Flags;
        internal uint ShowCommand;
        internal Point MinimumPosition;
        internal Point MaximumPosition;
        internal Rectangle NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommonControls
    {
        internal uint Size;
        internal uint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotificationHeader
    {
        internal nint WindowFrom;
        internal nuint IdFrom;
        internal int Code;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TreeViewItem
    {
        internal uint Mask;
        internal nint Item;
        internal uint State;
        internal uint StateMask;
        internal string? Text;
        internal int TextMaximum;
        internal int Image;
        internal int SelectedImage;
        internal int Children;
        internal nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TreeViewInsert
    {
        internal nint Parent;
        internal nint InsertAfter;
        internal TreeViewItem Item;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TreeViewNotification
    {
        internal NotificationHeader Header;
        internal uint Action;
        internal TreeViewNotificationItem OldItem;
        internal TreeViewNotificationItem NewItem;
        internal Point DragPoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TreeViewNotificationItem
    {
        internal uint Mask;
        internal nint Item;
        internal uint State;
        internal uint StateMask;
        internal nint Text;
        internal int TextMaximum;
        internal int Image;
        internal int SelectedImage;
        internal int Children;
        internal nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TabItem
    {
        internal uint Mask;
        internal uint State;
        internal uint StateMask;
        internal string? Text;
        internal int TextMaximum;
        internal int Image;
        internal nint Parameter;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindow(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("comctl32.dll", EntryPoint = "InitCommonControlsEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeCommonControls(ref CommonControls controls);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern nint DefaultWindowProcedure(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, string longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TreeViewInsert longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TreeViewItem longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TabItem longParameter);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRectangle(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "EnableWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    internal static extern nint SetFocus(nint window);

    [DllImport("user32.dll", EntryPoint = "GetFocus")]
    internal static extern nint GetFocus();

    [DllImport("user32.dll", EntryPoint = "GetAncestor")]
    internal static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetParent")]
    internal static extern nint GetParent(nint window);

    [DllImport("user32.dll", EntryPoint = "IsChild")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll", EntryPoint = "GetKeyState")]
    internal static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "IsZoomed")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRectangle(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)]
    internal static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, uint flags, nuint itemIdentifier, string? text);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
    internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPosition(out Point point);

    [DllImport("gdi32.dll", EntryPoint = "GetStockObject")]
    internal static extern nint GetStockObject(int objectIdentifier);

    [DllImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", EntryPoint = "CloseClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll", EntryPoint = "EmptyClipboard", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll", EntryPoint = "SetClipboardData", SetLastError = true)]
    internal static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalAlloc", SetLastError = true)]
    internal static extern nint GlobalAllocate(uint flags, nuint bytes);

    [DllImport("kernel32.dll", EntryPoint = "GlobalFree")]
    internal static extern nint GlobalFree(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
    internal static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadLibrary(string fileName);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint owner, string text, string caption, uint type);

    [DllImport("uxtheme.dll", EntryPoint = "SetWindowTheme", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    internal static extern int SetDwmWindowAttribute(nint window, int attribute, ref int value, int size);

    internal static int LowWord(nuint value) => unchecked((ushort)value);

    internal static int HighWord(nuint value) => unchecked((short)(value >> 16));

    internal static string GetWindowTextValue(nint window)
    {
        int length = GetWindowTextLength(window);
        char[] buffer = new char[length + 1];
        int actualLength = GetWindowText(window, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, actualLength));
    }
}
