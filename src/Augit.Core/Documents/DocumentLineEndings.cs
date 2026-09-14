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
