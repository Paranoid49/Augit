namespace Augit.App;

internal sealed partial class NativeConflictResolverDialog
{
    private bool _displayGridAligned;
    private readonly record struct DisplayGap(int Side, int Line, int Count);
    private sealed record DisplayAlignment(bool Supported, List<DisplayGap> Gaps);

    private static DisplayAlignment BuildDisplayAlignment(List<ConflictViewBlock> blocks, List<int> hiddenLines,
        CancellationToken cancellationToken = default)
    {
        Dictionary<(int Side, int Line), int> padding = [];
        for (int side = 0; side < 3; side++)
        {
            int added = 0;
            foreach (ConflictViewBlock block in blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int documentLine = side == 0 ? block.YoursLine : side == 1 ? block.ResultLine : block.TheirsLine;
                int documentCount = side == 0 ? block.YoursLineCount : side == 1 ? block.ResultLineCount : block.TheirsLineCount;
                int before = block.VirtualStart - GetSideLine(block, side) - added;
                int after = Math.Max(1, block.VirtualLength) - GetSideLineCount(block, side);
                Add(documentLine - 1, before);
                Add(documentLine + documentCount - 1, after);
            }

            void Add(int anchor, int count)
            {
                if (count <= 0) return;
                if (side == 1)
                {
                    int index = hiddenLines.BinarySearch(anchor);
                    while (index >= 0 && hiddenLines[index] == anchor)
                    {
                        anchor--;
                        index--;
                    }
                }
                // -1 表示首行前留白，由正文容器承载，不向 Scintilla 插入占位正文。
                (int, int) key = (side, Math.Max(-1, anchor));
                padding[key] = padding.GetValueOrDefault(key) + count;
                added += count;
            }
        }
        return new(blocks.Count > 0, padding.Select(item => new DisplayGap(item.Key.Side, item.Key.Line, item.Value)).ToList());
    }

    private void BeginDisplayAlignment(DisplayAlignment alignment)
    {
        _displayGridAligned = alignment.Supported;
        _yours!.ClearDisplayGaps();
        _result!.ClearDisplayGaps();
        _theirs!.ClearDisplayGaps();
        _yoursViewport!.SetLeadingLines(0);
        _resultViewport!.SetLeadingLines(0);
        _theirsViewport!.SetLeadingLines(0);
    }

    private void ApplyDisplayGap(DisplayGap gap)
    {
        NativeConflictTextViewport viewport = gap.Side == 0 ? _yoursViewport! : gap.Side == 1 ? _resultViewport! : _theirsViewport!;
        if (gap.Line < 0) viewport.SetLeadingLines(gap.Count);
        else viewport.Editor.SetDisplayGapAfterLine(gap.Line, gap.Count);
    }

    private void RefreshDisplayViewports()
    {
        _yoursViewport?.Refresh();
        _resultViewport?.Refresh();
        _theirsViewport?.Refresh();
    }
}
