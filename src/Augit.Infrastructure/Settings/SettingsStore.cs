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
                return AlignAppearance(new());
            }

            string json = await File.ReadAllTextAsync(_settingsPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            ApplicationSettings settings = JsonSerializer.Deserialize<ApplicationSettings>(json, SerializerOptions) ?? new();
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && !document.RootElement.EnumerateObject().Any(property => property.Name.Equals("appearanceVersion", StringComparison.OrdinalIgnoreCase)))
            {
                settings = settings with { AppearanceVersion = 0 };
            }
            return AlignAppearance(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AlignAppearance(new());
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

    /// <summary>
    /// 记录一次工作区打开：当前目录移到最近工作区首位，并把 LastWorkspace 更新为它。
    /// 最近工作区用于「Open Workspace」的 Recent Workspaces 列表；必须在新窗口创建前写入，
    /// 这样目标窗口的设置读取和下一次打开都能看到当前项目。
    /// </summary>
    public async Task<ApplicationSettings> RememberWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        string full = NormalizeWorkspacePath(workspacePath);
        ApplicationSettings current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        string[] recent = current.RecentWorkspaces
            .Where(path => !string.Equals(path, full, StringComparison.OrdinalIgnoreCase))
            .Prepend(full)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        bool recentChanged = !current.RecentWorkspaces.SequenceEqual(recent, StringComparer.OrdinalIgnoreCase);
        bool lastChanged = !string.Equals(current.LastWorkspace, full, StringComparison.OrdinalIgnoreCase);
        if (!recentChanged && !lastChanged)
        {
            return current;
        }

        ApplicationSettings updated = current with
        {
            LastWorkspace = full,
            RecentWorkspaces = recent,
        };
        if (lastChanged)
        {
            // 标签会话、活动文件与目录展开层级只属于上一个 LastWorkspace。打开另一个工作区
            // 时若原样保留，新窗口会把这批相对路径当作自己仓库里的文件恢复出来：已不存在的
            // 路径变成“文件已被删除、重命名或替换”的幽灵标签，同名文件则会错误显示别的仓库
            // 内容（2026-11-25 用户实机：global.json 标签出现在不含该文件的仓库）。
            // 面板尺寸有自己的工作区归属：切换前把旧尺寸固定给旧工作区，避免新工作区套用。
            ToolWindowLayoutSettings previousLayout = current.ToolWindows ?? new ToolWindowLayoutSettings();
            updated = updated with
            {
                OpenFiles = [],
                ActiveFile = null,
                ExpandedDirectories = [],
                ToolWindows = previousLayout with
                {
                    WorkspaceRoot = previousLayout.WorkspaceRoot ?? current.LastWorkspace,
                },
            };
        }

        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static string NormalizeWorkspacePath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    /// <summary>旧外观仅迁移一次；正文按用户裁决固定字体及字号，其他会话数据不变。</summary>
    private static ApplicationSettings AlignAppearance(ApplicationSettings settings)
    {
        if (settings.AppearanceVersion < 1)
        {
            settings = settings with
            {
                AppearanceVersion = 1,
                Theme = "Light",
                TextFontFamily = "Microsoft YaHei UI",
                TextFontSize = ApplicationSettings.DefaultUiFontSize,
            };
        }

        // 界面字体固定为 Microsoft YaHei UI；系统缺失时由前端字体栈静默回退到 Segoe UI。
        // 旧配置里的其他界面字体不再生效，下一次保存时归一化到固定值。
        return settings with
        {
            TextFontFamily = "Microsoft YaHei UI",
            MonospaceFontFamily = "JetBrains Mono",
            FontSize = 13,
        };
    }

    private static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        settings = AlignAppearance(settings);
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
                WorkspaceRoot = toolWindows.WorkspaceRoot is { Length: > 0 } owner ? TryGetFullPath(owner) : null,
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
