using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Augit.Infrastructure.Updates;

/// <summary>一次更新检查的结果。错误只用于 UI/日志，不向上抛网络异常。</summary>
public sealed record UpdateCheckResult(
    bool Available,
    string CurrentVersion,
    string? Version,
    UpdateManifest? Manifest,
    string? Error);

/// <summary>已下载、验签、解压并校验过版本的暂存更新。</summary>
public sealed record StagedUpdate(
    UpdateManifest Manifest,
    string AppDirectory,
    string PackagePath);

/// <summary>
/// 便携版更新服务：检查签名发布清单、下载便携 zip、校验哈希并解压暂存。
///
/// 应用自身不直接覆盖正在运行的文件；真正替换由 <c>Augit.exe --apply-update</c> 在新进程里完成。
/// 本类只负责可信下载与暂存，所有网络内容都经过 detached ECDSA 签名校验。
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string ReleasesApiUrl = "https://api.github.com/repos/Paranoid49/Augit/releases?per_page=30";
    public const string ManifestAssetName = "update.json";
    public const string SignatureAssetName = "update.json.sig";

    private const long MaximumManifestBytes = 128 * 1024;
    private const long MaximumPackageBytes = 200L * 1024 * 1024;
    private const long MaximumExtractedBytes = 300L * 1024 * 1024;
    private const int MaximumArchiveEntries = 20_000;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _publicKeyBase64;
    private readonly string _stagingRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateManifest? _lastManifest;

    public UpdateService(
        string currentVersion,
        string? stagingRoot = null,
        HttpClient? httpClient = null,
        string? publicKeyBase64 = null)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            throw new ArgumentException("当前版本不能为空。", nameof(currentVersion));
        }

        CurrentVersion = currentVersion.Split('+', 2)[0];
        _stagingRoot = stagingRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Augit",
            "updates");
        _publicKeyBase64 = publicKeyBase64 ?? UpdateTrust.PublicKeyBase64;
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5),
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Augit/{CurrentVersion}");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public string CurrentVersion { get; }

    /// <summary>只在当前进程内保留通过验签的清单，下载接口不接受网页传来的清单。</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastManifest = null;
            string releasesJson = await _httpClient.GetStringAsync(ReleasesApiUrl, cancellationToken).ConfigureAwait(false);
            List<(SemanticVersion Version, string Tag, JsonElement Release)> releases = [];
            using JsonDocument document = JsonDocument.Parse(releasesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Failure("更新服务返回了无效的发布列表。");
            }

            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }
                string? tag = ReadString(release, "tag_name");
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith('v'))
                {
                    continue;
                }
                if (!SemanticVersion.TryParse(tag[1..], out SemanticVersion? version) || version is null)
                {
                    continue;
                }
                releases.Add((version, tag, release));
            }

            if (releases.Count == 0)
            {
                return Failure("没有可用的发布版本。");
            }

            (SemanticVersion latestVersion, string latestTag, JsonElement latestRelease) = releases
                .OrderByDescending(item => item.Version)
                .First();
            string? manifestUrl = FindAssetUrl(latestRelease, ManifestAssetName);
            string? signatureUrl = FindAssetUrl(latestRelease, SignatureAssetName);
            if (manifestUrl is null || signatureUrl is null)
            {
                return Failure($"最新版本 {latestVersion} 尚未提供更新清单。");
            }

            byte[] manifestBytes = await DownloadSmallAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
            byte[] signatureBytes = await DownloadSmallAsync(signatureUrl, cancellationToken).ConfigureAwait(false);
            string signature = Encoding.UTF8.GetString(signatureBytes).Trim();
            if (!UpdateSignature.Verify(manifestBytes, signature, _publicKeyBase64))
            {
                return Failure("更新清单签名校验失败，已拒绝本次更新。");
            }

            if (!UpdateManifest.TryParse(manifestBytes, out UpdateManifest? manifest, out string? parseError)
                || manifest is null)
            {
                return Failure(parseError ?? "更新清单解析失败。");
            }
            if (!manifest.TryValidate(latestTag, out string? validationError))
            {
                return Failure(validationError ?? "更新清单内容无效。");
            }
            if (!SemanticVersion.TryParse(manifest.Version, out SemanticVersion? updateVersion)
                || updateVersion is null)
            {
                return Failure("更新清单版本号无效。");
            }
            if (updateVersion.CompareTo(ParseCurrentVersion()) <= 0)
            {
                return new UpdateCheckResult(false, CurrentVersion, updateVersion.ToString(), manifest, null);
            }

            _lastManifest = manifest;
            return new UpdateCheckResult(true, CurrentVersion, updateVersion.ToString(), manifest, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure("更新检查已取消。");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidDataException)
        {
            return Failure($"检查更新失败：{exception.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 下载并暂存最近一次 CheckAsync 验证过的更新。网页不能绕过验签直接提交 URL 或清单。
    /// </summary>
    public async Task<StagedUpdate> DownloadLastAsync(CancellationToken cancellationToken = default)
    {
        UpdateManifest manifest;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            manifest = _lastManifest ?? throw new InvalidOperationException("当前没有已通过验签的更新。");
        }
        finally
        {
            _gate.Release();
        }

        string versionRoot = Path.Combine(_stagingRoot, SanitizeVersion(manifest.Version));
        string packagePath = Path.Combine(versionRoot, manifest.Asset);
        string appRoot = Path.Combine(versionRoot, "app");
        Directory.CreateDirectory(versionRoot);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }
        if (Directory.Exists(appRoot))
        {
            Directory.Delete(appRoot, true);
        }

        string downloadedHash = await DownloadPackageAsync(manifest, packagePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(downloadedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(packagePath);
            throw new InvalidDataException("更新包 SHA-256 校验失败，已丢弃下载文件。");
        }

        ExtractSafely(packagePath, appRoot);
        string executable = ResolvePackagedExecutable(appRoot);
        string? productVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Split('+', 2)[0];
        if (!string.Equals(productVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新包内 Augit.exe 版本为 {productVersion ?? "unknown"}，与清单 {manifest.Version} 不一致。");
        }

        return new StagedUpdate(manifest, Path.GetDirectoryName(executable)!, packagePath);
    }

    private async Task<string> DownloadPackageAsync(
        UpdateManifest manifest,
        string destination,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            manifest.Url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > manifest.Size)
        {
            throw new InvalidDataException("更新包响应大小超过清单大小。");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream target = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumPackageBytes || total > manifest.Size)
            {
                throw new InvalidDataException("更新包超过清单声明的大小。");
            }
            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (total != manifest.Size)
        {
            throw new InvalidDataException($"更新包大小为 {total}，与清单 {manifest.Size} 不一致。");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<byte[]> DownloadSmallAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsGitHubReleaseAsset(url))
        {
            throw new InvalidDataException("更新清单地址不可信。");
        }

        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > MaximumManifestBytes)
        {
            throw new InvalidDataException("更新清单响应过大。");
        }
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream memory = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaximumManifestBytes)
            {
                throw new InvalidDataException("更新清单响应过大。");
            }
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static void ExtractSafely(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        long total = 0;
        int entries = 0;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            entries++;
            if (entries > MaximumArchiveEntries)
            {
                throw new InvalidDataException("更新包条目过多。");
            }
            total += entry.Length;
            if (total > MaximumExtractedBytes)
            {
                throw new InvalidDataException("更新包解压后超过允许大小。");
            }
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包包含越界路径。");
            }
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string ResolvePackagedExecutable(string extractionRoot)
    {
        string direct = Path.Combine(extractionRoot, "Augit.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        string[] candidates = Directory.GetFiles(extractionRoot, "Augit.exe", SearchOption.AllDirectories);
        if (candidates.Length != 1)
        {
            throw new InvalidDataException("更新包必须包含唯一的 Augit.exe。");
        }
        return candidates[0];
    }

    private static string SanitizeVersion(string version)
    {
        string sanitized = new(version.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string? FindAssetUrl(JsonElement release, string name)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (string.Equals(ReadString(asset, "name"), name, StringComparison.Ordinal))
            {
                return ReadString(asset, "browser_download_url");
            }
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsGitHubReleaseAsset(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/Paranoid49/Augit/releases/download/", StringComparison.OrdinalIgnoreCase);

    private SemanticVersion ParseCurrentVersion()
    {
        if (SemanticVersion.TryParse(CurrentVersion, out SemanticVersion? version) && version is not null)
        {
            return version;
        }
        _ = SemanticVersion.TryParse("0.0.0", out SemanticVersion? fallback);
        return fallback!;
    }

    private UpdateCheckResult Failure(string message)
        => new(false, CurrentVersion, null, null, message);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 暂存文件删除失败不覆盖真正的校验错误。
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _gate.Dispose();
    }

}
