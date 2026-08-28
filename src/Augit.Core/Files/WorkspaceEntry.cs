namespace Augit.Core.Files;

public sealed record WorkspaceEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool IsReparsePoint,
    bool CanExpand);
