namespace Augit.Core.Documents;

/// <summary>
/// 文档读取的尺寸限制。权威 `FileSizeLimit`（`platform/core-api/src/com/intellij/openapi/vfs/limits/
/// FileSizeLimit.kt:14-24,60-110`）定义**三档**：内容加载、智能感知、预览；三档各有一个默认值，
/// 扩展名**只能放大、不能缩小** —— 小于默认值的登记会被忽略，因为默认值同时是最小值
/// （`FileSizeLimit.kt:20-24,86-91`）。
///
/// 默认值取自 `FileUtilRt`（`platform/util-rt/src/com/intellij/openapi/util/io/FileUtilRt.java:1089-1101,70-77`）：
/// `idea.max.content.load.filesize` = **20 MB**、`idea.max.intellisense.filesize` = **2500 KB**、
/// `idea.max.content.load.large.preview.size` = **2500 KB**
/// （`LARGE_FILE_PREVIEW_SIZE = min(preview, LARGE_FOR_CONTENT_LOADING)`）。
/// </summary>
public static class DocumentLimits
{
    /// <summary>内容加载上限（权威 `getDefaultContentLoadLimit()`，默认 20 MB）。</summary>
    public const long DefaultContentLoadBytes = 20L * 1024 * 1024;

    /// <summary>智能感知上限（权威 `getDefaultIntellisenseLimit()`，默认 2500 KB）。</summary>
    public const long DefaultIntellisenseBytes = 2500L * 1024;

    /// <summary>预览上限（权威 `getDefaultPreviewLimit()`，默认 2500 KB）。</summary>
    public const long DefaultPreviewBytes = 2500L * 1024;

    /// <summary>
    /// 按扩展名登记的限额（权威扩展点 `com.intellij.fileEditor.fileSizeChecker` 里的 `limitsByExtension`）。
    ///
    /// **本 checkout 里没有任何实现** —— 全仓只有扩展点声明本身
    /// （`platform/core-impl/resources/intellij.platform.core.impl.xml:35-37`，另有 `ExtensionSizeLimitInfo`
    /// 与 `FileSizeLimit` 两个 API 文件），因此这张表为空、三档都取默认值。保留同一套结构是为了：
    /// ① 与权威的"按扩展名放大"规则同构；② 将来登记时不必再改调用点。
    /// </summary>
    private static readonly Dictionary<string, (long Content, long Intellisense, long Preview)> LimitsByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
        };

    /// <summary>内容加载上限：超过即按"大文件"只读预览（权威 `LargeFileEditorProvider.accept()`，`:35-39`）。</summary>
    public static long ContentLoadLimit(string? path) => LimitFor(path, LimitKind.Content, DefaultContentLoadBytes);

    /// <summary>智能感知上限（Augit 不跑语言分析，登记备查，供将来使用）。</summary>
    public static long IntellisenseLimit(string? path) => LimitFor(path, LimitKind.Intellisense, DefaultIntellisenseBytes);

    /// <summary>预览上限：超过内容加载上限的文本只读显示前这么多字节（权威 `getPreviewLimit(extension)`）。</summary>
    public static long PreviewLimit(string? path) => LimitFor(path, LimitKind.Preview, DefaultPreviewBytes);

    /// <summary>
    /// 冲突解决器读取文件的尺寸上限。它与上面三档**无关**：三档限制的是"编辑／预览能力"，
    /// 这里限制的是"合并能力的输入规模"（超过就不再把正文交给冲突页）。
    /// </summary>
    public const long MaximumTextBytes = 10L * 1024 * 1024;

    public const long MaximumImageBytes = 25L * 1024 * 1024;

    public const long MaximumImagePixels = 25_000_000;

    private enum LimitKind
    {
        Content,
        Intellisense,
        Preview,
    }

    private static long LimitFor(string? path, LimitKind kind, long fallback)
    {
        string extension = ExtensionOf(path);
        if (extension.Length == 0
            || !LimitsByExtension.TryGetValue(extension, out (long Content, long Intellisense, long Preview) limits))
        {
            return fallback;
        }

        long value = kind switch
        {
            LimitKind.Content => limits.Content,
            LimitKind.Intellisense => limits.Intellisense,
            _ => limits.Preview,
        };
        // 权威：默认值同时是**最小值**，登记值更小即被忽略。
        return Math.Max(value, fallback);
    }

    private static string ExtensionOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string extension = Path.GetExtension(path);
        return extension.StartsWith('.') ? extension[1..] : extension;
    }
}
