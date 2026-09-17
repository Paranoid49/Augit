using System.Text;
using System.Text.Json;
using Augit.Core.Files;

namespace Augit.Infrastructure.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "settings.json");
    }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new();
            }

            string json = await File.ReadAllTextAsync(_settingsPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ApplicationSettings>(json, SerializerOptions) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ApplicationSettings normalized = Normalize(settings);
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("设置文件必须位于一个目录中。");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _settingsPath + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(normalized, SerializerOptions);
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        string[] openFiles = settings.OpenFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
        string? activeFile = openFiles.FirstOrDefault(
            path => path.Equals(settings.ActiveFile, StringComparison.OrdinalIgnoreCase));
        string[] expandedDirectories = string.IsNullOrWhiteSpace(settings.LastWorkspace)
            ? []
            : [.. WorkspacePathRules.NormalizeExpandedDirectories(settings.LastWorkspace, settings.ExpandedDirectories)];
        string[] recentWorkspaces = settings.RecentWorkspaces
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryGetFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        ToolWindowLayoutSettings toolWindows = settings.ToolWindows ?? new();
        WindowPlacementSettings window = settings.Window ?? new();

        return settings with
        {
            OpenFiles = openFiles,
            ActiveFile = activeFile,
            ExpandedDirectories = expandedDirectories,
            RecentWorkspaces = recentWorkspaces,
            // 窗口尺寸与位置是逻辑单位：宽度/高度收敛到能容纳界面的范围，
            // 位置只保留有限值，非法值交给外壳回退到默认位置。
            Window = new()
            {
                Left = FiniteOrNull(window.Left),
                Top = FiniteOrNull(window.Top),
                Width = NormalizeDimension(window.Width, 320, 20000) ?? 1180,
                Height = NormalizeDimension(window.Height, 320, 20000) ?? 760,
                IsMaximized = window.IsMaximized,
            },
            ToolWindows = new()
            {
                ProjectPanelWidth = NormalizeDimension(toolWindows.ProjectPanelWidth, 240, 1600),
                BottomPanelHeight = NormalizeDimension(toolWindows.BottomPanelHeight, 180, 1200),
            },
            TerminalShell = TerminalShellIds.Normalize(settings.TerminalShell),
            TerminalCustomCommand = NullIfWhiteSpace(settings.TerminalCustomCommand),
        };
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static double? NormalizeDimension(double? value, double minimum, double maximum)
    {
        return value is not null && double.IsFinite(value.Value)
            ? Math.Clamp(value.Value, minimum, maximum)
            : null;
    }

    private static double? FiniteOrNull(double? value)
        => value is { } number && double.IsFinite(number) ? number : null;
}
