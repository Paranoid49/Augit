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
    internal const uint ClassDropShadow = 0x00020000;
    internal const uint WindowStyleOverlappedWindow = 0x00CF0000;
    internal const uint WindowStylePopup = 0x80000000;
    internal const uint WindowStyleCaption = 0x00C00000;
    internal const uint WindowStyleSystemMenu = 0x00080000;
    internal const uint WindowStyleThickFrame = 0x00040000;
    internal const uint WindowStyleMinimizeBox = 0x00020000;
    internal const uint WindowStyleMaximizeBox = 0x00010000;
    internal const uint WindowStyleChild = 0x40000000;
    internal const uint WindowStyleVisible = 0x10000000;
    internal const uint WindowStyleClipChildren = 0x02000000;
    internal const uint WindowStyleClipSiblings = 0x04000000;
    internal const uint WindowStyleTabStop = 0x00010000;
    internal const uint WindowStyleBorder = 0x00800000;
    internal const uint WindowStyleVerticalScroll = 0x00200000;
    internal const uint WindowStyleHorizontalScroll = 0x00100000;
    internal const uint ButtonPushButton = 0x00000000;
    internal const uint ButtonOwnerDraw = 0x0000000B;
    internal const uint ButtonFlat = 0x00008000;
    internal const uint ButtonAutoCheckbox = 0x00000003;
    internal const uint StaticLeft = 0x00000000;
    internal const uint StaticEndEllipsis = 0x00004000;
    internal const uint StaticCenter = 0x00000001;
    internal const uint StaticRight = 0x00000002;
    internal const uint StaticCenterImage = 0x00000200;
    internal const uint StaticOwnerDraw = 0x0000000D;
    internal const uint StaticNotify = 0x00000100;
    internal const uint EditAutoHorizontalScroll = 0x00000080;
    internal const uint EditNumber = 0x00002000;
    internal const uint EditMultiline = 0x00000004;
    internal const uint EditReadOnly = 0x00000800;
    internal const uint EditAutoVerticalScroll = 0x00000040;
    internal const uint EditWantReturn = 0x00001000;
    internal const uint TreeViewHasButtons = 0x0001;
    internal const uint TreeViewHasLines = 0x0002;
    internal const uint TreeViewLinesAtRoot = 0x0004;
    internal const uint TreeViewShowSelectionAlways = 0x0020;
    internal const uint TreeViewFullRowSelect = 0x1000;
    internal const uint TreeViewNoHorizontalScroll = 0x8000;
    internal const uint TreeViewDoubleBuffer = 0x0004;
    internal const uint TabControlFocusNever = 0x8000;
    internal const uint TabControlOwnerDrawFixed = 0x2000;
    internal const uint TabControlFixedWidth = 0x0400;
    internal const uint ListBoxNotify = 0x0001;
    internal const uint ListBoxGetCount = 0x018B;
    internal const uint ListBoxOwnerDrawFixed = 0x0010;
    internal const uint ListBoxHasStrings = 0x0040;
    internal const uint ListBoxNoIntegralHeight = 0x0100;
    internal const uint ComboBoxDropDownList = 0x0003;
    internal const uint ComboBoxOwnerDrawFixed = 0x0010;
    internal const uint ComboBoxHasStrings = 0x0200;
    internal const uint ComboBoxSetItemHeight = 0x0153;
    internal const int UseDefault = unchecked((int)0x80000000);
    internal const int ShowNormal = 1;
    internal const int ShowMinimized = 2;
    internal const int ShowMaximized = 3;
    internal const int ShowRestore = 9;
    internal const int ShowHide = 0;
    internal const int ShowWithoutActivate = 4;
    internal const int WindowActivationInactive = 0;
    internal const uint WindowMessageCreate = 0x0001;
    internal const uint WindowMessageNull = 0x0000;
    internal const uint WindowMessageActivate = 0x0006;
    internal const uint WindowMessageMove = 0x0003;
    internal const uint WindowMessageSize = 0x0005;
    internal const uint WindowMessageSetFocus = 0x0007;
    internal const uint WindowMessageKillFocus = 0x0008;
    internal const uint WindowMessageEnable = 0x000A;
    internal const uint WindowMessageClose = 0x0010;
    internal const uint WindowMessageShowWindow = 0x0018;
    internal const uint WindowMessageCancelMode = 0x001F;
    internal const uint WindowMessageSetCursor = 0x0020;
    internal const uint WindowMessageSettingChange = 0x001A;
    internal const uint WindowMessageDestroy = 0x0002;
    internal const uint WindowMessageCommand = 0x0111;
    internal const uint WindowMessageVerticalScroll = 0x0115;
    internal const uint WindowMessageHorizontalScroll = 0x0114;
    internal const uint WindowMessageDrawItem = 0x002B;
    internal const uint WindowMessageNotify = 0x004E;
    internal const uint WindowMessageGetMinimumMaximumInfo = 0x0024;
    internal const uint WindowMessageNonClientCalculateSize = 0x0083;
    internal const uint WindowMessageNonClientDestroy = 0x0082;
    internal const uint WindowMessageNonClientPaint = 0x0085;
    internal const uint WindowMessageNonClientActivate = 0x0086;
    internal const uint WindowMessageNonClientHitTest = 0x0084;
    internal const uint WindowMessageContextMenu = 0x007B;
    internal const uint WindowMessageKeyDown = 0x0100;
    internal const uint WindowMessageKeyUp = 0x0101;
    internal const uint WindowMessageLeftButtonDown = 0x0201;
    internal const uint WindowMessageLeftButtonUp = 0x0202;
    internal const uint WindowMessageLeftButtonDoubleClick = 0x0203;
    internal const uint WindowMessageMouseMove = 0x0200;
    internal const uint WindowMessageMiddleButtonUp = 0x0208;
    internal const uint WindowMessageMouseWheel = 0x020A;
    internal const uint WindowMessageMouseHorizontalWheel = 0x020E;
    internal const uint WindowMessageCaptureChanged = 0x0215;
    internal const uint WindowMessageMouseLeave = 0x02A3;
    internal const uint WindowMessageApp = 0x8000;
    internal const uint WindowMessageAppDispatch = WindowMessageApp + 1;
    internal const uint WindowMessageAppActivate = WindowMessageApp + 2;
    internal const uint WindowMessageThemeChanged = 0x031A;
    internal const uint WindowMessageDpiChanged = 0x02E0;
    internal const uint WindowMessageSetFont = 0x0030;
    internal const uint WindowMessageSetRedraw = 0x000B;
    internal const uint EditSetMargins = 0x00D3;
    internal const uint EditSetCueBanner = 0x1501;
    internal const nuint EditMarginLeftRight = 0x0003;
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
    internal const uint TreeViewSetImageList = TreeViewFirst + 9;
    internal const uint TreeViewSetIndent = TreeViewFirst + 7;
    internal const uint TreeViewSetItemHeight = TreeViewFirst + 27;
    internal const uint TreeViewGetItemHeight = TreeViewFirst + 28;
    internal const uint TreeViewHitTest = TreeViewFirst + 17;
    internal const uint TreeViewSetBackgroundColor = TreeViewFirst + 29;
    internal const uint TreeViewSetTextColor = TreeViewFirst + 30;
    internal const nuint TreeViewRoot = 0xFFFF0000;
    internal const nuint TreeViewLast = 0xFFFF0002;
    internal const nuint TreeViewCaret = 0x0009;
    internal const nuint TreeViewChild = 0x0004;
    internal const nuint TreeViewFirstVisible = 0x0005;
    internal const nuint TreeViewCollapseItem = 0x0001;
    internal const nuint TreeViewExpandItem = 0x0002;
    internal const uint TreeViewItemText = 0x0001;
    internal const uint TreeViewItemImage = 0x0002;
    internal const uint TreeViewItemState = 0x0008;
    internal const uint TreeViewItemParameter = 0x0004;
    internal const uint TreeViewItemSelectedImage = 0x0020;
    internal const uint TreeViewStateExpanded = 0x0020;
    internal const uint TreeViewHitOnItemButton = 0x0010;
    internal const int TreeViewNotificationFirst = -400;
    internal const int TreeViewNotificationSelectionChanged = TreeViewNotificationFirst - 51;
    internal const int TreeViewNotificationItemExpanding = TreeViewNotificationFirst - 54;
    internal const int TreeViewNotificationRightClick = -5;
    internal const int NotificationDoubleClick = -3;
    internal const int NotificationCustomDraw = -12;
    internal const uint CustomDrawPrePaint = 0x00000001;
    internal const uint CustomDrawItemPrePaint = 0x00010001;
    internal const nint CustomDrawDefault = 0x00000000;
    internal const nint CustomDrawNewFont = 0x00000002;
    internal const nint CustomDrawSkipDefault = 0x00000004;
    internal const nint CustomDrawNotifyItemDraw = 0x00000020;
    internal const uint CustomDrawItemSelected = 0x00000001;
    internal const uint TabControlFirst = 0x1300;
    internal const uint TabControlGetCurrentSelection = TabControlFirst + 11;
    internal const uint TabControlSetCurrentSelection = TabControlFirst + 12;
    internal const uint TabControlInsertItem = TabControlFirst + 62;
    internal const uint TabControlDeleteItem = TabControlFirst + 8;
    internal const uint TabControlGetItemRectangle = TabControlFirst + 10;
    internal const uint TabControlAdjustRectangle = TabControlFirst + 40;
    internal const uint TabControlSetItemSize = TabControlFirst + 41;
    internal const uint TabControlSetPadding = TabControlFirst + 43;
    internal const uint TabItemText = 0x0001;
    internal const int TabControlNotificationFirst = -550;
    internal const int TabControlNotificationSelectionChanged = TabControlNotificationFirst - 1;
    internal const int NotificationClick = -2;
    internal const uint StatusBarSetText = 0x0400 + 11;
    internal const uint ListBoxAddString = 0x0180;
    internal const uint ListBoxInsertString = 0x0181;
    internal const uint ListBoxDeleteString = 0x0182;
    internal const uint ListBoxResetContent = 0x0184;
    internal const uint ListBoxGetCurrentSelection = 0x0188;
    internal const uint ListBoxGetText = 0x0189;
    internal const uint ListBoxGetTextLength = 0x018A;
    internal const uint ListBoxSetCurrentSelection = 0x0186;
    internal const uint ListBoxGetTopIndex = 0x018E;
    internal const uint ListBoxGetHorizontalExtent = 0x0193;
    internal const uint ListBoxSetHorizontalExtent = 0x0194;
    internal const uint ListBoxSetTopIndex = 0x0197;
    internal const uint ListBoxGetItemRectangle = 0x0198;
    internal const uint ListBoxSetItemHeight = 0x01A0;
    internal const uint ListBoxItemFromPoint = 0x01A9;
    internal const int ListBoxNotificationDoubleClick = 2;
    internal const int ListBoxNotificationSelectionChanged = 1;
    internal const uint ComboBoxAddString = 0x0143;
    internal const uint ComboBoxGetCurrentSelection = 0x0147;
    internal const uint ComboBoxSetCurrentSelection = 0x014E;
    internal const int ComboBoxNotificationSelectionChanged = 1;
    internal const int EditNotificationChanged = 0x0300;
    internal const uint MenuString = 0x00000000;
    internal const uint MenuChecked = 0x00000008;
    internal const uint MenuDisabled = 0x00000002;
    internal const uint MenuSeparator = 0x00000800;
    internal const uint TrackPopupReturnCommand = 0x0100;
    internal const uint TrackPopupRightButton = 0x0002;
    internal const uint FormatMessageFromSystem = 0x00001000;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const uint SetWindowPositionNoMove = 0x0002;
    internal const uint SetWindowPositionNoSize = 0x0001;
    internal const uint SetWindowPositionShowWindow = 0x0040;
    internal const uint WindowExtendedStyleTopMost = 0x00000008;
    internal const uint WindowExtendedStyleToolWindow = 0x00000080;
    internal const uint WindowExtendedStyleLayered = 0x00080000;
    internal const uint LayeredWindowAttributesAlpha = 0x00000002;
    internal const uint ToolTipStyleAlwaysTip = 0x00000001;
    internal const uint ToolTipStyleNoPrefix = 0x00000002;
    internal const uint ToolTipFlagIdIsWindow = 0x00000001;
    internal const uint ToolTipFlagSubclass = 0x00000010;
    internal const uint ToolTipFirst = 0x0400;
    internal const uint ToolTipAddTool = ToolTipFirst + 50;
    internal const uint ToolTipUpdateTool = ToolTipFirst + 57;
    internal const uint ToolTipSetMaximumWidth = ToolTipFirst + 24;
    internal const uint TrackMouseEventLeave = 0x00000002;
    internal const uint TrackMouseEventCancel = 0x80000000;
    internal const uint SetWindowPositionFrameChanged = 0x0020;
    internal const int WindowLongStyle = -16;
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
    internal const int VirtualKeyAlt = 0x12;
    internal const int VirtualKeyShift = 0x10;
    internal const int VirtualKeyTab = 0x09;
    internal const int VirtualKeyEscape = 0x1B;
    internal const int VirtualKeyEnter = 0x0D;
    internal const int VirtualKeyUp = 0x26;
    internal const int VirtualKeyDown = 0x28;
    internal const int VirtualKeyLeft = 0x25;
    internal const int VirtualKeyRight = 0x27;
    internal const int VirtualKeySpace = 0x20;
    internal const int VirtualKeyF5 = 0x74;
    internal const uint GetAncestorRoot = 2;
    internal const uint GetAncestorRootOwner = 3;
    internal const int ErrorClassAlreadyExists = 1410;
    internal const int DefaultGuiFont = 17;
    internal const int NullBrush = 5;
    internal const uint OwnerDrawSelected = 0x0001;
    internal const uint OwnerDrawDisabled = 0x0004;
    internal const uint OwnerDrawFocus = 0x0010;
    internal const uint OwnerDrawHotLight = 0x0040;
    internal const int HitTestClient = 1;
    internal const int HitTestTransparent = -1;
    internal const int HitTestCaption = 2;
    internal const int HitTestLeft = 10;
    internal const int HitTestRight = 11;
    internal const int HitTestTop = 12;
    internal const int HitTestTopLeft = 13;
    internal const int HitTestTopRight = 14;
    internal const int HitTestBottom = 15;
    internal const int HitTestBottomLeft = 16;
    internal const int HitTestBottomRight = 17;
    internal static readonly nint WindowPositionTop = 0;
    internal static readonly nint WindowPositionTopMost = unchecked((nint)(-1));
    internal const uint WindowGetPrevious = 3;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal static readonly nint ArrowCursor = (nint)32512;
    internal static readonly nint SizeWestEastCursor = (nint)32644;
    internal static readonly nint SizeNorthSouthCursor = (nint)32645;
    internal static readonly nint TreeViewInsertRoot = unchecked((nint)(-65536));
    internal static readonly nint TreeViewInsertFirst = unchecked((nint)(-65535));
    internal static readonly nint TreeViewInsertLast = unchecked((nint)(-65534));

    internal const string ButtonClass = "BUTTON";
    internal const string EditClass = "EDIT";
    internal const string StaticClass = "STATIC";
    internal const string TreeViewClass = "SysTreeView32";
    internal const string TabControlClass = "SysTabControl32";
    internal const string StatusBarClass = "msctls_statusbar32";
    internal const string ListBoxClass = "LISTBOX";
    internal const string ComboBoxClass = "COMBOBOX";
    internal const string ToolTipClass = "tooltips_class32";

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
    internal struct MinimumMaximumInfo
    {
        internal Point Reserved;
        internal Point MaximumSize;
        internal Point MaximumPosition;
        internal Point MinimumTrackSize;
        internal Point MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rectangle Monitor;
        internal Rectangle WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommonControls
    {
        internal uint Size;
        internal uint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawItem
    {
        internal uint ControlType;
        internal uint ControlIdentifier;
        internal uint ItemIdentifier;
        internal uint ItemAction;
        internal uint ItemState;
        internal nint Control;
        internal nint DeviceContext;
        internal Rectangle ItemRectangle;
        internal nuint ItemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotificationHeader
    {
        internal nint WindowFrom;
        internal nuint IdFrom;
        internal int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CustomDraw
    {
        internal NotificationHeader Header;
        internal uint DrawStage;
        internal nint DeviceContext;
        internal Rectangle Rectangle;
        internal nuint ItemSpec;
        internal uint ItemState;
        internal nint ItemParameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TreeViewCustomDraw
    {
        internal CustomDraw CustomDraw;
        internal uint TextColor;
        internal uint TextBackgroundColor;
        internal int Level;
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
    internal struct TreeViewHitTestInfo
    {
        internal Point Point;
        internal uint Flags;
        internal nint Item;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct ToolInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Window;
        internal nuint Identifier;
        internal Rectangle Rectangle;
        internal nint Instance;
        internal nint Text;
        internal nint Parameter;
        internal nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrackMouseEvent
    {
        internal uint Size;
        internal uint Flags;
        internal nint Window;
        internal uint HoverTime;
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SubclassProcedure(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassIdentifier,
        nuint referenceData);

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

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(
        nint window,
        SubclassProcedure procedure,
        nuint subclassIdentifier,
        nuint referenceData);

    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(
        nint window,
        SubclassProcedure procedure,
        nuint subclassIdentifier);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    internal static extern nint DefaultSubclassProcedure(
        nint window,
        uint message,
        nuint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern nint DefaultWindowProcedure(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, string longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(
        nint window,
        uint message,
        nuint wordParameter,
        [Out] char[] longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TreeViewInsert longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref ToolInfo longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TreeViewItem longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TreeViewHitTestInfo longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref TabItem longParameter);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wordParameter, ref Rectangle longParameter);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "TrackMouseEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TrackMouse(ref TrackMouseEvent tracking);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", EntryPoint = "ShowScrollBar")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowScrollBar(nint window, int bar, [MarshalAs(UnmanagedType.Bool)] bool show);

    [DllImport("user32.dll", EntryPoint = "BeginDeferWindowPos", SetLastError = true)]
    internal static extern nint BeginDeferWindowPosition(int windowCount);

    [DllImport("user32.dll", EntryPoint = "DeferWindowPos", SetLastError = true)]
    internal static extern nint DeferWindowPosition(
        nint deferredPosition,
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "EndDeferWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndDeferWindowPosition(nint deferredPosition);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPosition(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRectangle(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "GetScrollPos")]
    internal static extern int GetScrollPosition(nint window, int bar);

    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    internal static extern nint GetWindowSibling(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetDpiForSystem")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowText(nint window, string text);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPointer(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPointer(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(
        nint window,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, [Out] char[] text, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "EnableWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(nint window);

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

    [DllImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPosition(out Point point);

    [DllImport("user32.dll", EntryPoint = "ScreenToClient")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint window, ref Point point);

    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", EntryPoint = "SetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPosition(int x, int y);

    [DllImport("user32.dll", EntryPoint = "GetWindowPlacement")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)]
    internal static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    internal static extern uint GetCurrentThreadId();

    /// <summary>
    /// 唤醒由原生模态窗口创建的独立消息循环。
    /// </summary>
    internal static void WakeWindowMessageLoop(nint window)
    {
        if (window == 0)
        {
            return;
        }

        uint threadId = GetWindowThreadProcessId(window, out _);
        if (threadId != 0)
        {
            _ = PostThreadMessage(threadId, WindowMessageNull, 0, 0);
        }
    }

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "IsDialogMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsDialogMessage(nint dialog, ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("user32.dll", EntryPoint = "SetCursor")]
    internal static extern nint SetCursor(nint cursor);

    [DllImport("user32.dll", EntryPoint = "SetCapture")]
    internal static extern nint SetCapture(nint window);

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "GetCapture")]
    internal static extern nint GetCapture();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

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
