using System.Text;

namespace Augit.Core.Documents;

public static class DocumentClassifier
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static DocumentClassification Classify(string fileName, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (HasPrefix(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new(DocumentKind.Png, "PNG 图片");
        }

        if (HasPrefix(content, [0xFF, 0xD8, 0xFF]))
        {
            return new(DocumentKind.Jpeg, "JPEG 图片");
        }

        if (HasPrefix(content, "BM"u8))
        {
            return new(DocumentKind.Bmp, "BMP 图片");
        }

        if (HasPrefix(content, "GIF87a"u8) || HasPrefix(content, "GIF89a"u8))
        {
            return new(DocumentKind.Gif, "GIF 二进制文件");
        }

        if (content.Length >= 12 && HasPrefix(content, "RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return new(DocumentKind.WebP, "WebP 二进制文件");
        }

        if (HasKnownBinarySignature(content) || content.Contains((byte)0))
        {
            return new(DocumentKind.Binary, "二进制文件");
        }

        if (!IsValidUtf8(content))
        {
            return new(DocumentKind.InvalidUtf8, "无效 UTF-8 文本");
        }

        string extension = Path.GetExtension(fileName);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return new(DocumentKind.Markdown, "Markdown 文本");
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return new(DocumentKind.Json, "JSON 文本");
        }

        return new(DocumentKind.Text, "UTF-8 文本");
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> textBytes = HasPrefix(content, [0xEF, 0xBB, 0xBF]) ? content[3..] : content;
        return StrictUtf8.GetString(textBytes);
    }

    public static bool IsValidUtf8(ReadOnlySpan<byte> content)
    {
        try
        {
            _ = DecodeUtf8(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool HasKnownBinarySignature(ReadOnlySpan<byte> content)
    {
        return HasPrefix(content, "%PDF-"u8)
            || HasPrefix(content, [0x50, 0x4B, 0x03, 0x04])
            || HasPrefix(content, [0x4D, 0x5A])
            || HasPrefix(content, [0x7F, 0x45, 0x4C, 0x46])
            || HasPrefix(content, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])
            || HasPrefix(content, [0x1F, 0x8B]);
    }

    private static bool HasPrefix(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix)
    {
        return content.StartsWith(prefix);
    }
}
