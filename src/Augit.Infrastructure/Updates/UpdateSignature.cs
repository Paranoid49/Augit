using System.Security.Cryptography;

namespace Augit.Infrastructure.Updates;

/// <summary>
/// 发布清单的 ECDSA P-256 签名验证。
///
/// 签名是 detached 文件，覆盖 update.json 的原始 UTF-8 字节；客户端先验签再解析，
/// 这样即使 GitHub API 或下载链路被篡改，攻击者也无法构造新的版本清单。
/// </summary>
public static class UpdateSignature
{
    public static bool Verify(ReadOnlySpan<byte> payload, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(
                payload,
                Convert.FromBase64String(signatureBase64.Trim()),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
