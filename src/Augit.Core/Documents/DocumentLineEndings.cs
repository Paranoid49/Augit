namespace Augit.Core.Documents;

public enum DocumentLineEndings
{
    Unknown,
    None,
    Lf,
    CrLf,
    Cr,
    Mixed,
}

public static class DocumentLineEndingDetector
{
    /// <summary>按已解码文本检测换行，支持 UTF-16/UTF-32；字节检测只适用于 UTF-8。</summary>
    public static DocumentLineEndings Detect(string text)
    {
        int kinds = 0;
        int index = 0;
        while (index < text.Length)
        {
            int newline = text.IndexOfAny(['\r', '\n'], index);
            if (newline < 0) break;
            if (text[newline] == '\n') { kinds |= 1; index = newline + 1; }
            else if (newline + 1 < text.Length && text[newline + 1] == '\n') { kinds |= 2; index = newline + 2; }
            else { kinds |= 4; index = newline + 1; }
            if ((kinds & (kinds - 1)) != 0) return DocumentLineEndings.Mixed;
        }
        return kinds switch { 1 => DocumentLineEndings.Lf, 2 => DocumentLineEndings.CrLf, 4 => DocumentLineEndings.Cr, _ => DocumentLineEndings.None };
    }

    public static DocumentLineEndings Detect(ReadOnlySpan<byte> utf8)
    {
        int kinds = 0;
        while (!utf8.IsEmpty)
        {
            int index = utf8.IndexOfAny((byte)'\r', (byte)'\n');
            if (index < 0) break;
            if (utf8[index] == '\n') { kinds |= 1; utf8 = utf8[(index + 1)..]; }
            else if (index + 1 < utf8.Length && utf8[index + 1] == '\n') { kinds |= 2; utf8 = utf8[(index + 2)..]; }
            else { kinds |= 4; utf8 = utf8[(index + 1)..]; }
            if ((kinds & (kinds - 1)) != 0) return DocumentLineEndings.Mixed;
        }
        return kinds switch { 1 => DocumentLineEndings.Lf, 2 => DocumentLineEndings.CrLf, 4 => DocumentLineEndings.Cr, _ => DocumentLineEndings.None };
    }
}
