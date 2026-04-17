namespace PrinterInstall.Core.Validation;

public static class ComputerNameListParser
{
    public static IReadOnlyList<string> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}
