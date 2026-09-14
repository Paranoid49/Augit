using Augit.Core.Git;

namespace Augit.App;

/// <summary>
/// 引用比较的显示文字；缩写仅用于界面，不能作为 Git 查询参数。
/// </summary>
internal readonly record struct NativeGitComparisonCaption(string FileTitle, string BaseRevision, string TargetRevision)
{
    internal string Title => $"{FileTitle} · {BaseRevision} → {TargetRevision}";

    internal static NativeGitComparisonCaption Create(GitComparisonDocument? document)
    {
        string path = string.IsNullOrWhiteSpace(document?.RelativePath)
            ? "全部文件"
            : Path.GetFileName(document.RelativePath);
        return new(
            $"比较: {path}",
            ShortenRevision(document?.BaseRevision ?? UiText.GeneratingDiff),
            string.IsNullOrWhiteSpace(document?.TargetRevision) ? "工作区" : ShortenRevision(document.TargetRevision));
    }

    internal static string ShortenRevision(string revision)
    {
        int suffixStart = revision.AsSpan().IndexOfAny('^', '~');
        int hashLength = suffixStart < 0 ? revision.Length : suffixStart;
        if (hashLength is not 40 and not 64)
        {
            return revision;
        }
        foreach (char character in revision.AsSpan(0, hashLength))
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return revision;
            }
        }

        // 保留父提交、祖先选择符；命名引用和其他 Git 表达式按原样显示。
        foreach (char character in revision.AsSpan(hashLength))
        {
            if (character is not '^' and not '~' && !char.IsAsciiDigit(character))
            {
                return revision;
            }
        }
        return string.Concat(revision.AsSpan(0, 8), revision.AsSpan(hashLength));
    }
}
