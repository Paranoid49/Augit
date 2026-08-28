using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Augit.Core.Documents;
using Augit.Infrastructure.Files;
using Markdig;

namespace Augit.App;

public static partial class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static Task<string> RenderAsync(
        string markdown,
        string workspaceRoot,
        string documentPath,
        bool darkTheme,
        CancellationToken cancellationToken = default)
    {
        return RenderAsync(
            markdown,
            workspaceRoot,
            documentPath,
            darkTheme,
            "Segoe UI",
            "Cascadia Mono",
            14,
            cancellationToken);
    }

    public static async Task<string> RenderAsync(
        string markdown,
        string workspaceRoot,
        string documentPath,
        bool darkTheme,
        string textFontFamily,
        string monospaceFontFamily,
        double fontSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(textFontFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(monospaceFontFamily);
        string escapedSource = RawHtmlRegex().Replace(markdown, static match => WebUtility.HtmlEncode(match.Value));
        string body = Markdown.ToHtml(escapedSource, Pipeline);
        body = await ReplaceAsync(
            body,
            ImageRegex(),
            match => RewriteImageAsync(match, workspaceRoot, documentPath, cancellationToken)).ConfigureAwait(false);
        body = await ReplaceAsync(
            body,
            LinkRegex(),
            match => Task.FromResult(RewriteLink(match, workspaceRoot, documentPath))).ConfigureAwait(false);
        string background = darkTheme ? "#292a2d" : "#ffffff";
        string foreground = darkTheme ? "#f1f3f4" : "#202124";
        string muted = darkTheme ? "#afb3b9" : "#62666d";
        string border = darkTheme ? "#45474c" : "#d9dbdf";
        string textFont = EscapeCssString(textFontFamily);
        string monospaceFont = EscapeCssString(monospaceFontFamily);
        string safeFontSize = Math.Clamp(fontSize, 9, 40).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'">
            <style>
            :root { color-scheme: {{(darkTheme ? "dark" : "light")}}; }
            body { margin: 0 auto; padding: 24px 32px; max-width: 980px; background: {{background}}; color: {{foreground}}; font: {{safeFontSize}}px/1.65 '{{textFont}}', sans-serif; }
            pre, code { font-family: '{{monospaceFont}}', Consolas, monospace; }
            pre { padding: 12px; overflow: auto; border: 1px solid {{border}}; border-radius: 5px; }
            code { background: color-mix(in srgb, {{muted}} 16%, transparent); padding: 1px 4px; border-radius: 3px; }
            a { color: #4c8fe8; }
            img { max-width: 100%; height: auto; }
            table { border-collapse: collapse; }
            th, td { border: 1px solid {{border}}; padding: 6px 10px; }
            blockquote { margin-left: 0; padding-left: 14px; color: {{muted}}; border-left: 3px solid {{border}}; }
            .blocked { color: {{muted}}; border: 1px dashed {{border}}; padding: 4px 8px; }
            </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }

    public static bool TryDecodeAugitLink(Uri uri, out string? path, out string? anchor)
    {
        path = null;
        anchor = null;
        if (!uri.IsAbsoluteUri || !uri.Host.Equals("augit.local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("open", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            path = Encoding.UTF8.GetString(FromBase64Url(segments[1]));
            anchor = segments.Length > 2 ? Encoding.UTF8.GetString(FromBase64Url(segments[2])) : null;
            return true;
        }
        catch (FormatException)
        {
            path = null;
            anchor = null;
            return false;
        }
    }

    private static async Task<string> RewriteImageAsync(
        Match match,
        string workspaceRoot,
        string documentPath,
        CancellationToken cancellationToken)
    {
        string source = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? absoluteUri))
        {
            return absoluteUri.Scheme is "http" or "https" ? match.Value : BlockedImage(source);
        }

        string? resolved = ResolveRelativeFile(workspaceRoot, documentPath, source, out _);
        if (resolved is null)
        {
            return BlockedImage(source);
        }

        FileInfo file = new(resolved);
        if (file.Length > DocumentLimits.MaximumImageBytes)
        {
            return BlockedImage(source);
        }

        byte[] content = await File.ReadAllBytesAsync(resolved, cancellationToken).ConfigureAwait(false);
        DocumentClassification classification = DocumentClassifier.Classify(resolved, content);
        if (!classification.IsSupportedImage
            || !ImageDimensionsReader.TryRead(classification.Kind, content, out ImageDimensions dimensions)
            || dimensions.PixelCount > DocumentLimits.MaximumImagePixels)
        {
            return BlockedImage(source);
        }

        string mediaType = classification.Kind switch
        {
            DocumentKind.Png => "image/png",
            DocumentKind.Jpeg => "image/jpeg",
            DocumentKind.Bmp => "image/bmp",
            _ => throw new InvalidOperationException("不受支持的图片分类。"),
        };
        string dataUri = $"data:{mediaType};base64,{Convert.ToBase64String(content)}";
        return match.Value.Replace(match.Groups["src"].Value, dataUri, StringComparison.Ordinal);
    }

    private static string RewriteLink(Match match, string workspaceRoot, string documentPath)
    {
        string source = WebUtility.HtmlDecode(match.Groups["href"].Value);
        if (source.StartsWith('#'))
        {
            return match.Value;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? absoluteUri))
        {
            return absoluteUri.Scheme is "http" or "https" or "mailto"
                ? match.Value
                : match.Value.Replace(match.Groups["href"].Value, "#", StringComparison.Ordinal);
        }

        string? resolved = ResolveRelativeFile(workspaceRoot, documentPath, source, out string? anchor);
        if (resolved is null)
        {
            return match.Value.Replace(match.Groups["href"].Value, "#", StringComparison.Ordinal);
        }

        string rewritten = $"https://augit.local/open/{ToBase64Url(Encoding.UTF8.GetBytes(resolved))}";
        if (!string.IsNullOrEmpty(anchor))
        {
            rewritten += $"/{ToBase64Url(Encoding.UTF8.GetBytes(anchor))}";
        }

        return match.Value.Replace(match.Groups["href"].Value, rewritten, StringComparison.Ordinal);
    }

    private static string? ResolveRelativeFile(
        string workspaceRoot,
        string documentPath,
        string source,
        out string? anchor)
    {
        anchor = null;
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(source);
        }
        catch (UriFormatException)
        {
            return null;
        }

        int fragmentIndex = decoded.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            anchor = decoded[(fragmentIndex + 1)..];
            decoded = decoded[..fragmentIndex];
        }

        int queryIndex = decoded.IndexOf('?');
        if (queryIndex >= 0)
        {
            decoded = decoded[..queryIndex];
        }

        if (string.IsNullOrWhiteSpace(decoded) || Path.IsPathRooted(decoded))
        {
            return null;
        }

        string? documentDirectory = Path.GetDirectoryName(documentPath);
        if (documentDirectory is null)
        {
            return null;
        }

        string candidate = Path.GetFullPath(Path.Combine(documentDirectory, decoded.Replace('/', Path.DirectorySeparatorChar)));
        string? resolved = SafeLocalPathResolver.ResolveWithinWorkspace(workspaceRoot, candidate);
        return resolved is not null && File.Exists(resolved) ? resolved : null;
    }

    private static string BlockedImage(string source)
    {
        return $"<span class=\"blocked\">{UiText.BlockedImage(WebUtility.HtmlEncode(source))}</span>";
    }

    private static string EscapeCssString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private static async Task<string> ReplaceAsync(string input, Regex regex, Func<Match, Task<string>> evaluator)
    {
        MatchCollection matches = regex.Matches(input);
        if (matches.Count == 0)
        {
            return input;
        }

        StringBuilder builder = new(input.Length);
        int offset = 0;
        foreach (Match match in matches)
        {
            builder.Append(input, offset, match.Index - offset);
            builder.Append(await evaluator(match).ConfigureAwait(false));
            offset = match.Index + match.Length;
        }

        builder.Append(input, offset, input.Length - offset);
        return builder.ToString();
    }

    private static string ToBase64Url(ReadOnlySpan<byte> value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->|</?[A-Za-z][A-Za-z0-9-]*(?:\s[^<>]*?)?/?>", RegexOptions.CultureInvariant)]
    private static partial Regex RawHtmlRegex();

    [GeneratedRegex("<img\\b[^>]*?\\bsrc=\"(?<src>[^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex("<a\\b[^>]*?\\bhref=\"(?<href>[^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}
