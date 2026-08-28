using System.Text.RegularExpressions;

namespace Augit.Core.Git;

public static partial class ConventionalCommitValidator
{
    [GeneratedRegex(@"^[^\s():!]+(?:\([^()\r\n]+\))?!?: \S.*$")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^(?:BREAKING CHANGE|BREAKING-CHANGE): \S.*$")]
    private static partial Regex BreakingFooterPattern();

    public static GitCommitValidationResult Validate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GitCommitValidationResult.Invalid("提交信息不能为空。");
        }

        string normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        if (!HeaderPattern().IsMatch(lines[0]))
        {
            return GitCommitValidationResult.Invalid(
                "提交标题必须符合 type(scope)!: description 或对应的不带 scope、破坏性标记形式。");
        }

        if (lines.Length > 1 && lines[1].Length != 0)
        {
            return GitCommitValidationResult.Invalid("提交正文必须与标题之间保留一个空行。");
        }

        foreach (string line in lines.Skip(2))
        {
            if ((line.StartsWith("BREAKING CHANGE", StringComparison.Ordinal)
                    || line.StartsWith("BREAKING-CHANGE", StringComparison.Ordinal))
                && !BreakingFooterPattern().IsMatch(line))
            {
                return GitCommitValidationResult.Invalid(
                    "破坏性变更 footer 必须使用 BREAKING CHANGE: description 格式。");
            }
        }

        return GitCommitValidationResult.Valid();
    }
}

public sealed record GitCommitValidationResult(bool IsValid, string? ErrorMessage)
{
    public static GitCommitValidationResult Valid() => new(true, null);

    public static GitCommitValidationResult Invalid(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, errorMessage);
    }
}
