using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PrinterInstall.Core.Network;

internal static class PrinterWebCertificatePolicy
{
    // Aplica-se apenas à leitura da identidade no endereço solicitado, sem credenciais ou alterações de confiança no Windows.
    internal static bool IsAllowed(string host, HttpRequestMessage request, X509Certificate2? certificate,
        X509Chain? chain, SslPolicyErrors errors)
    {
        var expectedHost = new UriBuilder("https", host).Uri.IdnHost;
        if (request.Method != HttpMethod.Get || request.RequestUri is not { Scheme: "https", Port: 443 } address ||
            !string.Equals(address.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase))
            return false;
        if (errors == SslPolicyErrors.None)
            return true;
        if (certificate is null || chain is null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            return false;

        // Impressoras usam certificados autoassinados cujo nome costuma ser o número de série, em vez do IP.
        if (!certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData) ||
            DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
            return false;

        var status = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (flags, item) => flags | item.Status);
        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0 && status == X509ChainStatusFlags.NoError)
            return false;
        return (status & ~X509ChainStatusFlags.UntrustedRoot) == 0;
    }
}
