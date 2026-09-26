namespace Augit.Core.Documents;

public enum DocumentReadStatus
{
    TextReady,
    ImageReady,
    BinarySummary,

    /// <summary>
    /// 文本超过内容加载上限：仍然给正文，但只是**前 N 字节的只读预览**
    /// （权威 `LargeFileEditorProvider` ＋ `LargeFileNotificationProvider`，
    /// `.../text/LargeFileEditorProvider.java:35-46`、`.../text/LargeFileNotificationProvider.java:37-58`）。
    /// </summary>
    TextPreview,

    /// <summary>旧状态：不带正文的"太大"拒绝。文本读取已改为 <see cref="TextPreview"/>，此值仅为兼容保留。</summary>
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
    public DocumentLineEndings LineEndings { get; init; }

    /// <summary>
    /// 只读预览实际读了前多少字节（仅 <see cref="DocumentReadStatus.TextPreview"/> 有值）。
    /// 对应权威警告里的 `{1}`：`FileSizeLimit.getPreviewLimit(extension)`。
    /// </summary>
    public long? PreviewBytes { get; init; }

    public bool CanOpenExternally => Status != DocumentReadStatus.Missing;
}
