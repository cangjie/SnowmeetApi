using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SnowmeetApi.Services.Payments;
using wechat_miniapp_base.Models;

namespace SnowmeetApi.Tests;

public class WepayPlatformCertificatesTests : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private readonly X509Certificate2 certificate;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "wepay-cert-tests-" + Guid.NewGuid());

    public WepayPlatformCertificatesTests()
    {
        certificate = new CertificateRequest("CN=WeChat platform test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        Directory.CreateDirectory(directory);
    }

    private object Entry => new { serial_no = certificate.SerialNumber, certificate = certificate.ExportCertificatePem() };
    private WepayKey Key => new() { platform_certificates = JsonConvert.SerializeObject(new[] { Entry }) };

    [Fact]
    public void DatabaseCertificateVerifiesCallbackWithoutDiskFile()
    {
        var manager = WepayPlatformCertificates.CreateManager(Key, certificate.SerialNumber, directory);
        var client = new WechatTenpayClient(new WechatTenpayClientOptions { PlatformCertificateManager = manager });
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce = "test-nonce", body = "{\"event_type\":\"TRANSACTION.SUCCESS\"}";
        string message = $"{timestamp}\n{nonce}\n{body}\n";
        string signature = Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(client.VerifyEventSignature(timestamp, nonce, body, signature, certificate.SerialNumber));
        Assert.False(client.VerifyEventSignature(timestamp, nonce, body + " ", signature, certificate.SerialNumber));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void OverlappingCertificatesRemainSelectableBySerial()
    {
        using var otherRsa = RSA.Create(2048);
        using var other = new CertificateRequest("CN=Next platform certificate", otherRsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(10));
        var key = new WepayKey { platform_certificates = JsonConvert.SerializeObject(new[] {
            Entry, new { serial_no = other.SerialNumber, certificate = other.ExportCertificatePem() } }) };
        var manager = WepayPlatformCertificates.CreateManager(key);
        Assert.Equal(2, manager.AllEntries().Count());
        Assert.Equal(certificate.SerialNumber, manager.GetEntry(certificate.SerialNumber)?.SerialNumber);
        Assert.Equal(other.SerialNumber, manager.GetEntry(other.SerialNumber)?.SerialNumber);
    }

    [Fact]
    public void ConfiguredDatabaseDoesNotFallBackToDiskForUnknownSerial()
    {
        File.WriteAllText(Path.Combine(directory, "ABC123.pem"), certificate.ExportCertificatePem());
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreateManager(Key, "ABC123", directory));
    }

    [Fact]
    public void SerialMustMatchCertificateContents()
    {
        var key = new WepayKey { platform_certificates = JsonConvert.SerializeObject(new[] {
            new { serial_no = "ABC123", certificate = certificate.ExportCertificatePem() } }) };
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreateManager(key));
    }

    [Theory]
    [InlineData("../private")]
    [InlineData("")]
    [InlineData("PUB_KEY_ID_123")]
    public void RejectsInvalidSerialBeforeFileAccess(string serial)
    {
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreateManager(new WepayKey(), serial, directory));
    }

    [Fact]
    public void LegacySingleCertificateStillWorks()
    {
        var manager = WepayPlatformCertificates.CreateManager(new WepayKey { cert = certificate.ExportCertificatePem() }, certificate.SerialNumber, directory);
        Assert.Equal(certificate.SerialNumber, manager.GetEntry(certificate.SerialNumber)?.SerialNumber);
    }

    [Fact]
    public void UnmigratedMerchantCanReadLegacyDiskCertificate()
    {
        File.WriteAllText(Path.Combine(directory, certificate.SerialNumber + ".pem"), certificate.ExportCertificatePem());
        var manager = WepayPlatformCertificates.CreateManager(new WepayKey(), certificate.SerialNumber, directory);
        Assert.Equal(certificate.SerialNumber, manager.GetEntry(certificate.SerialNumber)?.SerialNumber);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void EmptyConfiguredListFailsClosed(string json)
    {
        Assert.Throws<InvalidOperationException>(() => WepayPlatformCertificates.CreateManager(new WepayKey { platform_certificates = json }));
    }

    public void Dispose()
    {
        certificate.Dispose();
        rsa.Dispose();
        Directory.Delete(directory, true);
    }
}
