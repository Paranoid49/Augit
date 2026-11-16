namespace Augit.Core.Documents;

/// <summary>只读文本实际解码时使用的字符编码。</summary>
public enum DocumentTextEncoding
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Utf32Le,
    Utf32Be,
}

public sealed record DocumentClassification(DocumentKind Kind, string TypeName)
{
    /// <summary>文本编码；图片和二进制固定为 UTF-8，不参与解码。</summary>
    public DocumentTextEncoding TextEncoding { get; init; } = DocumentTextEncoding.Utf8;

    public bool IsText => Kind is DocumentKind.Text or DocumentKind.Markdown or DocumentKind.Json;

    public bool IsSupportedImage => Kind is DocumentKind.Png or DocumentKind.Jpeg or DocumentKind.Bmp;

    public string EncodingName => TextEncoding switch
    {
        DocumentTextEncoding.Utf16Le => "UTF-16 LE",
        DocumentTextEncoding.Utf16Be => "UTF-16 BE",
        DocumentTextEncoding.Utf32Le => "UTF-32 LE",
        DocumentTextEncoding.Utf32Be => "UTF-32 BE",
        _ => "UTF-8",
    };
}
