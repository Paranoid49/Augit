using Augit.Core.Files;

namespace Augit.Infrastructure.Files;

public static class SafeLocalPathResolver
{
    public static string? ResolveWithinWorkspace(string workspaceRoot, string candidatePath)
    {
        try
        {
            string root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(candidatePath);
            if (!WorkspacePathRules.IsWithin(root, candidate))
            {
                return null;
            }

            string relative = Path.GetRelativePath(root, candidate);
            string current = root;
            foreach (string segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (!item.Exists)
                {
                    return null;
                }

                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    FileSystemInfo? target = item.ResolveLinkTarget(true);
                    if (target is null || !target.Exists)
                    {
                        return null;
                    }

                    current = Path.GetFullPath(target.FullName);
                    if (!WorkspacePathRules.IsWithin(root, current))
                    {
                        return null;
                    }
                }
            }

            return current;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
