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

            if (length > DocumentLimits.MaximumTextBytes)
            {
                DocumentClassification textType = DocumentClassifier.Classify(requestedPath, []);
                return new(
                    DocumentReadStatus.TextTooLarge,
                    requestedPath,
                    resolvedPath,
                    textType,
                    length,
                    null,
                    null,
                    null,
                    "文本文件超过 10 MB，已停止读取正文。");
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
                string.Empty);
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
}
