namespace Augit.Infrastructure.Settings;

public sealed record ApplicationSettings
{
    public int Version { get; init; } = 1;

    public string? LastWorkspace { get; init; }

    public string Theme { get; init; } = "System";

    public string TextFontFamily { get; init; } = "Segoe UI";

    public string MonospaceFontFamily { get; init; } = "Cascadia Mono";

    public double FontSize { get; init; } = 14;

    public string? GitExecutablePath { get; init; }

    public string TerminalShell { get; init; } = TerminalShellIds.WindowsPowerShell;

    public string? TerminalCustomCommand { get; init; }

    public WindowPlacementSettings Window { get; init; } = new();

    public string[] OpenFiles { get; init; } = [];

    public string[] ExpandedDirectories { get; init; } = [];

    public string[] RecentWorkspaces { get; init; } = [];
}

public static class TerminalShellIds
{
    public const string WindowsPowerShell = "WindowsPowerShell";

    public const string PowerShell7 = "PowerShell7";

    public const string CommandPrompt = "CommandPrompt";

    public const string GitBash = "GitBash";

    public const string Wsl = "Wsl";

    public const string Custom = "Custom";

    public static string Normalize(string? shell)
    {
        return shell is PowerShell7 or CommandPrompt or GitBash or Wsl or Custom
            ? shell
            : WindowsPowerShell;
    }
}

public sealed record WindowPlacementSettings
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public double Width { get; init; } = 1180;

    public double Height { get; init; } = 760;

    public bool IsMaximized { get; init; }
}
