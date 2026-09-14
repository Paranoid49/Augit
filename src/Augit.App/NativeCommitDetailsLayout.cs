using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Augit.App;

internal sealed record NativeCommitDetailsLayout(NativeGdiPlusDrawing.TextBlock[] Blocks, int Height)
{
    internal static NativeCommitDetailsLayout Create(string body, string family, int fontHeight,
        int width, int bodyTop, int padding, CancellationToken cancellationToken,
        Action<NativeCommitDetailsLayout>? firstBlockReady = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nint dc = NativeMethods.CreateCompatibleDeviceContext(0);
        nint font = NativeMethods.CreateFont(-fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, family);
        nint previous = 0;
        GCHandle pinned = default;
        try
        {
            if (dc == 0 || font == 0) throw new Win32Exception("无法创建提交说明排版资源。");
            previous = NativeMethods.SelectObject(dc, font);
            using NativeGdiPlusDrawing.WrappedTextSession text = new(dc);
            pinned = GCHandle.Alloc(body, GCHandleType.Pinned);
            nint pointer = pinned.AddrOfPinnedObject();
            List<NativeGdiPlusDrawing.TextBlock> blocks = [];
            double top = bodyTop;
            for (int start = 0; start < body.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(Math.Clamp(width / Math.Max(1, fontHeight / 2) * 10, 128, 2048), body.Length - start);
                NativeGdiPlusDrawing.TextBlock block;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 保留下一块的上下文，不能在任意字符边界人为插入换行。
                    if (start + count < body.Length && char.IsHighSurrogate(body[start + count - 1])) count--;
                    block = text.Measure(pointer, start, count, width, top);
                    if (block.Length < count || start + count == body.Length) break;
                    count = (int)Math.Min(Math.Max((long)count * 2, 2048), body.Length - start);
                }
                if (block.Length == 0)
                {
                    // 极窄区域仍消费一个完整文字元素，超出部分由客户区裁切，不能丢字或死循环。
                    int element = StringInfo.GetNextTextElementLength(body.AsSpan(start));
                    block = text.Measure(pointer, start, element, Math.Max(width, fontHeight * element * 2), top);
                    if (block.Length == 0) throw new Win32Exception("提交说明包含无法排版的文字。");
                }
                blocks.Add(block);
                top += block.Height;
                start += block.Length;
                if (blocks.Count == 1 && start < body.Length)
                    firstBlockReady?.Invoke(new([block], checked((int)Math.Ceiling(top) + padding)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(blocks.ToArray(), checked((int)Math.Ceiling(top) + padding));
        }
        finally
        {
            if (pinned.IsAllocated) pinned.Free();
            if (previous != 0) _ = NativeMethods.SelectObject(dc, previous);
            if (font != 0) _ = NativeMethods.DeleteObject(font);
            if (dc != 0) _ = NativeMethods.DeleteDeviceContext(dc);
        }
    }
}
