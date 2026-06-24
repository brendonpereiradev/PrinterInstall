using System.Text;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public static class DirectRawPrinterTestPageBuilder
{
    public static byte[] ForBrand(PrinterBrand brand, string host)
    {
        return brand switch
        {
            PrinterBrand.Gainscha => BuildEscPos(host),
            PrinterBrand.Epson => BuildPcl5(host),
            PrinterBrand.Lexmark => BuildPcl5(host),
            _ => BuildPcl5(host)
        };
    }

    private static byte[] BuildPcl5(string host)
    {
        var lines = new[]
        {
            "Printer Install - Pagina de teste",
            $"Host: {host}",
            $"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "",
            "Se esta pagina imprimiu, a conectividade RAW/PCL esta OK."
        };
        var sb = new List<byte>();
        sb.AddRange([0x1B, (byte)'E']); // Reset
        sb.AddRange(Encoding.ASCII.GetBytes(string.Join("\r\n", lines)));
        sb.Add(0x0C); // Form feed
        sb.AddRange([0x1B, (byte)'E']); // Reset
        return sb.ToArray();
    }

    private static byte[] BuildEscPos(string host)
    {
        var text = new StringBuilder();
        text.AppendLine("Printer Install - Teste");
        text.AppendLine($"Host: {host}");
        text.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine("");
        text.AppendLine("Conectividade ESC/POS OK.");
        text.AppendLine("");
        var bytes = new List<byte> { 0x1B, (byte)'@' }; // Init
        bytes.AddRange(Encoding.ASCII.GetBytes(text.ToString()));
        bytes.AddRange([0x1B, (byte)'d', 4]); // Feed 4 lines
        return bytes.ToArray();
    }
}
