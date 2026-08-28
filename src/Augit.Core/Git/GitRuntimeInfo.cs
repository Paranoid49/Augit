namespace Augit.Core.Git;

public enum GitRuntimeStatus
{
    Available,
    NotFound,
    InvalidConfiguredPath,
    CannotStart,
    UnknownVersion,
    VersionTooOld,
}

public sealed record GitRuntimeInfo(
    GitRuntimeStatus Status,
    string? ExecutablePath,
    GitVersion? Version,
    string? UnavailableReason)
{
    public bool IsAvailable => Status == GitRuntimeStatus.Available;

    public static GitRuntimeInfo Available(string executablePath, GitVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return new(GitRuntimeStatus.Available, executablePath, version, null);
    }

    public static GitRuntimeInfo Unavailable(
        GitRuntimeStatus status,
        string unavailableReason,
        string? executablePath = null,
        GitVersion? version = null)
    {
        if (status == GitRuntimeStatus.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);
        return new(status, executablePath, version, unavailableReason);
    }
}
