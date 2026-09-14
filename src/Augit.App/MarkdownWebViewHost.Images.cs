namespace Augit.App;

internal sealed partial class MarkdownWebViewHost
{
    // 只在自有预览页处理图片 DOM；不开放宿主对象、不放宽文档 CSP，也不轮询图片状态。
    private const string ImageFeedbackScript = """
        (() => {
          const update = image => {
            if (!(image instanceof HTMLImageElement) || !image.isConnected) return;
            const wrapper = image.closest('.markdown-image');
            if (!wrapper) return;
            const state = !image.complete ? 'loading' : image.naturalWidth > 0 ? 'ready' : 'failure';
            if (wrapper.dataset.state === state) return;
            wrapper.dataset.state = state;
            const feedback = wrapper.querySelector('.markdown-image-feedback');
            const label = image.alt || '图片';
            feedback.textContent = state === 'failure' ? `图片加载失败：${label}（无法下载或解码）`
              : state === 'loading' ? `正在加载图片：${label}…` : '';
          };
          window.augitRefreshImages = () => document.querySelectorAll('.markdown-image img').forEach(update);
          document.addEventListener('load', event => update(event.target), true);
          document.addEventListener('error', event => update(event.target), true);
          document.addEventListener('DOMContentLoaded', window.augitRefreshImages, { once: true });
        })();
        """;
}
