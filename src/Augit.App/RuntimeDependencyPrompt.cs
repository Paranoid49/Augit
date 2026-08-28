using Augit.Infrastructure.Interop;

namespace Augit.App;

internal static class RuntimeDependencyPrompt
{
    internal static readonly Uri WebView2DownloadUri = new(
        "https://developer.microsoft.com/microsoft-edge/webview2/#download-section");

    internal static void ShowWebView2Missing(nint owner)
    {
        int result = NativeMethods.MessageBox(
            owner,
            UiText.WebView2RuntimeMissing,
            UiText.AppName,
            NativeMethods.MessageBoxYesNo | NativeMethods.MessageBoxIconWarning);
        if (result != NativeMethods.DialogResultYes)
        {
            return;
        }

        ExternalLaunchResult launch = ExternalProgramLauncher.OpenUriWithDefaultApplication(WebView2DownloadUri);
        if (!launch.IsSuccess)
        {
            _ = NativeMethods.MessageBox(
                owner,
                launch.ErrorMessage ?? UiText.ExternalProgramFailed,
                UiText.AppName,
                NativeMethods.MessageBoxIconWarning);
        }
    }
}
