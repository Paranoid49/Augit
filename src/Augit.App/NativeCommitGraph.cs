using System.Buffers;
using Augit.Core.Git;

namespace Augit.App;

internal sealed record NativeCommitGraphSegment(int FromColumn, float FromY, int ToColumn, float ToY, int Color, bool Dashed = false);
internal sealed record NativeCommitGraphRow(int Column, int Color, bool IsHead, IReadOnlyList<NativeCommitGraphSegment> Segments);
internal sealed record NativeCommitGraph(IReadOnlyList<NativeCommitGraphRow> Rows, int ColumnCount)
{
    private sealed record Lane(string Hash, int Color);

    internal static NativeCommitGraph Build(IReadOnlyList<GitHistoryEntry> entries)
    {
        Dictionary<string, int> positions = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < entries.Count; index++) positions.TryAdd(entries[index].FullHash, index);
        List<Lane> lanes = [];
        List<NativeCommitGraphRow> rows = [];
        int nextColor = 0;
        int columnCount = 1;
        for (int rowIndex = 0; rowIndex < entries.Count; rowIndex++)
        {
            GitHistoryEntry entry = entries[rowIndex];
            int column = lanes.FindIndex(lane => lane.Hash.Equals(entry.FullHash, StringComparison.OrdinalIgnoreCase));
            bool incoming = column >= 0;
            if (!incoming)
            {
                column = lanes.Count;
                lanes.Add(new(entry.FullHash, nextColor++));
            }
            Lane node = lanes[column];
            Lane[] before = lanes.ToArray();
            lanes.RemoveAt(column);
            string[] parents = entry.ParentHashes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] visibleParents = parents.Where(hash => positions.TryGetValue(hash, out int parentRow) && parentRow > rowIndex).ToArray();
            List<int> missingColors = [];
            int insertion = column;
            for (int parentIndex = 0; parentIndex < parents.Length; parentIndex++)
            {
                string parent = parents[parentIndex];
                if (lanes.Any(lane => lane.Hash.Equals(parent, StringComparison.OrdinalIgnoreCase))) continue;
                int color = parentIndex == 0 ? node.Color : nextColor++;
                if (positions.TryGetValue(parent, out int parentRow) && parentRow > rowIndex)
                    lanes.Insert(Math.Min(insertion++, lanes.Count), new(parent, color));
                else
                    missingColors.Add(color);
            }

            // 每行上半段接续上一行，下半段把已有轨道移到下一行，并按真实父关系分叉或合流。
            List<NativeCommitGraphSegment> segments = [];
            for (int index = 0; index < before.Length; index++)
            {
                if (index == column)
                {
                    if (incoming) segments.Add(new(index, 0, index, 0.5f, node.Color));
                    continue;
                }
                Lane lane = before[index];
                int destination = lanes.FindIndex(next => next.Hash.Equals(lane.Hash, StringComparison.OrdinalIgnoreCase));
                segments.Add(new(index, 0, index, 0.5f, lane.Color));
                segments.Add(new(index, 0.5f, destination, 1, lane.Color));
            }
            foreach (string parent in visibleParents)
            {
                int destination = lanes.FindIndex(lane => lane.Hash.Equals(parent, StringComparison.OrdinalIgnoreCase));
                segments.Add(new(column, 0.5f, destination, 1, lanes[destination].Color));
            }
            // 缺失父关系有独立的短分叉，避免与同列可见父关系重叠而被实线完全遮住。
            int missingColumn = Math.Max(before.Length, lanes.Count);
            foreach (int color in missingColors)
            {
                int destination = visibleParents.Length == 0 && missingColors.Count == 1 ? column : missingColumn++;
                segments.Add(new(column, 0.5f, destination, 0.92f, color, Dashed: true));
                columnCount = Math.Max(columnCount, destination + 1);
            }
            rows.Add(new(column, node.Color, entry.References.Any(reference => reference.IsHead), segments));
            columnCount = Math.Max(columnCount, Math.Max(before.Length, lanes.Count));
        }
        return new(rows, columnCount);
    }

    internal int Width => NativeTheme.Scale(29 + (ColumnCount - 1) * 16);

    internal void DrawRow(nint dc, NativeMethods.Rectangle rectangle, int index, bool dark, uint background)
    {
        if (index < 0 || index >= Rows.Count) return;
        NativeCommitGraphRow row = Rows[index];
        float height = rectangle.Bottom - rectangle.Top;
        float X(int column) => rectangle.Left + NativeTheme.Scale(15 + column * 16);
        int capacity = row.Segments.Count * 2;
        NativeGdiPlusDrawing.ColoredStrokeLine[]? rented = null;
        Span<NativeGdiPlusDrawing.ColoredStrokeLine> lines = capacity <= 128
            ? stackalloc NativeGdiPlusDrawing.ColoredStrokeLine[capacity]
            : (rented = ArrayPool<NativeGdiPlusDrawing.ColoredStrokeLine>.Shared.Rent(capacity));
        try
        {
            Span<NativeGdiPlusDrawing.StrokeLine> segmentLines = stackalloc NativeGdiPlusDrawing.StrokeLine[2];
            int count = 0;
            foreach (NativeCommitGraphSegment segment in row.Segments)
            {
                int written = WriteLines(segment, X(segment.FromColumn), X(segment.ToColumn), rectangle.Top, height, segmentLines);
                for (int line = 0; line < written; line++)
                    lines[count++] = new(segmentLines[line], NativeTheme.GitGraphColor(segment.Color, dark));
            }
            _ = NativeGdiPlusDrawing.StrokeColoredLines(dc, NativeTheme.Scale(1.5f), lines[..count]);
        }
        finally
        {
            if (rented is not null) ArrayPool<NativeGdiPlusDrawing.ColoredStrokeLine>.Shared.Return(rented);
        }
        NativeTheme.DrawCommitGraphNode(dc, (int)X(row.Column), (rectangle.Top + rectangle.Bottom) / 2,
            NativeTheme.GitGraphColor(row.Color, dark), background, row.IsHead);
    }

    internal static NativeGdiPlusDrawing.StrokeLine[] BuildDashedLinesForTest(
        NativeCommitGraphSegment segment,
        float fromX,
        float toX,
        float top,
        float height)
    {
        ArgumentNullException.ThrowIfNull(segment);
        NativeGdiPlusDrawing.StrokeLine[] lines = new NativeGdiPlusDrawing.StrokeLine[2];
        int count = WriteLines(segment, fromX, toX, top, height, lines);
        return lines[..count];
    }

    private static int WriteLines(
        NativeCommitGraphSegment segment,
        float fromX,
        float toX,
        float top,
        float height,
        Span<NativeGdiPlusDrawing.StrokeLine> lines)
    {
        // 虚线从真实的父关系起点开始，不能用固定的 0.68/0.84 覆盖短行或斜向延续。
        float start = segment.FromY;
        float end = segment.ToY;
        float span = end - start;
        if (span <= 0.001f)
        {
            return 0;
        }
        if (!segment.Dashed)
        {
            lines[0] = new(fromX, top + start * height, toX, top + end * height);
            return 1;
        }
        lines[0] = new(fromX, top + start * height, fromX + (toX - fromX) * 0.42f, top + (start + span * 0.42f) * height);
        lines[1] = new(fromX + (toX - fromX) * 0.68f, top + (start + span * 0.68f) * height, toX, top + end * height);
        return 2;
    }
}
