using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Network;

/// <summary>Lê a identidade do dispositivo sem enviar trabalho de impressão nem alterar o equipamento.</summary>
public sealed class NetworkPrinterIdentityService : IPrinterIdentityService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = ProbeTimeout
    };

    private readonly string _community;
    private readonly HttpClient _http;
    private readonly HttpClient? _webHttp;

    public NetworkPrinterIdentityService()
        : this(Environment.GetEnvironmentVariable("PRINTERINSTALL_SNMP_COMMUNITY") ?? "public")
    {
    }

    public NetworkPrinterIdentityService(string community)
        : this(community, Http, null)
    {
    }

    internal NetworkPrinterIdentityService(string community, HttpClient http)
        : this(community, http, http)
    {
    }

    private NetworkPrinterIdentityService(string community, HttpClient http, HttpClient? webHttp)
    {
        _community = community;
        _http = http;
        _webHttp = webHttp;
    }

    public async Task<PrinterIdentityResult> IdentifyAsync(string host, CancellationToken cancellationToken = default)
    {
        host = host?.Trim() ?? "";
        if (!PrinterHostValidator.IsValidHostAddress(host))
            return new(host, null, null, "Rede", "Endereço IP ou nome de host inválido.");

        cancellationToken.ThrowIfCancellationRequested();
        PrinterIdentityResult? snmp = null;
        if (!string.IsNullOrWhiteSpace(_community))
            snmp = await TrySnmpAsync(host, cancellationToken).ConfigureAwait(false);

        if (snmp?.ConflictingEvidence == true)
            return snmp;

        if (snmp?.IsIdentified == true)
            return snmp;

        var ipp = await TryIppAsync(host, cancellationToken).ConfigureAwait(false);
        var identity = ReconcileEvidence(host, snmp, ipp);
        if (identity?.ConflictingEvidence == true || identity?.IsIdentified == true)
            return identity;

        var web = await TryWebAsync(host, cancellationToken).ConfigureAwait(false);
        identity = ReconcileEvidence(host, identity, web);
        return identity?.Brand is not null || identity?.ConflictingEvidence == true ? identity :
            new(host, null, null, "SNMP/IPP/HTTP",
                "Não foi possível obter fabricante e modelo por SNMP, IPP ou pela interface web. Verifique o acesso à impressora e a configuração dos protocolos.");
    }

    private static PrinterIdentityResult? ReconcileEvidence(string host, PrinterIdentityResult? first, PrinterIdentityResult? second)
    {
        if (first?.ConflictingEvidence == true || second is null)
            return first;
        if (second.ConflictingEvidence || first is null)
            return second;

        if (first.Brand is not null && second.Brand is not null && first.Brand != second.Brand)
            return new(host, null, null, $"{first.Source}/{second.Source}", "Respostas conflitantes sobre o fabricante.", true);
        if (first.Model is not null && second.Model is not null &&
            !string.Equals(first.Model, second.Model, StringComparison.OrdinalIgnoreCase))
            return new(host, null, null, $"{first.Source}/{second.Source}", "Respostas conflitantes sobre o modelo.", true);

        return second.IsIdentified || first.Brand is null && second.Brand is not null ? second : first;
    }

    private async Task<PrinterIdentityResult?> TrySnmpAsync(string host, CancellationToken ct)
    {
        try
        {
            IPAddress address;
            if (!IPAddress.TryParse(host, out address!))
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct).WaitAsync(ProbeTimeout, ct).ConfigureAwait(false);
                address = addresses.First(a => a.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork
                    or System.Net.Sockets.AddressFamily.InterNetworkV6);
            }

            var endpoint = new IPEndPoint(address, 161);
            var community = new OctetString(_community);
            var values = await Messenger.GetAsync(VersionCode.V2, endpoint, community,
                [new Variable(new ObjectIdentifier("1.3.6.1.2.1.1.1.0"))])
                .WaitAsync(ProbeTimeout, ct).ConfigureAwait(false);

            PrinterIdentityResult? best = null;
            foreach (var value in values)
            {
                var candidate = PrinterIdentityParser.Parse(host, value.Data.ToString(), "SNMP sysDescr");
                if (candidate.ConflictingEvidence)
                    return candidate;
                if (candidate.IsIdentified)
                    return candidate;
                if (candidate.Brand is not null)
                    best = candidate;
            }

            // O índice do dispositivo de impressão é descoberto percorrendo a coluna hrDeviceDescr.
            try
            {
                var deviceDescriptions = new List<Variable>();
                await Messenger.WalkAsync(VersionCode.V2, endpoint, community,
                    new ObjectIdentifier("1.3.6.1.2.1.25.3.2.1.3"), deviceDescriptions, WalkMode.WithinSubtree)
                    .WaitAsync(ProbeTimeout, ct).ConfigureAwait(false);
                foreach (var value in deviceDescriptions.Take(32))
                {
                    var candidate = PrinterIdentityParser.Parse(host, value.Data.ToString(), "SNMP hrDeviceDescr");
                    if (candidate.ConflictingEvidence)
                        return candidate;
                    if (best?.Brand is not null && candidate.Brand is not null && best.Brand != candidate.Brand)
                        return new(host, null, null, "SNMP", "Descrições SNMP apresentam fabricantes conflitantes.", true);
                    if (best?.Model is not null && candidate.Model is not null &&
                        !string.Equals(best.Model, candidate.Model, StringComparison.OrdinalIgnoreCase))
                        return new(host, null, null, "SNMP", "Descrições SNMP apresentam modelos conflitantes.", true);
                    if (candidate.Brand is not null && (best?.Model is null || candidate.Model is not null))
                        best = candidate;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // sysDescr ainda pode fornecer o fabricante quando hrDeviceDescr não estiver disponível.
            }

            return best;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<PrinterIdentityResult?> TryIppAsync(string host, CancellationToken ct)
    {
        var authority = IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]" : host;
        string[] endpoints =
        [
            $"http://{authority}:631/ipp/print",
            $"http://{authority}:631/printers/print",
            $"http://{authority}/ipp/print",
            $"https://{authority}/ipp/print"
        ];

        PrinterIdentityResult? best = null;
        foreach (var endpoint in endpoints)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Content = new ByteArrayContent(BuildIppRequest(endpoint));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .WaitAsync(ProbeTimeout, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 16384)
                    continue;
                var bytes = await response.Content.ReadAsByteArrayAsync(ct).WaitAsync(ProbeTimeout, ct).ConfigureAwait(false);
                if (bytes.Length > 16384)
                    continue;
                var model = ReadIppMakeAndModel(bytes);
                if (model is null)
                    continue;
                var candidate = PrinterIdentityParser.Parse(host, model, "IPP printer-make-and-model");
                if (candidate.ConflictingEvidence)
                    return candidate;
                if (candidate.IsIdentified)
                    return candidate;
                if (candidate.Brand is not null)
                    best = candidate;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Tenta o próximo endpoint IPP conhecido.
            }
        }

        return best;
    }

    private async Task<PrinterIdentityResult?> TryWebAsync(string host, CancellationToken ct)
    {
        using var ownedClient = _webHttp is null ? CreatePrinterWebClient(host) : null;
        var client = _webHttp ?? ownedClient!;
        PrinterIdentityResult? best = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scheme in new[] { "http", "https" })
        {
            var root = new UriBuilder(scheme, host).Uri;
            var pending = new Queue<Uri>();
            pending.Enqueue(root);
            for (var pages = 0; pages < 6 && pending.TryDequeue(out var address);)
            {
                ct.ThrowIfCancellationRequested();
                if (!visited.Add(address.AbsoluteUri))
                    continue;
                pages++;
                var page = await TryWebPageAsync(host, address, client, ct).ConfigureAwait(false);
                if (page is null)
                    continue;
                visited.Add(page.Address.AbsoluteUri);
                best = ReconcileEvidence(host, best, page.Identity);
                if (best?.ConflictingEvidence == true || best?.IsIdentified == true)
                    return best;

                if (page.HasGainschaInfoRequest)
                {
                    var info = await TryGainschaWebInfoAsync(host, new Uri(page.Address, "/updata_message?"), client, ct)
                        .ConfigureAwait(false);
                    best = ReconcileEvidence(host, best, info);
                    if (best?.ConflictingEvidence == true || best?.IsIdentified == true)
                        return best;
                }

                foreach (var path in page.Navigation)
                    if (Uri.TryCreate(page.Address, path, out var destination) && IsSamePrinterEndpoint(root, destination))
                        pending.Enqueue(destination);

                // As duas gerações do Web Config atendem a todos os modelos Epson do catálogo.
                if (page.Identity.Brand == PrinterBrand.Epson)
                    foreach (var path in new[] { "/PRESENTATION/ADVANCED/COMMON/TOP", "/PRESENTATION/HTML/TOP/INDEX.HTML" })
                        pending.Enqueue(new Uri(page.Address, path));
            }
        }

        return best;
    }

    private static HttpClient CreatePrinterWebClient(string host) => new(new HttpClientHandler
    {
        UseProxy = false,
        UseCookies = false,
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
            PrinterWebCertificatePolicy.IsAllowed(host, request, certificate, chain, errors)
    }) { Timeout = ProbeTimeout };

    private static bool IsSamePrinterEndpoint(Uri origin, Uri destination) =>
        string.Equals(destination.IdnHost, origin.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        destination.Scheme is "http" or "https" && destination.Port is 80 or 443 &&
        string.IsNullOrEmpty(destination.UserInfo) && string.IsNullOrEmpty(destination.Fragment);

    private sealed record WebIdentityPage(PrinterIdentityResult Identity, Uri Address, IReadOnlyList<string> Navigation, bool HasGainschaInfoRequest);
    private sealed record PrinterWebResponse(Uri Address, string Content, string? MediaType);

    private async Task<WebIdentityPage?> TryWebPageAsync(string host, Uri endpoint, HttpClient client, CancellationToken ct)
    {
        var response = await ReadPrinterWebResponseAsync(endpoint, client, 128 * 1024, ct).ConfigureAwait(false);
        if (response is null || response.MediaType is not null &&
            !string.Equals(response.MediaType, "text/html", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(response.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            return null;

        var source = response.Address.Scheme == "https" ? "HTTPS Web Config" : "HTTP Web Config";
        try
        {
            return new(PrinterWebIdentityParser.Parse(host, response.Content, source), response.Address,
                PrinterWebIdentityParser.GetNavigationTargets(response.Content), GainschaWebIdentityParser.HasPrinterInfoRequest(response.Content));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private async Task<PrinterIdentityResult?> TryGainschaWebInfoAsync(string host, Uri endpoint, HttpClient client, CancellationToken ct)
    {
        var response = await ReadPrinterWebResponseAsync(endpoint, client, 8192, ct).ConfigureAwait(false);
        if (response is null || response.MediaType is not null &&
            !string.Equals(response.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
            return null;
        var source = response.Address.Scheme == "https" ? "HTTPS Gainscha Printer Info" : "HTTP Gainscha Printer Info";
        return GainschaWebIdentityParser.Parse(host, response.Content, source);
    }

    private static async Task<PrinterWebResponse?> ReadPrinterWebResponseAsync(Uri endpoint, HttpClient client, int maxBytes, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var destination = new Uri(endpoint, location);
                    if (!IsSamePrinterEndpoint(endpoint, destination))
                        return null;
                    endpoint = destination;
                    continue;
                }

                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
                    return null;

                using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var page = new MemoryStream();
                var buffer = new byte[4096];
                int count;
                while ((count = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (page.Length + count > maxBytes)
                        return null;
                    page.Write(buffer, 0, count);
                }

                return new(endpoint, Encoding.UTF8.GetString(page.ToArray()), response.Content.Headers.ContentType?.MediaType);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Falha HTTP não libera a instalação; tenta a próxima interface e preserva outras evidências.
        }

        return null;
    }

    internal static byte[] BuildIppRequest(string uri)
    {
        using var stream = new MemoryStream();
        stream.Write([1, 1, 0, 11, 0, 0, 0, 1, 1]); // IPP 1.1, Get-Printer-Attributes, requisição 1, grupo de operação
        WriteAttribute(stream, 0x47, "attributes-charset", "utf-8");
        WriteAttribute(stream, 0x48, "attributes-natural-language", "en");
        WriteAttribute(stream, 0x45, "printer-uri", uri.Replace("http://", "ipp://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "ipps://", StringComparison.OrdinalIgnoreCase));
        WriteAttribute(stream, 0x44, "requested-attributes", "printer-make-and-model");
        stream.WriteByte(3);
        return stream.ToArray();
    }

    private static void WriteAttribute(Stream stream, byte tag, string name, string value)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[2];
        stream.WriteByte(tag);
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nameBytes.Length));
        stream.Write(length);
        stream.Write(nameBytes);
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)valueBytes.Length));
        stream.Write(length);
        stream.Write(valueBytes);
    }

    internal static string? ReadIppMakeAndModel(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 9 || bytes[0] is not (1 or 2) || BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(2, 2)) >= 0x0100)
            return null;

        var offset = 8;
        var previousName = "";
        while (offset < bytes.Length)
        {
            var tag = bytes[offset++];
            if (tag == 3)
                break;
            if (tag <= 0x0F)
                continue;
            if (offset + 2 > bytes.Length)
                return null;
            var nameLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            offset += 2;
            if (offset + nameLength + 2 > bytes.Length)
                return null;
            var name = nameLength > 0 ? Encoding.ASCII.GetString(bytes.Slice(offset, nameLength)) : previousName;
            previousName = name;
            offset += nameLength;
            var valueLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            offset += 2;
            if (offset + valueLength > bytes.Length)
                return null;
            if (name == "printer-make-and-model" && valueLength is > 0 and <= 512)
            {
                if (tag is 0x41 or 0x42)
                    return Encoding.UTF8.GetString(bytes.Slice(offset, valueLength));
                if (tag == 0x35 && valueLength >= 4)
                {
                    var value = bytes.Slice(offset, valueLength);
                    var languageLength = BinaryPrimitives.ReadUInt16BigEndian(value.Slice(0, 2));
                    var textLengthOffset = 2 + languageLength;
                    if (textLengthOffset + 2 <= value.Length)
                    {
                        var textLength = BinaryPrimitives.ReadUInt16BigEndian(value.Slice(textLengthOffset, 2));
                        if (textLengthOffset + 2 + textLength == value.Length)
                            return Encoding.UTF8.GetString(value.Slice(textLengthOffset + 2, textLength));
                    }
                }
            }
            offset += valueLength;
        }

        return null;
    }
}
