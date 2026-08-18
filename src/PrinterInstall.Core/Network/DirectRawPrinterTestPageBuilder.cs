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
            _ => BuildStandardLabelLines(host, timestamp, presetLabel, def.WidthMm, def.HeightMm, widthDots, heightDots)
        };

        return Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    private static IEnumerable<string> BuildHeader(int widthMm, int heightMm)
    {
        yield return $"SIZE {widthMm} mm, {heightMm} mm";
        yield return "GAP 3 mm, 0 mm";
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
        var margin = 12;
        var boxRight = widthDots - margin;
        var boxBottom = heightDots - margin;

        foreach (var line in BuildHeader(widthMm, heightMm))
            yield return line;

        yield return $"BOX {boxRight},{boxBottom},{margin},{margin},2";
        yield return $"TEXT {boxRight - 8},{boxBottom - 40},\"3\",{TextRotation},1,1,\"TEST\"";
        yield return $"TEXT {boxRight - 8},{boxBottom - 120},\"2\",{TextRotation},1,1,\"Pulseira\"";
        yield return $"TEXT {boxRight - 8},{boxBottom - 200},\"2\",{TextRotation},1,1,\"Printer Install\"";
        yield return $"TEXT {boxRight - 8},{boxBottom - 280},\"2\",{TextRotation},1,1,\"Host: {EscapeTspl(host)}\"";
        yield return $"TEXT {boxRight - 8},{boxBottom - 360},\"2\",{TextRotation},1,1,\"{EscapeTspl(timestamp)}\"";
        yield return "PRINT 1,1";
    }

    private static string EscapeTspl(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
