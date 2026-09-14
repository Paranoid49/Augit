using System.Reflection;
using System.Runtime.InteropServices;

namespace Augit.App.Tests;

internal static class NativeSelectionFocusAssertions
{
    internal static void Verify(MainWindow window, object owner, string fieldName, bool tree = false)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        nint control = (nint)owner.GetType().GetField(fieldName, fields)!.GetValue(owner)!;
        nint other = (nint)typeof(MainWindow).GetField("_documentTabs", fields)!.GetValue(window)!;
        nint previousFocus = NativeMethods.GetFocus();
        nint selection = tree
            ? NativeMethods.SendMessage(control, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0)
            : NativeMethods.SendMessage(control, NativeMethods.ListBoxGetCurrentSelection, 0, 0);
        nint top = tree ? 0 : NativeMethods.SendMessage(control, NativeMethods.ListBoxGetTopIndex, 0, 0);
        try
        {
            // 列表测试固定首行作为绘制样本；不发送选择通知，结束时恢复原有选择和滚动。
            if (!tree)
            {
                _ = NativeMethods.SendMessage(control, NativeMethods.ListBoxSetCurrentSelection, 0, 0);
                _ = NativeMethods.SendMessage(control, NativeMethods.ListBoxSetTopIndex, 0, 0);
            }
            nint selected = tree ? selection : 0;
            NativeMethods.Rectangle row = GetRowRectangle(control, selected, tree);
            NativeThemePalette palette = NativeTheme.Palette(NativeTheme.IsDark("System"));
            uint inactive = fieldName == "_historyList" ? palette.HistorySelectionInactive : palette.SelectionInactive;
            _ = NativeMethods.SetFocus(other);
            foreach ((nint target, uint expected) in new[] { (control, palette.AccentSoft), (other, inactive), (control, palette.AccentSoft) })
            {
                _ = NativeMethods.UpdateWindow(control);
                _ = NativeMethods.SetFocus(target);
                Assert.AreEqual(target, NativeMethods.GetFocus(), fieldName);
                Assert.IsTrue(GetUpdateRectangle(control, out _, false), $"{fieldName} 焦点变化后必须请求局部重绘。");
                Assert.AreEqual(expected, CaptureBackground(control, row), $"{fieldName} 必须按实际 HWND 焦点绘制选中背景。");
                Assert.AreEqual(selected, tree
                    ? NativeMethods.SendMessage(control, NativeMethods.TreeViewGetNextItem, NativeMethods.TreeViewCaret, 0)
                    : NativeMethods.SendMessage(control, NativeMethods.ListBoxGetCurrentSelection, 0, 0), "焦点转移不能取消或移动选择。");
                if (!tree)
                    Assert.AreEqual((nint)0, NativeMethods.SendMessage(control, NativeMethods.ListBoxGetTopIndex, 0, 0), "焦点转移不能滚动列表。");
            }
        }
        finally
        {
            if (!tree)
            {
                _ = NativeMethods.SendMessage(control, NativeMethods.ListBoxSetCurrentSelection, unchecked((nuint)selection), 0);
                _ = NativeMethods.SendMessage(control, NativeMethods.ListBoxSetTopIndex, unchecked((nuint)top), 0);
            }
            _ = NativeMethods.SetFocus(previousFocus);
        }
    }

    private static NativeMethods.Rectangle GetRowRectangle(nint control, nint selected, bool tree)
    {
        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.Rectangle>());
        try
        {
            Marshal.WriteIntPtr(buffer, selected);
            nint result = NativeMethods.SendMessage(control,
                tree ? NativeMethods.TreeViewGetItemRectangle : NativeMethods.ListBoxGetItemRectangle,
                tree ? 0 : unchecked((nuint)selected), buffer);
            Assert.IsTrue(tree ? result != 0 : result != -1);
            return Marshal.PtrToStructure<NativeMethods.Rectangle>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint CaptureBackground(nint control, NativeMethods.Rectangle row)
    {
        Assert.IsTrue(NativeMethods.GetClientRectangle(control, out NativeMethods.Rectangle client));
        Assert.IsGreaterThan(0, client.Right);
        Assert.IsGreaterThan(0, client.Bottom);
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        NativeMethods.BitmapInfo info = new()
        {
            Header = new()
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = client.Right,
                Height = -client.Bottom,
                Planes = 1,
                BitCount = 32,
            },
        };
        nint bitmap = NativeMethods.CreateDeviceIndependentBitmap(dc, ref info, 0, out _, 0, 0);
        Assert.AreNotEqual((nint)0, bitmap);
        nint previous = NativeMethods.SelectObject(dc, bitmap);
        try
        {
            // 标准子控件通过 WM_PRINTCLIENT 绘制实际客户区；不依赖桌面遮挡或抢占前台。
            _ = NativeMethods.SendMessage(control, 0x0318, unchecked((nuint)dc), 0x000C);
            return NativeMethods.GetPixel(dc, client.Right - NativeTheme.Scale(18), row.Top + NativeTheme.Scale(3));
        }
        finally
        {
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(bitmap);
            _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUpdateRectangle(nint window, out NativeMethods.Rectangle rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
