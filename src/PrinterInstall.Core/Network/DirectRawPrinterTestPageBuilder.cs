using System.Text;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public static class DirectRawPrinterTestPageBuilder
{
    public static byte[] ForBrand(PrinterBrand brand, string host)
    {
        return brand switch
        {
            PrinterBrand.Gainscha => BuildTspl(host),
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
            "Se esta pagina imprimiu, a conectividade desta impressora esta OK."
        };
        var sb = new List<byte>();
        sb.AddRange([0x1B, (byte)'E']); // Reset
        sb.AddRange(Encoding.ASCII.GetBytes(string.Join("\r\n", lines)));
        sb.Add(0x0C); // Form feed
        sb.AddRange([0x1B, (byte)'E']); // Reset
        return sb.ToArray();
    }

    /// <summary>
    /// Etiquetadoras Gainscha (ex.: GA-2408T) usam TSPL na porta RAW 9100, nao ESC/POS.
    /// </summary>
    private static byte[] BuildTspl(string host)
    {
        // Etiqueta fisica: 89 mm (largura) x 36 mm (altura) @ 203 dpi ~ 712 x 288 dots.
        const int labelWidthDots = 712;
        const int labelHeightDots = 288;
        const int margin = 20;
        var boxRight = labelWidthDots - margin;
        var boxBottom = labelHeightDots - margin;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        // Rotation 180: Gainscha GA-2408T ignora DIRECTION de forma inconsistente; rotacionar cada TEXT corrige etiqueta invertida.
        // Com rotation=180, (x,y) e o canto inferior direito do texto (TSPL/TSC).
        const int textRotation = 180;
        var lines = new[]
        {
            "SIZE 89 mm, 36 mm",
            "GAP 3 mm, 0 mm",
            "DIRECTION 0",
            "REFERENCE 0,0",
            "CLS",
            $"BOX {boxRight},{boxBottom},{margin},{margin},3",
            $"TEXT {boxRight - 30},{boxBottom - 25},\"4\",{textRotation},2,2,\"TEST\"",
            $"TEXT {boxRight - 30},{boxBottom - 95},\"3\",{textRotation},1,1,\"Printer Install\"",
            $"TEXT {boxRight - 30},{boxBottom - 135},\"2\",{textRotation},1,1,\"Host: {EscapeTspl(host)}\"",
            $"TEXT {boxRight - 30},{boxBottom - 165},\"2\",{textRotation},1,1,\"{EscapeTspl(timestamp)}\"",
            "PRINT 1,1"
        };
        return Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    private static string EscapeTspl(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
