using System.IO;
using Augit.Infrastructure.Files;
using Augit.Infrastructure.Interop;
using Augit.Infrastructure.Settings;

namespace Augit.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            SettingsStore settingsStore = new();
            ApplicationSettings settings = settingsStore.LoadAsync().GetAwaiter().GetResult();
            string? requestedWorkspace = ResolveRequestedWorkspace(arguments, settings.LastWorkspace);
            WorkspaceInstanceCoordinator? coordinator = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(requestedWorkspace))
                {
                    WorkspaceValidationResult validation = WorkspaceDirectoryService.ValidateRoot(requestedWorkspace);
                    if (validation.IsValid && validation.FullPath is not null)
                    {
                        requestedWorkspace = validation.FullPath;
                        coordinator = new(requestedWorkspace);
                        if (!coordinator.TryBecomeOwnerAsync().GetAwaiter().GetResult())
                        {
                            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                            return 0;
                        }
                    }
                }

                using MainWindow window = new(settingsStore, settings, requestedWorkspace, coordinator);
                coordinator = null;
                window.Show();
                return MainWindow.RunMessageLoop();
            }
            finally
            {
                if (coordinator is not null)
                {
                    coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception exception)
        {
            _ = NativeMethods.MessageBox(
                0,
                UiText.StartupFailed(exception.Message),
                UiText.AppName,
                NativeMethods.MessageBoxIconError);
            return 1;
        }
    }

    internal static string? ResolveRequestedWorkspace(string[] arguments, string? lastWorkspace)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? candidate = arguments.Length > 0 ? arguments[0] : lastWorkspace;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return candidate;
        }
    }
}
