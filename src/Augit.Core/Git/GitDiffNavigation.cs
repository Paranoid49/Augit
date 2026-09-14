namespace Augit.Core.Git;

public static class GitDiffNavigation
{
    // 输入保留补丁段边界；输出是去除元数据后正文中的一基行号。
    public static List<int> GetChangeStartLines(IEnumerable<GitDiffLineKind> lineKinds)
    {
        ArgumentNullException.ThrowIfNull(lineKinds);
        List<int> starts = [];
        int visibleLine = 0;
        bool insideChange = false;
        foreach (GitDiffLineKind kind in lineKinds)
        {
            if (kind == GitDiffLineKind.NoNewlineMarker)
            {
                // 无换行标记不占正文行，也不把同一次替换拆开。
                continue;
            }

            if (kind is GitDiffLineKind.Metadata or GitDiffLineKind.HunkHeader)
            {
                insideChange = false;
                continue;
            }

            visibleLine++;
            bool changed = kind is GitDiffLineKind.Added or GitDiffLineKind.Removed or GitDiffLineKind.Modified;
            if (changed && !insideChange)
            {
                starts.Add(visibleLine);
            }
            insideChange = changed;
        }
        return starts;
    }
}
