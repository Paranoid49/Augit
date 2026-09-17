namespace Augit.Shell.Tests.Helpers;

/// <summary>测试用临时目录；Dispose 时递归删除（Git 会写只读文件，先清属性）。</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "Augit.Shell.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string GetPath(string relativePath) => Path.Combine(FullPath, relativePath);

    public void Dispose()
    {
        if (!Directory.Exists(FullPath))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(FullPath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(FullPath, true);
    }
}
