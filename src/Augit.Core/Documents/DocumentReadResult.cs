namespace Augit.Core.Documents;

public enum DocumentReadStatus
{
    TextReady,
    ImageReady,
    BinarySummary,
    TextTooLarge,
    ImageTooLarge,
    InvalidUtf8,
    ImageDecodeFailed,
    Missing,
    AccessDenied,
    UnsafeTarget,
}

public sealed record DocumentReadResult(
    DocumentReadStatus Status,
    string RequestedPath,
    string ResolvedPath,
    DocumentClassification Classification,
    long FileSize,
    string? Text,
    int? PixelWidth,
    int? PixelHeight,
    string Message)
{
    public bool CanOpenExternally => Status != DocumentReadStatus.Missing;
}
