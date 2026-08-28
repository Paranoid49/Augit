using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Augit.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RuntimePackageVerificationTests
{
    [TestMethod]
    public void 安装脚本与运行时锁定基线保持一致()
    {
        string installer = File.ReadAllText(FindRepositoryFile("tools", "packaging", "Augit.iss"));
        string baseline = File.ReadAllText(FindRepositoryFile("docs", "runtime-dependencies.md"));
        string[] lockedValues =
        [
            "10.0.11",
            "694e0e0af26b2b8949b8eda8a3831ab31aeac79797d43d6ff8c8798eae642c0904852e641c47329d7d893408f25feab1530ca2b7a0c6ed0d991e0113466a4bf9",
            "151.0.4129.107",
            "213044432",
            "358a11cff88ce519301c3b60bcefe848f922688ab0a333fc0f18ddf83bb3b4f3",
        ];
        foreach (string value in lockedValues)
        {
            Assert.Contains(value, installer, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(value, baseline, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("VersionAtLeast(Version, 151, 0, 4129, 107)", installer, StringComparison.Ordinal);
        Assert.Contains("UsePreviousTasks=yes", installer, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=admin", installer, StringComparison.Ordinal);
        Assert.Contains("MinVersion=10.0.19045", installer, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task 发布脚本可由WindowsPowerShell正确解析中文Utf8()
    {
        string scriptPath = FindRepositoryFile("tools", "release.ps1");
        byte[] bytes = await File.ReadAllBytesAsync(scriptPath);
        Assert.IsGreaterThanOrEqualTo(3, bytes.Length);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        ProcessStartInfo startInfo = CreateWindowsPowerShellStartInfo();
        startInfo.Environment["AUGIT_RELEASE_SCRIPT_FOR_TEST"] = scriptPath;
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(
            "[void][scriptblock]::Create((Get-Content -LiteralPath $env:AUGIT_RELEASE_SCRIPT_FOR_TEST -Raw))");

        VerificationResult result = await RunProcessAsync(startInfo);
        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    [TestMethod]
    public void 运行时资产锁定哈希与当前文件一致()
    {
        string lockPath = FindRepositoryFile("tools", "runtime-assets.lock.json");
        string repositoryRoot = Directory.GetParent(Path.GetDirectoryName(lockPath)!)!.FullName;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        JsonElement assets = document.RootElement.GetProperty("assets");
        Assert.IsGreaterThan(0, assets.GetArrayLength());
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string relativePath = asset.GetProperty("sourcePath").GetString()!;
            string path = Path.GetFullPath(relativePath, repositoryRoot);
            long expectedSize = asset.GetProperty("size").GetInt64();
            string expectedHash = asset.GetProperty("sha256").GetString()!;

            Assert.IsTrue(File.Exists(path), relativePath);
            Assert.AreEqual(expectedSize, new FileInfo(path).Length, relativePath);
            Assert.AreEqual(expectedHash, ComputeSha256(path), true, relativePath);
        }
    }

    [TestMethod]
    public async Task 微软签名且大小与哈希匹配时通过校验()
    {
        string dotNetPath = GetDotNetPath();
        VerificationResult result = await RunVerifierAsync(
            dotNetPath,
            ComputeSha256(dotNetPath),
            new FileInfo(dotNetPath).Length);

        Assert.AreEqual(0, result.ExitCode, result.Output);
    }

    [TestMethod]
    public async Task 文件大小不匹配时在哈希校验前拒绝()
    {
        string dotNetPath = GetDotNetPath();
        VerificationResult result = await RunVerifierAsync(
            dotNetPath,
            ComputeSha256(dotNetPath),
            new FileInfo(dotNetPath).Length + 1);

        Assert.AreEqual(9, result.ExitCode, result.Output);
    }

    [TestMethod]
    public async Task 文件哈希不匹配时拒绝()
    {
        string dotNetPath = GetDotNetPath();
        VerificationResult result = await RunVerifierAsync(
            dotNetPath,
            new string('0', 64),
            new FileInfo(dotNetPath).Length);

        Assert.AreEqual(10, result.ExitCode, result.Output);
    }

    [TestMethod]
    public async Task 文件没有有效签名时拒绝()
    {
        string assemblyPath = typeof(RuntimePackageVerificationTests).Assembly.Location;
        VerificationResult result = await RunVerifierAsync(
            assemblyPath,
            ComputeSha256(assemblyPath),
            new FileInfo(assemblyPath).Length);

        Assert.AreEqual(11, result.ExitCode, result.Output);
    }

    private static string GetDotNetPath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "dotnet.exe");
        Assert.IsTrue(File.Exists(path), $"未找到用于签名校验的 dotnet.exe：{path}");
        return path;
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<VerificationResult> RunVerifierAsync(
        string path,
        string expectedHash,
        long expectedSize)
    {
        string scriptPath = FindRepositoryFile("tools", "packaging", "VerifyRuntime.ps1");
        ProcessStartInfo startInfo = CreateWindowsPowerShellStartInfo();
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Path");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("-Algorithm");
        startInfo.ArgumentList.Add("SHA256");
        startInfo.ArgumentList.Add("-ExpectedHash");
        startInfo.ArgumentList.Add(expectedHash);
        startInfo.ArgumentList.Add("-ExpectedSize");
        startInfo.ArgumentList.Add(expectedSize.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await RunProcessAsync(startInfo);
    }

    private static ProcessStartInfo CreateWindowsPowerShellStartInfo()
    {
        string powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        ProcessStartInfo startInfo = new()
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        return startInfo;
    }

    private static async Task<VerificationResult> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动运行时校验进程。");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        string output = (await standardOutput) + (await standardError);
        return new(process.ExitCode, output);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"未找到仓库文件：{Path.Combine(relativeParts)}");
    }

    private sealed record VerificationResult(int ExitCode, string Output);
}
