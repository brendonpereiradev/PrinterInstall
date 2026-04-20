namespace PrinterInstall.Core.Drivers;

public static class PnputilOutputParser
{
    public static string ExtractLastUsefulLine(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return string.Empty;

        var lines = log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed.Trim();
        }
        return string.Empty;
    }
}
