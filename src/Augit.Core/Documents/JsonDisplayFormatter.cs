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
            long column = (exception.BytePositionInLine ?? 0) + 1;
            return JsonDisplayResult.Failure(source, line, column);
        }
    }
}

public sealed record JsonDisplayResult(bool IsValid, string DisplayText, long? ErrorLine, long? ErrorColumn)
{
    public static JsonDisplayResult Success(string formattedText) => new(true, formattedText, null, null);

    public static JsonDisplayResult Failure(string source, long line, long column) => new(false, source, line, column);
}
