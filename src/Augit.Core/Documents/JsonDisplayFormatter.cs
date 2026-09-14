using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Augit.Core.Documents;

public static class JsonDisplayFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static JsonDisplayResult Format(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                source,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            string formatted = JsonSerializer.Serialize(document.RootElement, SerializerOptions);
            return JsonDisplayResult.Success(formatted);
        }
        catch (JsonException exception)
        {
            long line = (exception.LineNumber ?? 0) + 1;
            long column = CharacterColumn(source, line, exception.BytePositionInLine ?? 0);
            return JsonDisplayResult.Failure(source, line, column);
        }
    }

    private static long CharacterColumn(string source, long line, long bytePosition)
    {
        int start = 0;
        for (long current = 1; current < line; current++)
        {
            int newline = source.IndexOf('\n', start);
            if (newline < 0) return 1;
            start = newline + 1;
        }

        // 解析器返回 UTF-8 字节列；界面按 Unicode 标量计列，代理对算一个字符。
        long column = 1;
        foreach (Rune rune in source.AsSpan(start).EnumerateRunes())
        {
            if (rune.Value == '\n' || bytePosition < rune.Utf8SequenceLength) break;
            bytePosition -= rune.Utf8SequenceLength;
            column++;
        }
        return column;
    }
}

public sealed record JsonDisplayResult(bool IsValid, string DisplayText, long? ErrorLine, long? ErrorColumn)
{
    public static JsonDisplayResult Success(string formattedText) => new(true, formattedText, null, null);

    public static JsonDisplayResult Failure(string source, long line, long column) => new(false, source, line, column);
}
