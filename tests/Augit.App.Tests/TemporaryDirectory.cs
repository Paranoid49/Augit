using System.IO;

namespace Augit.App.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "Augit.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string GetPath(string relativePath) => Path.Combine(FullPath, relativePath);

    public void Dispose()
    {
        if (Directory.Exists(FullPath))
        {
            foreach (string file in Directory.EnumerateFiles(FullPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(FullPath, true);
        }
    }
}
