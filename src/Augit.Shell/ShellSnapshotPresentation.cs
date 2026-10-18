namespace Augit.Shell;

/// <summary>控制器显示后等待两次渲染回调；不将图片解码完成当作已显示。</summary>
internal static class ShellSnapshotPresentation
{
    /// <summary>导航前固定首图尺寸，避免onload前以物理宽高作为CSS尺寸闪现。</summary>
    internal static string InitialImageSize(ReadOnlySpan<byte> png, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (png.Length < 24 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[8..12]) != 13
            || !png[12..16].SequenceEqual("IHDR"u8)) throw new ArgumentException("快照缺少有效PNG尺寸头。", nameof(png));
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        if (width <= 0 || height <= 0) throw new ArgumentException("PNG尺寸必须为正数。", nameof(png));
        return FormattableString.Invariant($"width:{width / scale}px;height:{height / scale}px;");
    }

    // CapturePreview可能按逻辑客户区向上取整；以PNG真实宽高还原，不能压回宿主边界。
    internal const string ImageSizingScript = """
        window.fitSnapshotImage = image => {
          image.style.width = `${image.naturalWidth / window.snapshotCaptureScale}px`;
          image.style.height = `${image.naturalHeight / window.snapshotCaptureScale}px`;
        };
        """;

    // 首图定尺后等待两帧；隐藏文档解码完成仍不足以证明首图已参与合成。
    internal const string ImageLoadedScript = """
        fitSnapshotImage(this);requestAnimationFrame(()=>requestAnimationFrame(()=>chrome.webview.postMessage('loaded:'+this.dataset.revision)))
        """;

    internal const string Script = """
        (() => {
          requestAnimationFrame(() => requestAnimationFrame(() => {
            chrome.webview.postMessage('presented');
          }));
        })()
        """;
}
