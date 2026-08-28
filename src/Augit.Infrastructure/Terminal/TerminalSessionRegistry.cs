using System.Security.Cryptography;
using System.Text;

namespace Augit.Infrastructure.Terminal;

public sealed class TerminalSessionRegistry
{
    private readonly string _directory;

    public TerminalSessionRegistry(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "TerminalSessions");
    }

    public TerminalSessionLease? TryAcquire(string workspacePath)
    {
        string lockPath = GetLockPath(workspacePath);
        try
        {
            Directory.CreateDirectory(_directory);
            FileStream stream = new(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                128,
                FileOptions.WriteThrough);
            stream.SetLength(0);
            using (StreamWriter writer = new(stream, new UTF8Encoding(false), 128, leaveOpen: true))
            {
                writer.WriteLine(Environment.ProcessId);
                writer.WriteLine(Path.GetFullPath(workspacePath));
                writer.Flush();
            }

            stream.Position = 0;
            return new(stream, lockPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsActive(string workspacePath)
    {
        using TerminalSessionLease? lease = TryAcquire(workspacePath);
        return lease is null;
    }

    internal string GetLockPath(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        string normalized = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_directory, $"{hash}.lock");
    }
}

public sealed class TerminalSessionLease : IDisposable
{
    private FileStream? _stream;
    private readonly string _lockPath;

    internal TerminalSessionLease(FileStream stream, string lockPath)
    {
        _stream = stream;
        _lockPath = lockPath;
    }

    public void Dispose()
    {
        FileStream? stream = Interlocked.Exchange(ref _stream, null);
        if (stream is null)
        {
            return;
        }

        stream.Dispose();
        try
        {
            File.Delete(_lockPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
