using System.Globalization;
using System.Text;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public static class DirectRawPrinterTestPageBuilder
{
    private const int DotsPerMm = 8;
    private const int TextRotation = 180;

    public static byte[] ForBrand(PrinterBrand brand, string host, GainschaLabelPreset? gainschaLabelPreset = null)
    {
        return brand switch
        {
            PrinterBrand.Gainscha => BuildTspl(host, gainschaLabelPreset ?? GainschaLabelPreset.Paciente),
            PrinterBrand.Epson => BuildPcl5(host),
            PrinterBrand.Lexmark => BuildPcl5(host),
            PrinterBrand.Brother => BuildPcl5(host),
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
    /// Etiquetadoras Gainscha (ex.: GA-2408T) usam TSPL na porta RAW 9100.
    /// </summary>
    private static byte[] BuildTspl(string host, GainschaLabelPreset preset)
    {
        var def = GainschaLabelPresetCatalog.GetDefinition(preset);
        var widthDots = def.WidthMm * DotsPerMm;
        var heightDots = def.HeightMm * DotsPerMm;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var presetLabel = def.UiDisplayName;

        var lines = preset switch
        {
            GainschaLabelPreset.Pulseira => BuildPulseiraLines(host, timestamp, def.WidthMm, def.HeightMm, widthDots, heightDots),
            GainschaLabelPreset.Lote => BuildLoteLines(host, timestamp, def.WidthMm, def.HeightMm, widthDots, heightDots),
            _ => BuildStandardLabelLines(host, timestamp, presetLabel, def.WidthMm, def.HeightMm, widthDots, heightDots)
        };

        return Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    private static IEnumerable<string> BuildHeader(int widthMm, int heightMm)
    {
        yield return $"SIZE {widthMm} mm, {heightMm} mm";
        yield return "GAP 3 mm, 0 mm";
        yield return "SPEED 4";
        yield return "DENSITY 15";
        yield return "DIRECTION 0";
        yield return "REFERENCE 0,0";
        yield return "CLS";
    }

    private static IEnumerable<string> BuildStandardLabelLines(
        string host, string timestamp, string presetLabel,
        int widthMm, int heightMm, int widthDots, int heightDots)
    {
        var margin = 20;
        var boxRight = widthDots - margin;
        var boxBottom = heightDots - margin;
        var x = boxRight - 30;
        const int lineGap = 40;

        foreach (var line in BuildHeader(widthMm, heightMm))
            yield return line;

        yield return $"BOX {boxRight},{boxBottom},{margin},{margin},3";
        yield return $"TEXT {x},{boxBottom - lineGap},\"3\",{TextRotation},1,1,\"TEST\"";
        yield return $"TEXT {x},{boxBottom - lineGap * 2},\"3\",{TextRotation},1,1,\"{EscapeTspl(presetLabel)}\"";
        yield return $"TEXT {x},{boxBottom - lineGap * 3},\"3\",{TextRotation},1,1,\"Printer Install\"";
        yield return $"TEXT {x},{boxBottom - lineGap * 4},\"2\",{TextRotation},1,1,\"Host: {EscapeTspl(host)}\"";
        yield return $"TEXT {x},{boxBottom - lineGap * 5},\"2\",{TextRotation},1,1,\"{EscapeTspl(timestamp)}\"";
        yield return "PRINT 1,1";
    }

    private static IEnumerable<string> BuildPulseiraLines(
        string host, string timestamp, int widthMm, int heightMm, int widthDots, int heightDots)
    {
        // Pulseira: 25mm (largura = 200 dots) × 270mm (comprimento = 2160 dots).
        // Texto rotacionado 90° para correr ao longo do comprimento.
        // Moldura generosa na área nítida (Y=600 até Y=1600) com "Printer Install"
        // perfeitamente centralizado e com escala 1:1 nativa para eliminar ranhuras bitmap.
        const int rotation = 90;
        var margin = 16;
        var boxRight = widthDots - margin;

        var boxTop = 600;
        var boxBottom = 1600;

        // Font 4 nativa (altura 32 dots, centralizado em X=100 -> X=116)
        var textX = 116;

        foreach (var line in BuildHeader(widthMm, heightMm))
            yield return line;

        // Moldura retangular dedicada e espaçosa
        yield return $"BOX {boxRight},{boxBottom},{margin},{boxTop},2";

        // "Printer Install" centralizado dentro da caixa (ocupa Y=920..1280)
        yield return $"TEXT {textX},920,\"4\",{rotation},1,1,\"Printer Install\"";

        yield return "PRINT 1,1";
    }

    private static IEnumerable<string> BuildLoteLines(
        string host, string timestamp, int widthMm, int heightMm, int widthDots, int heightDots)
    {
        // Lote: 2 colunas de 45 mm x 13 mm com gap entre colunas de 3 mm (largura total de 93 mm).
        const int columnCount = 2;
        const int columnGapMm = 3;
        const int columnGapDots = columnGapMm * DotsPerMm;
        var totalWidthMm = (widthMm * columnCount) + columnGapMm;

        foreach (var line in BuildHeader(totalWidthMm, heightMm))
            yield return line;

        const int margin = 6;
        var boxTop = margin;
        var boxBottom = heightDots - margin;

        for (var col = 0; col < columnCount; col++)
        {
            var colOffset = col * (widthDots + columnGapDots);
            var boxLeft = colOffset + margin;
            var boxRight = colOffset + widthDots - margin;
            var textX = boxRight - 10;

            yield return $"BOX {boxRight},{boxBottom},{boxLeft},{boxTop},2";
            yield return $"TEXT {textX},{boxBottom - 18},\"2\",{TextRotation},1,1,\"TEST - Lote\"";
            yield return $"TEXT {textX},{boxBottom - 44},\"1\",{TextRotation},1,1,\"Host: {EscapeTspl(host)}\"";
            yield return $"TEXT {textX},{boxBottom - 68},\"1\",{TextRotation},1,1,\"{EscapeTspl(timestamp)}\"";
        }

        yield return "PRINT 1,1";
    }

    private static string EscapeTspl(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
