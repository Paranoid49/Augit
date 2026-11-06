using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Augit.Infrastructure.Updates;

namespace Augit.Infrastructure.Tests;

[TestClass]
public sealed class UpdateManifestTests
{
    [TestMethod]
    public void 有效签名通过且篡改内容被拒绝()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = Encoding.UTF8.GetBytes("""{"schema":1,"version":"1.0.0"}""");
        byte[] signature = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        string signatureBase64 = Convert.ToBase64String(signature);

        Assert.IsTrue(UpdateSignature.Verify(payload, signatureBase64, publicKey));
        payload[0] ^= 1;
        Assert.IsFalse(UpdateSignature.Verify(payload, signatureBase64, publicKey));
    }

    [TestMethod]
    public void 清单校验拒绝错误tag和越界地址()
    {
        UpdateManifest manifest = new()
        {
            Schema = 1,
            Version = "1.0.0",
            Tag = "v1.0.0",
            Asset = "Augit-1.0.0-win-x64-portable.zip",
            Url = "https://github.com/Paranoid49/Augit/releases/download/v1.0.0/Augit-1.0.0-win-x64-portable.zip",
            Size = 123,
            Sha256 = new string('a', 64),
        };

        Assert.IsTrue(manifest.TryValidate("v1.0.0", out _));
        Assert.IsFalse(manifest.TryValidate("v1.0.1", out _));
        Assert.IsFalse((manifest with { Url = "https://evil.example/Augit.zip" }).TryValidate("v1.0.0", out _));
        Assert.IsFalse((manifest with { Size = 0 }).TryValidate("v1.0.0", out _));
        Assert.IsFalse((manifest with { Sha256 = "xyz" }).TryValidate("v1.0.0", out _));
    }

    [TestMethod]
    public void 清单解析保留签名字段之外的元数据()
    {
        string json = """
            {"schema":1,"version":"1.0.0","tag":"v1.0.0","asset":"a.zip","url":"https://github.com/Paranoid49/Augit/releases/download/v1.0.0/a.zip","size":12,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","notesUrl":"https://github.com/Paranoid49/Augit/releases/tag/v1.0.0"}
            """;
        Assert.IsTrue(UpdateManifest.TryParse(Encoding.UTF8.GetBytes(json), out UpdateManifest? manifest, out _));
        Assert.IsNotNull(manifest);
        Assert.AreEqual("1.0.0", manifest.Version);
        Assert.AreEqual(12L, manifest.Size);
        Assert.AreEqual("https://github.com/Paranoid49/Augit/releases/tag/v1.0.0", manifest.NotesUrl);
    }
}
