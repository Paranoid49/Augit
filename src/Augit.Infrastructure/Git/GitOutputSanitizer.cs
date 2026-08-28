using System.Text.RegularExpressions;

namespace Augit.Infrastructure.Git;

internal static partial class GitOutputSanitizer
{
    [GeneratedRegex(@"(?<scheme>\b(?:https?|ssh)://)[^\s/@]+(?::[^\s/@]*)?@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentialsPattern();

    [GeneratedRegex(@"(?<name>\b(?:access[_-]?token|api[_-]?key|password|passwd|secret|token)\b\s*[:=]\s*)(?<value>[^\s&;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamedSecretPattern();

    [GeneratedRegex(@"(?<name>\bAuthorization\s*:\s*(?:Basic|Bearer)\s+)(?<value>[^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderPattern();

    public static string Sanitize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        string sanitized = UrlCredentialsPattern().Replace(output, "${scheme}***@");
        sanitized = AuthorizationHeaderPattern().Replace(sanitized, "Authorization: ***");
        sanitized = NamedSecretPattern().Replace(sanitized, "${name}***");
        return sanitized.Trim();
    }
}
