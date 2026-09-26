using System.Globalization;
using Augit.Core.Documents;
using Augit.Core.Files;

namespace Augit.Infrastructure.Files;

public static class ReadOnlyDocumentService
{
    private const int HeaderSize = 64 * 1024;

    public static async Task<DocumentReadResult> ReadAsync(string workspaceRoot, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return Failure(DocumentReadStatus.Missing, path, path, "文件路径无效。");
        }

        if (!WorkspacePathRules.IsWithin(workspaceRoot, requestedPath))
        {
            return Failure(DocumentReadStatus.UnsafeTarget, requestedPath, requestedPath, "请求的文件不在当前工作区内。");
        }

        string? resolvedPath = ResolveReadablePath(requestedPath);
        if (resolvedPath is null)
        {
            return Failure(DocumentReadStatus.UnsafeTarget, requestedPath, requestedPath, "符号链接目标失效、循环或不在本机固定磁盘上。");
        }

        if (!File.Exists(resolvedPath))
        {
            return Failure(DocumentReadStatus.Missing, requestedPath, resolvedPath, "文件已被删除、重命名或替换。");
        }

        try
        {
            await using FileStream stream = new(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                HeaderSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            byte[] header = new byte[Math.Min(length, HeaderSize)];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            DocumentClassification headerClassification = DocumentClassifier.Classify(requestedPath, header);

            if (headerClassification.IsSupportedImage)
            {
                return ReadImage(requestedPath, resolvedPath, length, headerClassification, header);
            }

            if (headerClassification.Kind is DocumentKind.Gif or DocumentKind.WebP or DocumentKind.Binary)
            {
                return new(
                    DocumentReadStatus.BinarySummary,
                    requestedPath,
                    resolvedPath,
                    headerClassification,
                    length,
                    null,
                    null,
                    null,
                    "此文件只提供二进制摘要，可使用系统默认程序打开。");
            }

            if (length > DocumentLimits.ContentLoadLimit(requestedPath))
            {
                // 权威对超过**内容加载上限**的文本打开"大文件编辑器"
                // （`LargeFileEditorProvider.accept()`，platform/platform-impl/src/com/intellij/openapi/fileEditor/
                // impl/text/LargeFileEditorProvider.java:35-46`）：编辑器只读（`editor.setViewer(true)`），
                // 并**显示前 `getPreviewLimit(extension)` 字节的只读预览** —— 警告文案
                // "The file is too large ({0}). Showing a read-only preview of the first {1}."
                // （`.../text/LargeFileNotificationProvider.java:49-56`，`IdeBundle.properties:2120`）。
                // 因此这里不再整页拒绝，而是回前 N 字节的正文 ＋ 预览字节数（界面据此给可隐藏的警告）。
                long previewBytes = Math.Min(length, DocumentLimits.PreviewLimit(requestedPath));
                byte[] preview = new byte[previewBytes];
                stream.Position = 0;
                await stream.ReadExactlyAsync(preview, cancellationToken).ConfigureAwait(false);
                // 截断边界必须落在**完整字符**上：截到半个 UTF-8 序列会解码出替换字符。
                int usable = Utf8CompletePrefixLength(preview);
                ReadOnlySpan<byte> prefix = preview.AsSpan(0, usable);
                DocumentClassification previewClassification = DocumentClassifier.Classify(requestedPath, prefix);
                return new(
                    DocumentReadStatus.TextPreview,
                    requestedPath,
                    resolvedPath,
                    previewClassification,
                    length,
                    DocumentClassifier.DecodeUtf8(prefix),
                    null,
                    null,
                    $"文件过大（{FormatSize(length)}）。这里显示前 {FormatSize(previewBytes)} 的只读预览。")
                {
                    PreviewBytes = previewBytes,
                    LineEndings = DocumentLineEndingDetector.Detect(prefix),
                };
            }

            stream.Position = 0;
            byte[] content = new byte[length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            DocumentClassification classification = DocumentClassifier.Classify(requestedPath, content);
            if (classification.Kind == DocumentKind.InvalidUtf8)
            {
                return new(
                    DocumentReadStatus.InvalidUtf8,
                    requestedPath,
                    resolvedPath,
                    classification,
                    length,
                    null,
                    null,
                    null,
                    "文件不是有效的 UTF-8。");
            }

            if (!classification.IsText)
            {
                return new(
                    DocumentReadStatus.BinarySummary,
                    requestedPath,
                    resolvedPath,
                    classification,
                    length,
                    null,
                    null,
                    null,
                    "此文件只提供二进制摘要，可使用系统默认程序打开。");
            }

            return new(
                DocumentReadStatus.TextReady,
                requestedPath,
                resolvedPath,
                classification,
                length,
                DocumentClassifier.DecodeUtf8(content),
                null,
                null,
                string.Empty)
            {
                LineEndings = DocumentLineEndingDetector.Detect(content),
            };
        }
        catch (FileNotFoundException)
        {
            return Failure(DocumentReadStatus.Missing, requestedPath, resolvedPath, "文件已被删除、重命名或替换。");
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(DocumentReadStatus.Missing, requestedPath, resolvedPath, "文件所在目录已不存在。");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(DocumentReadStatus.AccessDenied, requestedPath, resolvedPath, "没有读取此文件的权限。");
        }
        catch (IOException)
        {
            return Failure(DocumentReadStatus.AccessDenied, requestedPath, resolvedPath, "文件当前无法读取，请稍后重试。");
        }
    }

    private static DocumentReadResult ReadImage(
        string requestedPath,
        string resolvedPath,
        long length,
        DocumentClassification classification,
        ReadOnlySpan<byte> header)
    {
        if (length > DocumentLimits.MaximumImageBytes)
        {
            return new(
                DocumentReadStatus.ImageTooLarge,
                requestedPath,
                resolvedPath,
                classification,
                length,
                null,
                null,
                null,
                "图片超过 25 MB，已停止预览。");
        }

        if (!ImageDimensionsReader.TryRead(classification.Kind, header, out ImageDimensions dimensions))
        {
            return new(
                DocumentReadStatus.ImageDecodeFailed,
                requestedPath,
                resolvedPath,
                classification,
                length,
                null,
                null,
                null,
                "无法读取图片尺寸，已停止预览。");
        }

        if (dimensions.PixelCount > DocumentLimits.MaximumImagePixels)
        {
            return new(
                DocumentReadStatus.ImageTooLarge,
                requestedPath,
                resolvedPath,
                classification,
                length,
                null,
                dimensions.Width,
                dimensions.Height,
                "图片超过 2500 万解码像素，已停止预览。");
        }

        return new(
            DocumentReadStatus.ImageReady,
            requestedPath,
            resolvedPath,
            classification,
            length,
            null,
            dimensions.Width,
            dimensions.Height,
            string.Empty);
    }

    private static string? ResolveReadablePath(string requestedPath)
    {
        try
        {
            FileInfo file = new(requestedPath);
            if (!file.Exists)
            {
                return file.LinkTarget is null ? requestedPath : null;
            }

            string resolvedPath = requestedPath;
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo? target = file.ResolveLinkTarget(true);
                if (target is null || !target.Exists)
                {
                    return null;
                }

                resolvedPath = Path.GetFullPath(target.FullName);
            }

            return WorkspaceDirectoryService.IsFixedLocalPath(resolvedPath) ? resolvedPath : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DocumentReadResult Failure(DocumentReadStatus status, string requestedPath, string resolvedPath, string message)
    {
        return new(
            status,
            requestedPath,
            resolvedPath,
            new(DocumentKind.Binary, "未知文件"),
            0,
            null,
            null,
            null,
            message);
    }

    /// <summary>
    /// 字节前缀里**完整 UTF-8 字符**的长度：不足一个完整序列的尾部整个丢掉
    /// （预览截断落在字符中间时，解码会多出一个替换字符 —— 权威的预览也是按内容读取，不做半字符截断）。
    /// </summary>
    internal static int Utf8CompletePrefixLength(ReadOnlySpan<byte> bytes)
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
        int expected = (lead & 0x80) == 0x00 ? 1
            : (lead & 0xE0) == 0xC0 ? 2
            : (lead & 0xF0) == 0xE0 ? 3
            : (lead & 0xF8) == 0xF0 ? 4
            : 1;
        return expected <= bytes.Length - (index - 1) ? bytes.Length : index - 1;
    }

    /// <summary>人类可读的文件大小（权威警告里的 `{0}`／`{1}` 由 `StringUtil.formatFileSize` 生成）。</summary>
    internal static string FormatSize(long bytes)
    {
        const long kilobyte = 1024;
        const long megabyte = 1024 * kilobyte;
        if (bytes >= megabyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / megabyte:0.#} MB");
        }

        if (bytes >= kilobyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / kilobyte:0.#} KB");
        }

        return $"{bytes} 字节";
    }
}
