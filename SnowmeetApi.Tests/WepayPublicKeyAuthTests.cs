using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Newtonsoft.Json;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SnowmeetApi.Services.Payments;
using wechat_miniapp_base.Models;

namespace SnowmeetApi.Tests;

// 微信支付公钥模式（2026-11-22：万龙租赁换新商户号 1006012310，新商户只有「微信支付公钥」，没有平台证书）。
// 回调（支付、退款）头 Wechatpay-Serial 是公钥 ID（PUB_KEY_ID_…），用 wepay_key.pub_key_id / pub_key 验签；
// 老商户（平台证书）的回调验签保持不变。
public class WepayPublicKeyAuthTests : IDisposable
{
    private const string PubKeyId = "PUB_KEY_ID_0000000000000000000000000000000001";
    private readonly RSA platformRsa = RSA.Create(2048);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wepay-pubkey-tests-" + Guid.NewGuid());

    public WepayPublicKeyAuthTests() { Directory.CreateDirectory(directory); }

    private WepayKey PubKeyMerchant(string platformCertificates = null) => new()
    {
        mch_id = "1006012310",
        pub_key_id = PubKeyId,
        pub_key = platformRsa.ExportSubjectPublicKeyInfoPem(),
        platform_certificates = platformCertificates
    };

    private static string Sign(RSA rsa, string timestamp, string nonce, string body)
        => Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{body}\n"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    private WechatTenpayClient ClientFor(WepayKey key, string serial)
    {
        var options = new WechatTenpayClientOptions { MerchantV3Secret = "0123456789abcdef0123456789abcdef" };
        WepayPlatformCertificates.ApplyCallbackAuth(options, key, serial, directory);
        return new WechatTenpayClient(options);
    }

    private const string Body = "{\"id\":\"x\",\"event_type\":\"TRANSACTION.SUCCESS\"}";

    [Fact]
    public void PublicKeyMerchantCallbackIsVerifiedWithThePublicKey()
    {
        var client = ClientFor(PubKeyMerchant(), PubKeyId);
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string sig = Sign(platformRsa, ts, "nonce-1", Body);
        Assert.True(client.VerifyEventSignature(ts, "nonce-1", Body, sig, PubKeyId));
        Assert.Empty(Directory.GetFiles(directory));   // 不读磁盘上的平台证书
    }

    [Fact]
    public void ApplyingSelectsThePublicKeyScheme()
    {
        var options = new WechatTenpayClientOptions();
        WepayPlatformCertificates.ApplyCallbackAuth(options, PubKeyMerchant(), PubKeyId, directory);
        Assert.Equal(PlatformAuthScheme.PublicKey, options.PlatformAuthScheme);
        Assert.Equal(PubKeyId, options.PlatformPublicKeyManager.GetEntry(PubKeyId)?.SerialNumber);
    }

    [Fact]
    public void TamperedBodyOrWrongSignerFailsVerification()
    {
        var client = ClientFor(PubKeyMerchant(), PubKeyId);
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string good = Sign(platformRsa, ts, "n", Body);
        Assert.False(client.VerifyEventSignature(ts, "n", Body + " ", good, PubKeyId));
        Assert.False(client.VerifyEventSignature(ts, "other-nonce", Body, good, PubKeyId));
        using var attacker = RSA.Create(2048);
        Assert.False(client.VerifyEventSignature(ts, "n", Body, Sign(attacker, ts, "n", Body), PubKeyId));
    }

    [Fact]
    public void PublicKeyIdMatchIsCaseInsensitiveAndTrimmed()
    {
        var key = PubKeyMerchant();
        Assert.True(WepayPlatformCertificates.UsesPublicKey(key, PubKeyId.ToLowerInvariant()));
        Assert.True(WepayPlatformCertificates.UsesPublicKey(key, "  " + PubKeyId + " "));
        Assert.False(WepayPlatformCertificates.UsesPublicKey(key, "PUB_KEY_ID_other"));
        Assert.False(WepayPlatformCertificates.UsesPublicKey(key, null));
        Assert.False(WepayPlatformCertificates.UsesPublicKey(key, ""));
    }

    [Fact]
    public void WithoutPublicKeyConfiguredBehaviourIsTheOldCertificateOne()
    {
        // 老商户：没有 pub_key_id / pub_key，序列号再像公钥 ID 也不走公钥
        using var cert = new CertificateRequest("CN=old platform", platformRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var old = new WepayKey { platform_certificates = JsonConvert.SerializeObject(new[] {
            new { serial_no = cert.SerialNumber, certificate = cert.ExportCertificatePem() } }) };
        var options = new WechatTenpayClientOptions();
        WepayPlatformCertificates.ApplyCallbackAuth(options, old, cert.SerialNumber, directory);
        Assert.Equal(PlatformAuthScheme.Certificate, options.PlatformAuthScheme);
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var client = new WechatTenpayClient(options);
        Assert.True(client.VerifyEventSignature(ts, "n", Body, Sign(platformRsa, ts, "n", Body), cert.SerialNumber));
        Assert.False(WepayPlatformCertificates.UsesPublicKey(old, PubKeyId));
    }

    [Fact]
    public void PublicKeyMerchantWithPlatformCertificatesStillVerifiesCertificateSerials()
    {
        // 切换期间微信可能还用平台证书签名：序列号是证书的走证书，是公钥 ID 的走公钥
        using var certRsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=platform", certRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var key = PubKeyMerchant(JsonConvert.SerializeObject(new[] {
            new { serial_no = cert.SerialNumber, certificate = cert.ExportCertificatePem() } }));
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        Assert.True(ClientFor(key, cert.SerialNumber).VerifyEventSignature(ts, "n", Body, Sign(certRsa, ts, "n", Body), cert.SerialNumber));
        Assert.True(ClientFor(key, PubKeyId).VerifyEventSignature(ts, "n", Body, Sign(platformRsa, ts, "n", Body), PubKeyId));
        // 公钥签的名，却声称是证书序列号：验不过
        Assert.False(ClientFor(key, cert.SerialNumber).VerifyEventSignature(ts, "n", Body, Sign(platformRsa, ts, "n", Body), cert.SerialNumber));
    }

    [Fact]
    public void UnknownSerialForAPublicKeyOnlyMerchantFailsClosed()
    {
        // 只配了公钥、没有平台证书：别的序列号不能被当成磁盘文件名去读，直接拒绝
        var options = new WechatTenpayClientOptions();
        Assert.ThrowsAny<Exception>(() => WepayPlatformCertificates.ApplyCallbackAuth(options, PubKeyMerchant(),
            "ABCDEF0123456789", directory));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PUB_KEY_ID_bad id")]
    [InlineData("../PUB_KEY_ID_1")]
    public void InvalidPublicKeyIdIsRejected(string id)
    {
        var key = PubKeyMerchant();
        key.pub_key_id = id;
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreatePublicKeyManager(key));
    }

    [Theory]
    [InlineData("not a pem")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public void InvalidPublicKeyPemIsRejected(string pem)
    {
        var key = PubKeyMerchant();
        key.pub_key = pem;
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreatePublicKeyManager(key));
    }

    [Fact]
    public void OnlyTheIdOrOnlyTheKeyMeansPublicKeyModeIsNotConfigured()
    {
        var idOnly = new WepayKey { pub_key_id = PubKeyId };
        var keyOnly = new WepayKey { pub_key = platformRsa.ExportSubjectPublicKeyInfoPem() };
        Assert.False(WepayPlatformCertificates.UsesPublicKey(idOnly, PubKeyId));
        Assert.False(WepayPlatformCertificates.UsesPublicKey(keyOnly, PubKeyId));
    }

    public void Dispose()
    {
        platformRsa.Dispose();
        Directory.Delete(directory, true);
    }
}
