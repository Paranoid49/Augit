using System.Text.RegularExpressions;

namespace Augit.Core.Git;

public static partial class GitUnifiedDiffParser
{
    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderPattern();

    public static IReadOnlyList<GitDiffLine> Parse(string unifiedPatch)
    {
        ArgumentNullException.ThrowIfNull(unifiedPatch);
        List<GitDiffLine> lines = [];
        int oldLine = 0;
        int newLine = 0;
        bool insideHunk = false;
        foreach (string rawLine in unifiedPatch.Split('\n'))
        {
            string line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            Match hunkMatch = HunkHeaderPattern().Match(line);
            if (hunkMatch.Success)
            {
                oldLine = int.Parse(hunkMatch.Groups["old"].Value, System.Globalization.CultureInfo.InvariantCulture);
                newLine = int.Parse(hunkMatch.Groups["new"].Value, System.Globalization.CultureInfo.InvariantCulture);
                insideHunk = true;
                lines.Add(new(GitDiffLineKind.HunkHeader, null, null, line));
                continue;
            }

            if (!insideHunk)
            {
                if (line.Length > 0)
                {
                    lines.Add(new(GitDiffLineKind.Metadata, null, null, line));
                }

                continue;
            }

            if (line.StartsWith('\\'))
            {
                lines.Add(new(GitDiffLineKind.NoNewlineMarker, null, null, line));
            }
            else if (line.StartsWith('-'))
            {
                lines.Add(new(GitDiffLineKind.Removed, oldLine++, null, line[1..]));
            }
            else if (line.StartsWith('+'))
            {
                lines.Add(new(GitDiffLineKind.Added, null, newLine++, line[1..]));
            }
            else if (line.StartsWith(' '))
            {
                lines.Add(new(GitDiffLineKind.Context, oldLine++, newLine++, line[1..]));
            }
            else if (line.Length > 0)
            {
                lines.Add(new(GitDiffLineKind.Metadata, null, null, line));
            }
        }

        return lines;
    }

    public static IReadOnlyList<GitSideBySideRow> ToSideBySide(IReadOnlyList<GitDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<GitSideBySideRow> rows = [];
        for (int index = 0; index < lines.Count; index++)
        {
            GitDiffLine line = lines[index];
            if (line.Kind == GitDiffLineKind.Removed)
            {
                List<GitDiffLine> removed = [];
                while (index < lines.Count && lines[index].Kind == GitDiffLineKind.Removed)
                {
                    removed.Add(lines[index++]);
                }

                List<GitDiffLine> added = [];
                while (index < lines.Count && lines[index].Kind == GitDiffLineKind.Added)
                {
                    added.Add(lines[index++]);
                }

                index--;
                AppendChangedRows(rows, removed, added);
                continue;
            }

            if (line.Kind == GitDiffLineKind.Added)
            {
                AppendChangedRows(rows, [], [line]);
                continue;
            }

            rows.Add(new(
                line.OldLineNumber,
                line.Text,
                [],
                line.NewLineNumber,
                line.Text,
                [],
                line.Kind));
        }

        return rows;
    }

    private static void AppendChangedRows(
        List<GitSideBySideRow> rows,
        List<GitDiffLine> removed,
        List<GitDiffLine> added)
    {
        int count = Math.Max(removed.Count, added.Count);
        for (int index = 0; index < count; index++)
        {
            GitDiffLine? oldLine = index < removed.Count ? removed[index] : null;
            GitDiffLine? newLine = index < added.Count ? added[index] : null;
            (IReadOnlyList<GitTextSpan> oldChanges, IReadOnlyList<GitTextSpan> newChanges) = oldLine is not null && newLine is not null
                ? GitWordDiff.FindChanges(oldLine.Text, newLine.Text)
                : (oldLine is null ? [] : [new GitTextSpan(0, oldLine.Text.Length)],
                    newLine is null ? [] : [new GitTextSpan(0, newLine.Text.Length)]);
            rows.Add(new(
                oldLine?.OldLineNumber,
                oldLine?.Text,
                oldChanges,
                newLine?.NewLineNumber,
                newLine?.Text,
                newChanges,
                oldLine is null ? GitDiffLineKind.Added : newLine is null ? GitDiffLineKind.Removed : GitDiffLineKind.Modified));
        }
    }
}
