namespace PrinterInstall.Core.Gainscha;

public static class SeagullSsdalLocator
{
    private static readonly string[] SearchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    private static readonly string[] SubfolderNames = ["Seagull", "Seagull Scientific"];

    public static string? TryLocate()
    {
        foreach (var root in SearchRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            foreach (var sub in SubfolderNames)
            {
                var basePath = Path.Combine(root, sub);
                if (!Directory.Exists(basePath))
                    continue;

                foreach (var file in Directory.EnumerateFiles(basePath, "ssdal.exe", SearchOption.AllDirectories))
                    return file;
            }
        }

        return null;
    }

    public static string LocateOrThrow() =>
        TryLocate()
        ?? throw new InvalidOperationException(
            "Seagull ssdal.exe not found. Install the Gainscha driver on this machine.");
}
