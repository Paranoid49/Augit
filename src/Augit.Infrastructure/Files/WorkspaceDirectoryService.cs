using Augit.Core.Files;

namespace Augit.Infrastructure.Files;

public static class WorkspaceDirectoryService
{
    public static WorkspaceValidationResult ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return WorkspaceValidationResult.Invalid("请选择本地目录。");
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return WorkspaceValidationResult.Invalid("目录不存在或无法访问。");
            }

            if (!IsFixedLocalPath(fullPath))
            {
                return WorkspaceValidationResult.Invalid("Augit 只支持本机固定磁盘上的目录。");
            }

            return WorkspaceValidationResult.Valid(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return WorkspaceValidationResult.Invalid("目录路径无效或无法访问。");
        }
    }

    public static IReadOnlyList<WorkspaceEntry> EnumerateChildren(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        EnumerationOptions options = new()
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        List<WorkspaceEntry> entries = [];
        foreach (FileSystemInfo item in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos("*", options))
        {
            if (WorkspacePathRules.IsHiddenGitEntry(item.FullName))
            {
                continue;
            }

            bool isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
            bool isReparsePoint = (item.Attributes & FileAttributes.ReparsePoint) != 0;
            entries.Add(new(item.Name, item.FullName, isDirectory, isReparsePoint, isDirectory && !isReparsePoint));
        }

        entries.Sort(CompareEntries);
        return entries;
    }

    internal static bool IsFixedLocalPath(string fullPath)
    {
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        string? root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
    }

    private static int CompareEntries(WorkspaceEntry left, WorkspaceEntry right)
    {
        int typeComparison = right.IsDirectory.CompareTo(left.IsDirectory);
        return typeComparison != 0 ? typeComparison : NaturalNameComparer.Instance.Compare(left.Name, right.Name);
    }
}

public sealed record WorkspaceValidationResult(bool IsValid, string? FullPath, string? ErrorMessage)
{
    public static WorkspaceValidationResult Valid(string fullPath) => new(true, fullPath, null);

    public static WorkspaceValidationResult Invalid(string message) => new(false, null, message);
}
