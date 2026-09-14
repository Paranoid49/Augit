using System.ComponentModel;
using System.Runtime.InteropServices;
using Augit.Core.Documents;
using Microsoft.Web.WebView2.Core;

namespace Augit.App;

internal sealed partial class NativeDocumentView
{
    private bool _markdownPreviewDirty = true;
    private Task _markdownPreviewWork = Task.CompletedTask;
    internal Func<string, CancellationToken, Task<string>>? MarkdownRendererForTest { get; set; }
    internal MarkdownWebViewHost? MarkdownHostForTest => _markdownPreview;
    internal Task MarkdownWorkForTest => _markdownPreviewWork;

    private void CancelMarkdownPreviewWork()
    {
        _markdownPreviewVersion++;
        CancellationTokenSource? cancellation = _markdownPreviewCancellation;
        _markdownPreviewCancellation = null;
        cancellation?.Cancel();
        // 取消源由所属任务释放，避免连续切换时读取已销毁的 Token。
        DestroyChild(ref _previewStatusLabel);
    }

    private Task SetMarkdownModeAsync(DocumentDisplayMode mode, bool forceReload = false)
    {
        if (_result.Classification.Kind != DocumentKind.Markdown || _disposed) return Task.CompletedTask;
        if (mode == DocumentDisplayMode.Original) { ShowOriginal(); return Task.CompletedTask; }
        _displayMode = mode;
        _showAlternative = true;
        if (mode != DocumentDisplayMode.Split) CancelMarkdownSplitterDrag();
        InvalidateToolbar();
        _originalEditor?.SetVisible(mode == DocumentDisplayMode.Split || _markdownPreview is null);
        Layout();
        if (forceReload) { _markdownPreviewDirty = true; CancelMarkdownPreviewWork(); }
        if (!NativeMethods.IsWindowVisible(Handle)) return Task.CompletedTask;
        if (_markdownPreviewCancellation is not null) return _markdownPreviewWork;
        if (_markdownPreview is not null && !_markdownPreviewDirty) return Task.CompletedTask;

        CancelMarkdownPreviewWork();
        CancellationTokenSource cancellation = new();
        _markdownPreviewCancellation = cancellation;
        int version = _markdownPreviewVersion;
        _markdownPreviewError = null;
        DestroyChild(ref _previewErrorLabel);
        Task previous = _markdownPreviewWork;
        _markdownPreviewWork = RenderMarkdownPreviewAsync(previous, version, cancellation);
        return _markdownPreviewWork;
    }

    private bool IsCurrentMarkdownRequest(int version, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && version == _markdownPreviewVersion
        && _displayMode != DocumentDisplayMode.Original && NativeMethods.IsWindowVisible(Handle);

    private async Task RenderMarkdownPreviewAsync(Task previous, int version, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        using CancellationTokenSource indicator = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task feedback = ShowMarkdownLoadingAsync(version, indicator.Token);
        MarkdownWebViewHost? created = null;
        try
        {
            // 串行接收文档更新；旧浏览器任务结束前不提交新 DOM，避免晚到结果覆盖正文。
            await previous;
            if (!IsCurrentMarkdownRequest(version, token)) return;
            string html = MarkdownRendererForTest is { } render
                ? await render(_result.Text ?? string.Empty, token)
                : await MarkdownPreviewRenderer.RenderAsync(_result.Text ?? string.Empty, _workspaceRoot,
                    _result.RequestedPath, NativeTheme.IsDark(_settings.Theme), _settings.TextFontFamily,
                    _settings.MonospaceFontFamily, _settings.UiFontSize, _settings.FontSize, token);
            if (!IsCurrentMarkdownRequest(version, token)) return;
            if (_markdownPreview is null)
            {
                created = await MarkdownWebViewHost.CreateAsync(Handle, html,
                    (path, anchor) => _openLinkedFile(path, null, anchor),
                    message => { if (!_disposed && NativeMethods.IsWindowVisible(Handle)) _setStatus(message); }, cancellationToken: token);
                if (!IsCurrentMarkdownRequest(version, token)) return;
                _markdownPreview = created;
                created = null;
            }
            else await _markdownPreview.UpdateContentAsync(html, token);
            if (!IsCurrentMarkdownRequest(version, token)) return;
            _markdownPreviewDirty = false;
            _originalEditor?.SetVisible(_displayMode == DocumentDisplayMode.Split);
            DestroyChild(ref _previewStatusLabel);
            Layout();
            if (!string.IsNullOrEmpty(_pendingAnchor))
            {
                string anchor = _pendingAnchor;
                _pendingAnchor = null;
                await _markdownPreview.NavigateToAnchorAsync(anchor);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or COMException
            or InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or Win32Exception)
        {
            if (IsCurrentMarkdownRequest(version, token))
            {
                if (exception is WebView2RuntimeNotFoundException) RuntimeDependencyPrompt.ShowWebView2Missing(Handle);
                _markdownPreviewError = exception.Message;
                _originalEditor?.SetVisible(_displayMode == DocumentDisplayMode.Split || _markdownPreview is null);
                _previewErrorLabel = CreateChild(NativeMethods.StaticClass,
                    $"预览失败：{exception.Message}",
                    NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible, 0);
                NativeTheme.ApplyToControl(_previewErrorLabel, NativeTheme.IsDark(_settings.Theme));
                Layout();
            }
        }
        finally
        {
            created?.Dispose();
            indicator.Cancel();
            await feedback;
            if (ReferenceEquals(_markdownPreviewCancellation, cancellation))
            {
                _markdownPreviewCancellation = null;
                DestroyChild(ref _previewStatusLabel);
                if (!_disposed) Layout();
            }
            cancellation.Dispose();
        }
    }

    private async Task ShowMarkdownLoadingAsync(int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
            if (!IsCurrentMarkdownRequest(version, token)) return;
            _previewStatusLabel = CreateChild(NativeMethods.StaticClass, UiText.MarkdownPreviewLoading,
                NativeMethods.WindowStyleChild | NativeMethods.WindowStyleVisible | NativeMethods.StaticOwnerDraw,
                PreviewStatusIdentifier);
            Layout();
        }
        catch (OperationCanceledException) { }
    }
}
