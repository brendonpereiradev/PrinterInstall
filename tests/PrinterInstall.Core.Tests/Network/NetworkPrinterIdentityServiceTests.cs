using System.Buffers.Binary;
using System.Text;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class NetworkPrinterIdentityServiceTests
{
    [Fact]
    public void BuildIppRequest_RequestsPrinterMakeAndModel()
    {
        var request = NetworkPrinterIdentityService.BuildIppRequest("http://192.0.2.10:631/ipp/print");
        Assert.Equal(0x0B, request[3]);
        Assert.Contains("printer-make-and-model", Encoding.UTF8.GetString(request));
        Assert.Contains("ipp://192.0.2.10:631/ipp/print", Encoding.UTF8.GetString(request));
    }

    [Fact]
    public void ReadIppMakeAndModel_ReadsAttributeAndRejectsTruncatedResponse()
    {
        using var stream = new MemoryStream();
        stream.Write([2, 0, 0, 0, 0, 0, 0, 1, 4, 0x41]);
        WriteText(stream, "printer-make-and-model");
        WriteText(stream, "Lexmark CX532adwe");
        stream.WriteByte(3);
        var packet = stream.ToArray();

        Assert.Equal("Lexmark CX532adwe", NetworkPrinterIdentityService.ReadIppMakeAndModel(packet));
        Assert.Null(NetworkPrinterIdentityService.ReadIppMakeAndModel(packet.AsSpan(0, packet.Length - 4)));
    }

    [Fact]
    public void ReadIppMakeAndModel_ReadsTextWithLanguage()
    {
        using var stream = new MemoryStream();
        stream.Write([1, 1, 0, 0, 0, 0, 0, 1, 4, 0x35]);
        WriteText(stream, "printer-make-and-model");
        using var value = new MemoryStream();
        WriteText(value, "en");
        WriteText(value, "Brother HL-L5212DW");
        var bytes = value.ToArray();
        Span<byte> size = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(size, checked((ushort)bytes.Length));
        stream.Write(size);
        stream.Write(bytes);
        stream.WriteByte(3);

        Assert.Equal("Brother HL-L5212DW", NetworkPrinterIdentityService.ReadIppMakeAndModel(stream.ToArray()));
    }

    private static void WriteText(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> size = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(size, checked((ushort)bytes.Length));
        stream.Write(size);
        stream.Write(bytes);
    }
}
