namespace Augit.Core.Documents;

public sealed record DocumentClassification(DocumentKind Kind, string TypeName)
{
    public bool IsText => Kind is DocumentKind.Text or DocumentKind.Markdown or DocumentKind.Json;

    public bool IsSupportedImage => Kind is DocumentKind.Png or DocumentKind.Jpeg or DocumentKind.Bmp;
}
