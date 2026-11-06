using System.Text.Json;
using System.Text.Json.Serialization;

namespace Augit.Infrastructure.Updates;

/// <summary>
/// 已签名的便携版更新清单。清单本身是发布资产，签名文件与清单分开传输；
/// URL、大小和哈希都必须在签名覆盖范围内，不能只信 GitHub API 返回的元数据。
/// </summary>
public sealed record UpdateManifest
{
    public int Schema { get; init; }

    public string Version { get; init; } = "";

    public string Tag { get; init; } = "";

    public string Asset { get; init; } = "";

    public string Url { get; init; } = "";

    public long Size { get; init; }

    public string Sha256 { get; init; } = "";

    public string? NotesUrl { get; init; }

    public string? PublishedAt { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static bool TryParse(ReadOnlySpan<byte> bytes, out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, SerializerOptions);
        }
        catch (JsonException exception)
        {
            error = $"更新清单不是有效 JSON：{exception.Message}";
            return false;
        }

        if (manifest is null)
        {
            error = "更新清单为空。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 校验清单字段与已选择的发布 tag。调用方必须先验证签名，再做这里的内容校验。
    /// </summary>
    public bool TryValidate(string expectedTag, out string? error)
    {
        error = null;
        if (Schema != 1)
        {
            error = $"不支持的更新清单版本：{Schema}。";
            return false;
        }
        if (!SemanticVersion.TryParse(Version, out _))
        {
            error = $"更新版本号无效：{Version}。";
            return false;
        }
        if (!string.Equals(Tag, "v" + Version, StringComparison.Ordinal))
        {
            error = $"更新清单 tag 与版本号不一致：{Tag} / {Version}。";
            return false;
        }
        if (!string.Equals(Tag, expectedTag, StringComparison.Ordinal))
        {
            error = $"更新清单 tag 与发布 tag 不一致：{Tag} / {expectedTag}。";
            return false;
        }
        if (Size is <= 0 or > 200L * 1024 * 1024)
        {
            error = $"更新包大小超出允许范围：{Size}。";
            return false;
        }
        if (!IsSha256(Sha256))
        {
            error = "更新包 SHA-256 格式无效。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Asset) || Asset.Contains('/') || Asset.Contains('\\')
            || !Asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            error = "更新包文件名无效。";
            return false;
        }
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/Paranoid49/Augit/releases/download/", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.EndsWith("/" + Asset, StringComparison.OrdinalIgnoreCase))
        {
            error = "更新包地址不是允许的 GitHub Release HTTPS 地址。";
            return false;
        }

        return true;
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}

/// <summary>
/// 更新签名使用的公钥。私钥只保存在发布流水线的仓库 Secret 中，绝不进入仓库或客户端。
/// </summary>
internal static class UpdateTrust
{
    // ECDSA P-256 SubjectPublicKeyInfo。轮换密钥时必须同时发布一个能验证新公钥的过渡版本。
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+XRC/DPqAQoPdK4k15nw0NIuQ/9bCTJi1I0YUw7WWeaNOtlmWbAg0Dusq8CuNinblCjEye59hZdbI1HHD/EP2w==";
}
