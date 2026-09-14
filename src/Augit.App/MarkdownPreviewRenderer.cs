using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Augit.Core.Documents;
using Augit.Infrastructure.Files;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace Augit.App;

public static partial class MarkdownPreviewRenderer
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
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
            NativeFontResolver.DefaultInterfaceFamily,
            NativeFontResolver.DefaultMonospaceFamily,
            13,
            cancellationToken: cancellationToken);
    }

    public static Task<string> RenderAsync(
        string markdown,
        string workspaceRoot,
        string documentPath,
        bool darkTheme,
        string textFontFamily,
        string monospaceFontFamily,
        double fontSize,
        double? monospaceFontSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(textFontFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(monospaceFontFamily);
        return Task.Run(async () =>
        {
            await RenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await RenderCoreAsync(markdown, workspaceRoot, documentPath, darkTheme, textFontFamily,
                    monospaceFontFamily, fontSize, monospaceFontSize, cancellationToken).ConfigureAwait(false);
            }
            finally { RenderGate.Release(); }
        }, cancellationToken);
    }

    private static async Task<string> RenderCoreAsync(string markdown, string workspaceRoot, string documentPath,
        bool darkTheme, string textFontFamily, string monospaceFontFamily, double fontSize,
        double? monospaceFontSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string body = Markdown.ToHtml(markdown, Pipeline);
        cancellationToken.ThrowIfCancellationRequested();
        body = await ReplaceAsync(
            body,
            ImageRegex(),
            match => RewriteImageAsync(match, workspaceRoot, documentPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        body = await ReplaceAsync(
            body,
            LinkRegex(),
            match => Task.FromResult(RewriteLink(match, workspaceRoot, documentPath)), cancellationToken).ConfigureAwait(false);
        NativeThemePalette palette = NativeTheme.Palette(darkTheme);
        string background = CssColor(palette.Panel);
        string foreground = CssColor(palette.Text);
        string muted = CssColor(palette.Muted);
        string border = CssColor(palette.Border);
        string textFont = EscapeCssString(NativeFontResolver.ResolveInterface(textFontFamily));
        string monospaceFont = EscapeCssString(NativeFontResolver.ResolveMonospace(monospaceFontFamily));
        string safeFontSize = Math.Clamp(fontSize, 9, 40).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string safeMonospaceFontSize = Math.Clamp(monospaceFontSize ?? fontSize, 9, 40).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'">
            <style>
            :root { color-scheme: {{(darkTheme ? "dark" : "light")}}; }
            html { background: {{background}}; }
            body { margin: 0; padding: 24px 36px 40px; background: {{background}}; color: {{foreground}}; font: {{safeFontSize}}px/1.62 '{{textFont}}', 'Microsoft YaHei UI', sans-serif; }
            h1 { margin: 0 0 28px; font-size: 29px; font-weight: 600; }
            h2 { margin: 26px 0 13px; font-size: 21px; font-weight: 600; }
            p, li { font-size: {{safeFontSize}}px; }
            pre, code { font-family: '{{monospaceFont}}', Consolas, monospace; font-size: {{safeMonospaceFontSize}}px; }
            pre { padding: 12px; overflow: auto; border: 1px solid {{border}}; border-radius: 5px; }
            code { background: color-mix(in srgb, {{muted}} 16%, transparent); padding: 1px 4px; border-radius: 3px; }
            a { color: #4c8fe8; }
            img { max-width: 100%; height: auto; }
            .markdown-image { display: inline-block; max-width: 100%; vertical-align: middle; }
            .markdown-image-feedback { display: inline-block; max-width: 100%; box-sizing: border-box; color: {{muted}}; border: 1px dashed {{border}}; padding: 4px 8px; overflow-wrap: anywhere; }
            .markdown-image[data-state='ready'] .markdown-image-feedback,
            .markdown-image:not([data-state='ready']) img { display: none; }
            ::-webkit-scrollbar { width: 10px; height: 10px; }
            ::-webkit-scrollbar-track { background: transparent; }
            ::-webkit-scrollbar-thumb { background: color-mix(in srgb, {{muted}} 68%, transparent); border: 2px solid {{background}}; border-radius: 6px; }
            table { border-collapse: collapse; }
            th, td { border: 1px solid {{border}}; padding: 6px 10px; }
            blockquote { margin-left: 0; padding-left: 14px; color: {{muted}}; border-left: 3px solid {{border}}; }
            .blocked { color: {{muted}}; border: 1px dashed {{border}}; padding: 4px 8px; overflow-wrap: anywhere; }
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
        try
        {
            string result = await ReadImageAsync(match, source, workspaceRoot, documentPath, cancellationToken).ConfigureAwait(false);
            // 被阻止资源已带原因；合法图片的网络与解码反馈由预览内固定脚本处理。
            return result.StartsWith("<img", StringComparison.OrdinalIgnoreCase)
                ? $"<span class=\"markdown-image\" data-state=\"pending\">{result}<span class=\"markdown-image-feedback\" role=\"status\">正在加载图片…</span></span>"
                : result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return BlockedImage(source);
        }
    }

    private static async Task<string> ReadImageAsync(Match match, string source, string workspaceRoot,
        string documentPath, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? absoluteUri))
        {
            return absoluteUri.Scheme is "http" or "https" ? match.Value : BlockedImage(source);
        }

        string? resolved = ResolveRelativeFile(workspaceRoot, documentPath, source, out _);
        if (resolved is null)
        {
            return BlockedImage(source);
        }

        await using FileStream file = new(resolved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = file.Length;
        if (length > DocumentLimits.MaximumImageBytes)
        {
            return BlockedImage(source);
        }

        // 只读取已校验的长度，避免文件在读取期间增长导致分配突破图片上限。
        byte[] content = new byte[checked((int)length)];
        await file.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
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
                : BlockedLink(match, source);
        }

        string? resolved = ResolveRelativeFile(workspaceRoot, documentPath, source, out string? anchor);
        if (resolved is null)
        {
            return BlockedLink(match, source);
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
            // 先分离 URL 语法，再解码文件名，保留文件名内经过转义的 # 和 ?。
            int fragment = source.IndexOf('#');
            if (fragment >= 0) { anchor = Uri.UnescapeDataString(source[(fragment + 1)..]); source = source[..fragment]; }
            int query = source.IndexOf('?');
            if (query >= 0) source = source[..query];
            decoded = Uri.UnescapeDataString(source);
        }
        catch (UriFormatException)
        {
            return null;
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

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(documentDirectory, decoded.Replace('/', Path.DirectorySeparatorChar)));
            string? resolved = SafeLocalPathResolver.ResolveWithinWorkspace(workspaceRoot, candidate);
            return resolved is not null && File.Exists(resolved) ? resolved : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static string BlockedImage(string source)
    {
        return $"<span class=\"blocked\">{UiText.BlockedImage(WebUtility.HtmlEncode(source))}</span>";
    }

    private static string BlockedLink(Match match, string source) =>
        $"<span class=\"blocked\" title=\"{WebUtility.HtmlEncode(source)}\">{match.Groups["label"].Value}（链接已阻止：路径不存在、越出工作区或协议不支持）</span>";

    private static string EscapeCssString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    private static string CssColor(uint color) => $"#{color & 255:x2}{(color >> 8) & 255:x2}{(color >> 16) & 255:x2}";

    private static async Task<string> ReplaceAsync(string input, Regex regex, Func<Match, Task<string>> evaluator, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
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

    [GeneratedRegex("<img\\b[^>]*?\\bsrc=\"(?<src>[^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex("<a\\b[^>]*?\\bhref=\"(?<href>[^\"]*)\"[^>]*>(?<label>[\\s\\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}
