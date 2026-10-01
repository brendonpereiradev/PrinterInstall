using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class PrinterWebCertificatePolicyTests
{
    [Theory]
    [InlineData("EPSON72F748")]
    [InlineData("LEXMARK")]
    [InlineData("BROTHER")]
    [InlineData("GAINSCHA")]
    public void IsAllowed_ValidSelfSignedPrinterCertificate_AcceptsOnlyReadOfRequestedHost(string commonName)
    {
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var chain = BuildChain(certificate);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://192.0.2.10/");

        Assert.True(PrinterWebCertificatePolicy.IsAllowed("192.0.2.10", request, certificate, chain,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Theory]
    [InlineData("https://192.0.2.99/", "GET")]
    [InlineData("https://192.0.2.10/", "POST")]
    [InlineData("https://192.0.2.10:8443/", "GET")]
    public void IsAllowed_OtherHostOrOperation_DoesNotBypassTlsValidation(string uri, string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);

        Assert.False(PrinterWebCertificatePolicy.IsAllowed("192.0.2.10", request, null, null, SslPolicyErrors.None));
    }

    [Fact]
    public void IsAllowed_ExpiredSelfSignedCertificate_IsRejected()
    {
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=EPSON", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-1));
        using var chain = BuildChain(certificate);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://192.0.2.10/");

        Assert.False(PrinterWebCertificatePolicy.IsAllowed("192.0.2.10", request, certificate, chain,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void IsAllowed_UnknownCaSignedCertificate_IsRejected()
    {
        using var issuerKey = RSA.Create(2048);
        var issuerRequest = new CertificateRequest("CN=Unknown CA", issuerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(2));
        using var deviceKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=EPSON", deviceKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [1, 2, 3, 4]);
        using var chain = BuildChain(certificate);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://192.0.2.10/");

        Assert.False(PrinterWebCertificatePolicy.IsAllowed("192.0.2.10", request, certificate, chain,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private static X509Chain BuildChain(X509Certificate2 certificate)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.Build(certificate);
        return chain;
    }
}
