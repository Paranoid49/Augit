using System.Text;

namespace Augit.Core.Documents;

public static class DocumentClassifier
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, false, true);
    private static readonly UTF32Encoding StrictUtf32Le = new(false, false, true);
    private static readonly UTF32Encoding StrictUtf32Be = new(true, false, true);

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

        if (HasKnownBinarySignature(content))
        {
            return new(DocumentKind.Binary, "二进制文件");
        }

        // 先按 BOM 识别 UTF-16/UTF-32：这些文本天然含大量 NUL 字节，
        // 不能再用“含 NUL 即二进制”的旧规则拒绝。SQL、配置和脚本常被 Windows
        // 工具保存为带 BOM 的 UTF-16。
        if (TryClassifyBomText(fileName, content, out DocumentClassification? bomText))
        {
            return bomText!;
        }

        if (content.Contains((byte)0))
        {
            // 无 BOM 的 UTF-16 只能在高置信度时接受；否则仍是二进制。
            if (TryClassifyBomlessUtf16(fileName, content, out DocumentClassification? utf16Text))
            {
                return utf16Text!;
            }

            return new(DocumentKind.Binary, "二进制文件");
        }

        if (!IsValidUtf8(content))
        {
            return new(DocumentKind.InvalidUtf8, "无效 UTF-8 文本");
        }

        return CreateTextClassification(fileName, DocumentTextEncoding.Utf8);
    }

    /// <summary>按分类中的编码解码文本，自动去掉对应 BOM。</summary>
    public static string DecodeText(ReadOnlySpan<byte> content, DocumentClassification classification)
    {
        ReadOnlySpan<byte> payload = content[EncodingBomLength(content, classification.TextEncoding)..];
        return classification.TextEncoding switch
        {
            DocumentTextEncoding.Utf16Le => StrictUtf16Le.GetString(payload),
            DocumentTextEncoding.Utf16Be => StrictUtf16Be.GetString(payload),
            DocumentTextEncoding.Utf32Le => StrictUtf32Le.GetString(payload),
            DocumentTextEncoding.Utf32Be => StrictUtf32Be.GetString(payload),
            _ => StrictUtf8.GetString(payload),
        };
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

    /// <summary>
    /// 截断到完整字符边界，供大文件只读预览使用。
    /// UTF-16 必须按 2 字节、UTF-32 按 4 字节对齐；UTF-8 按多字节序列回退。
    /// </summary>
    public static int CompleteTextPrefixLength(ReadOnlySpan<byte> content, DocumentTextEncoding encoding)
    {
        return encoding switch
        {
            DocumentTextEncoding.Utf16Le or DocumentTextEncoding.Utf16Be => content.Length - (content.Length % 2),
            DocumentTextEncoding.Utf32Le or DocumentTextEncoding.Utf32Be => content.Length - (content.Length % 4),
            _ => CompleteUtf8PrefixLength(content),
        };
    }

    private static DocumentClassification CreateTextClassification(string fileName, DocumentTextEncoding encoding)
    {
        string extension = Path.GetExtension(fileName);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return new(DocumentKind.Markdown, "Markdown 文本") { TextEncoding = encoding };
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return new(DocumentKind.Json, "JSON 文本") { TextEncoding = encoding };
        }

        return new(
            DocumentKind.Text,
            encoding == DocumentTextEncoding.Utf8 ? "UTF-8 文本" : $"{EncodingDisplayName(encoding)} 文本")
        {
            TextEncoding = encoding,
        };
    }

    private static bool TryClassifyBomText(
        string fileName,
        ReadOnlySpan<byte> content,
        out DocumentClassification? classification)
    {
        classification = null;
        if (HasPrefix(content, [0x00, 0x00, 0xFE, 0xFF]))
        {
            return TryDecodeBomText(fileName, content[4..], DocumentTextEncoding.Utf32Be, out classification);
        }
        if (HasPrefix(content, [0xFF, 0xFE, 0x00, 0x00]))
        {
            return TryDecodeBomText(fileName, content[4..], DocumentTextEncoding.Utf32Le, out classification);
        }
        if (HasPrefix(content, [0xFE, 0xFF]))
        {
            return TryDecodeBomText(fileName, content[2..], DocumentTextEncoding.Utf16Be, out classification);
        }
        if (HasPrefix(content, [0xFF, 0xFE]))
        {
            return TryDecodeBomText(fileName, content[2..], DocumentTextEncoding.Utf16Le, out classification);
        }
        if (HasPrefix(content, [0xEF, 0xBB, 0xBF]))
        {
            return TryDecodeBomText(fileName, content[3..], DocumentTextEncoding.Utf8, out classification);
        }
        return false;
    }

    private static bool TryDecodeBomText(
        string fileName,
        ReadOnlySpan<byte> payload,
        DocumentTextEncoding encoding,
        out DocumentClassification? classification)
    {
        classification = null;
        string decoded;
        try
        {
            decoded = DecodeText(payload, new DocumentClassification(DocumentKind.Text, "") { TextEncoding = encoding });
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (decoded.Length > 0 && !IsTextLike(decoded))
        {
            return false;
        }

        classification = CreateTextClassification(fileName, encoding);
        return true;
    }

    private static bool TryClassifyBomlessUtf16(
        string fileName,
        ReadOnlySpan<byte> content,
        out DocumentClassification? classification)
    {
        classification = null;
        if (content.Length < 4 || content.Length % 2 != 0)
        {
            return false;
        }

        int zeroOnEven = 0;
        int zeroOnOdd = 0;
        for (int index = 0; index < content.Length; index += 2)
        {
            if (content[index] == 0) zeroOnEven++;
            if (content[index + 1] == 0) zeroOnOdd++;
        }

        DocumentTextEncoding preferred = zeroOnOdd >= zeroOnEven
            ? DocumentTextEncoding.Utf16Le
            : DocumentTextEncoding.Utf16Be;
        if (Math.Max(zeroOnEven, zeroOnOdd) < content.Length / 8)
        {
            return false;
        }

        foreach (DocumentTextEncoding encoding in preferred == DocumentTextEncoding.Utf16Le
            ? new[] { DocumentTextEncoding.Utf16Le, DocumentTextEncoding.Utf16Be }
            : new[] { DocumentTextEncoding.Utf16Be, DocumentTextEncoding.Utf16Le })
        {
            if (!TryDecodeBomText(fileName, content, encoding, out DocumentClassification? candidate))
            {
                continue;
            }

            string text = DecodeText(content, candidate!);
            if (!IsTextLike(text))
            {
                continue;
            }

            classification = candidate;
            return true;
        }

        return false;
    }

    private static bool IsTextLike(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        int controls = 0;
        foreach (char character in text)
        {
            if (character is '\r' or '\n' or '\t') continue;
            if (char.IsControl(character)) controls++;
        }
        return controls == 0;
    }

    private static int EncodingBomLength(ReadOnlySpan<byte> content, DocumentTextEncoding encoding)
    {
        return encoding switch
        {
            DocumentTextEncoding.Utf32Be when HasPrefix(content, [0x00, 0x00, 0xFE, 0xFF]) => 4,
            DocumentTextEncoding.Utf32Le when HasPrefix(content, [0xFF, 0xFE, 0x00, 0x00]) => 4,
            DocumentTextEncoding.Utf16Be when HasPrefix(content, [0xFE, 0xFF]) => 2,
            DocumentTextEncoding.Utf16Le when HasPrefix(content, [0xFF, 0xFE]) => 2,
            DocumentTextEncoding.Utf8 when HasPrefix(content, [0xEF, 0xBB, 0xBF]) => 3,
            _ => 0,
        };
    }

    private static string EncodingDisplayName(DocumentTextEncoding encoding) => encoding switch
    {
        DocumentTextEncoding.Utf16Le => "UTF-16 LE",
        DocumentTextEncoding.Utf16Be => "UTF-16 BE",
        DocumentTextEncoding.Utf32Le => "UTF-32 LE",
        DocumentTextEncoding.Utf32Be => "UTF-32 BE",
        _ => "UTF-8",
    };

    private static int CompleteUtf8PrefixLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return 0;
        }

        int index = bytes.Length;
        int walked = 0;
        while (index > 0 && walked < 3 && (bytes[index - 1] & 0xC0) == 0x80)
        {
            index--;
            walked++;
        }

        if (index == 0)
        {
            return 0;
        }

        byte lead = bytes[index - 1];
        int expected = lead switch
        {
            <= 0x7F => 1,
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1,
        };
        int available = bytes.Length - index + 1;
        return available < expected ? index - 1 : bytes.Length;
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
        => content.StartsWith(prefix);
}
