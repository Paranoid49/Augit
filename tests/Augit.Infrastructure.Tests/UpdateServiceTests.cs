using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Augit.Infrastructure.Updates;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    public async Task 检查并下载已签名的更新()
    {
        using TemporaryDirectory temporary = new();
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
        string version = CurrentTestProductVersion();
        byte[] zip = CreatePortableZip();
        string tag = $"v{version}";
        string asset = $"Augit-{version}-win-x64-portable.zip";
        string manifestJson = CreateManifestJson(version, tag, asset, zip);
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        string signature = Convert.ToBase64String(signing.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        string manifestUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json";
        string signatureUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json.sig";
        string assetUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/{asset}";
        string feed = CreateFeed(tag);
        using HttpClient http = new(new RouteHandler(new Dictionary<string, byte[]>
        {
            [UpdateService.ReleasesFeedUrl] = Encoding.UTF8.GetBytes(feed),
            [manifestUrl] = manifestBytes,
            [signatureUrl] = Encoding.UTF8.GetBytes(signature + "\n"),
            [assetUrl] = zip,
        }));
        using UpdateService service = new("0.9.0", temporary.GetPath("updates"), http, publicKey);

        UpdateCheckResult check = await service.CheckAsync();

        Assert.IsNull(check.Error);
        Assert.IsTrue(check.Available);
        Assert.AreEqual(version, check.Version);
        StagedUpdate staged = await service.DownloadLastAsync();
        Assert.IsTrue(File.Exists(Path.Combine(staged.AppDirectory, "Augit.exe")));
        Assert.IsTrue(File.Exists(staged.PackagePath));
    }

    [TestMethod]
    public async Task 签名被篡改时拒绝更新()
    {
        using TemporaryDirectory temporary = new();
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
        string version = CurrentTestProductVersion();
        byte[] zip = CreatePortableZip();
        string tag = $"v{version}";
        string asset = $"Augit-{version}-win-x64-portable.zip";
        byte[] manifestBytes = Encoding.UTF8.GetBytes(CreateManifestJson(version, tag, asset, zip));
        string manifestUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json";
        string signatureUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json.sig";
        string feed = CreateFeed(tag);
        using HttpClient http = new(new RouteHandler(new Dictionary<string, byte[]>
        {
            [UpdateService.ReleasesFeedUrl] = Encoding.UTF8.GetBytes(feed),
            [manifestUrl] = manifestBytes,
            [signatureUrl] = Encoding.UTF8.GetBytes(Convert.ToBase64String(new byte[64]) + "\n"),
        }));
        using UpdateService service = new("0.9.0", temporary.GetPath("updates"), http, publicKey);

        UpdateCheckResult check = await service.CheckAsync();

        Assert.IsFalse(check.Available);
        Assert.IsNotNull(check.Error);
        Assert.IsTrue(check.Error.Contains("签名", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task 当前版本不低于清单时不提供更新()
    {
        using TemporaryDirectory temporary = new();
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
        string version = CurrentTestProductVersion();
        byte[] zip = CreatePortableZip();
        string tag = $"v{version}";
        string asset = $"Augit-{version}-win-x64-portable.zip";
        byte[] manifestBytes = Encoding.UTF8.GetBytes(CreateManifestJson(version, tag, asset, zip));
        string signature = Convert.ToBase64String(signing.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        string manifestUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json";
        string signatureUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json.sig";
        string feed = CreateFeed(tag);
        using HttpClient http = new(new RouteHandler(new Dictionary<string, byte[]>
        {
            [UpdateService.ReleasesFeedUrl] = Encoding.UTF8.GetBytes(feed),
            [manifestUrl] = manifestBytes,
            [signatureUrl] = Encoding.UTF8.GetBytes(signature + "\n"),
        }));
        using UpdateService service = new(version, temporary.GetPath("updates"), http, publicKey);

        UpdateCheckResult check = await service.CheckAsync();

        Assert.IsNull(check.Error);
        Assert.IsFalse(check.Available);
    }

    [TestMethod]
    public async Task 发布源按语义版本选择最高版本而不是第一条()
    {
        using TemporaryDirectory temporary = new();
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo());
        string newerVersion = "9.9.9";
        string tag = $"v{newerVersion}";
        byte[] zip = CreatePortableZip();
        byte[] manifestBytes = Encoding.UTF8.GetBytes(CreateManifestJson(
            newerVersion,
            tag,
            $"Augit-{newerVersion}-win-x64-portable.zip",
            zip));
        string signature = Convert.ToBase64String(signing.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        string manifestUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json";
        string signatureUrl = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/update.json.sig";
        using HttpClient http = new(new RouteHandler(new Dictionary<string, byte[]>
        {
            [UpdateService.ReleasesFeedUrl] = Encoding.UTF8.GetBytes(CreateFeed("v0.8.0", tag, "v1.2.3")),
            [manifestUrl] = manifestBytes,
            [signatureUrl] = Encoding.UTF8.GetBytes(signature + "\n"),
        }));
        using UpdateService service = new("0.9.0", temporary.GetPath("updates"), http, publicKey);

        UpdateCheckResult check = await service.CheckAsync();

        Assert.IsNull(check.Error);
        Assert.IsTrue(check.Available);
        Assert.AreEqual(newerVersion, check.Version);
    }

    [TestMethod]
    public async Task 发布源限流时给出可读提示而不是原始Http错误()
    {
        using TemporaryDirectory temporary = new();
        using HttpClient http = new(new RouteHandler(
            new Dictionary<string, byte[]>(),
            new Dictionary<string, HttpStatusCode>
            {
                [UpdateService.ReleasesFeedUrl] = HttpStatusCode.Forbidden,
            }));
        using UpdateService service = new("0.9.0", temporary.GetPath("updates"), http, "unused");

        UpdateCheckResult check = await service.CheckAsync();

        Assert.IsFalse(check.Available);
        Assert.IsNotNull(check.Error);
        Assert.IsTrue(check.Error.Contains("限制", StringComparison.Ordinal));
        Assert.IsFalse(check.Error.Contains("Response status code", StringComparison.Ordinal));
    }

    private static string CreateFeed(params string[] tags)
        => "<feed xmlns=\"http://www.w3.org/2005/Atom\">"
            + string.Concat(tags.Select(tag =>
                $"<entry><link rel=\"alternate\" type=\"text/html\" href=\"https://github.com/Paranoid49/Augit/releases/tag/{tag}\"/></entry>"))
            + "</feed>";

    private static string CurrentTestProductVersion()
    {
        string file = typeof(UpdateServiceTests).Assembly.Location;
        string product = FileVersionInfo.GetVersionInfo(file).ProductVersion ?? throw new InvalidOperationException("测试程序集缺少 ProductVersion。");
        return product.Split('+', 2)[0];
    }

    private static byte[] CreatePortableZip()
    {
        byte[] executable = File.ReadAllBytes(typeof(UpdateServiceTests).Assembly.Location);
        using MemoryStream memory = new();
        using (ZipArchive archive = new(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("Augit/Augit.exe", CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(executable);
        }
        return memory.ToArray();
    }

    private static string CreateManifestJson(string version, string tag, string asset, byte[] zip)
    {
        string hash = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            schema = 1,
            version,
            tag,
            asset,
            url = $"https://github.com/Paranoid49/Augit/releases/download/{tag}/{asset}",
            size = zip.Length,
            sha256 = hash,
            notesUrl = $"https://github.com/Paranoid49/Augit/releases/tag/{tag}",
        });
    }

    private sealed class RouteHandler(
        Dictionary<string, byte[]> routes,
        Dictionary<string, HttpStatusCode>? failures = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? "";
            if (failures is not null && failures.TryGetValue(url, out HttpStatusCode status))
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }
            if (!routes.TryGetValue(url, out byte[]? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }
}
