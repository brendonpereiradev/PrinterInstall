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
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false })
    {
        Timeout = ProbeTimeout
    };

    private readonly string _community;

    public NetworkPrinterIdentityService()
        : this(Environment.GetEnvironmentVariable("PRINTERINSTALL_SNMP_COMMUNITY") ?? "public")
    {
    }

    public NetworkPrinterIdentityService(string community)
    {
        _community = community;
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
        if (snmp?.Brand is not null && ipp?.Brand is not null && snmp.Brand != ipp.Brand)
            return new(host, null, null, "SNMP/IPP", "Respostas conflitantes sobre o fabricante.", true);

        if (ipp?.ConflictingEvidence == true)
            return ipp;

        return ipp?.IsIdentified == true ? ipp : snmp?.Brand is not null ? snmp :
            ipp?.Brand is not null ? ipp : new(host, null, null, "SNMP/IPP",
                "SNMP e IPP não retornaram fabricante e modelo reconhecidos. Verifique o acesso e a configuração dos protocolos.");
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

    private static async Task<PrinterIdentityResult?> TryIppAsync(string host, CancellationToken ct)
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
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
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
