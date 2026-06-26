using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Gainscha;

public sealed class GainschaLabelPreferenceConfigurator : IGainschaLabelPreferenceConfigurator
{
    private static readonly TimeSpan SsdalTimeout = TimeSpan.FromMinutes(2);

    public async Task ApplyAsync(string printerQueueName, GainschaLabelPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);

        var ssdal = SeagullSsdalLocator.LocateOrThrow();
        var sdsPath = await MaterializeTemplateAsync(preset, cancellationToken).ConfigureAwait(false);

        try
        {
            await RunSsdalSettingsAsync(ssdal, printerQueueName, "reset", sdsPath: null, cancellationToken)
                .ConfigureAwait(false);
            await RunSsdalSettingsAsync(ssdal, printerQueueName, "import", sdsPath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(sdsPath);
        }
    }

    internal static async Task RunSsdalSettingsAsync(
        string ssdalPath,
        string printerQueueName,
        string action,
        string? sdsPath,
        CancellationToken cancellationToken)
    {
        var cmd = action switch
        {
            "reset" => $"{Quote(ssdalPath)} /p {Quote(printerQueueName)} /q settings reset",
            "import" when !string.IsNullOrEmpty(sdsPath) =>
                $"{Quote(ssdalPath)} /p {Quote(printerQueueName)} /q settings import {Quote(sdsPath)}",
            _ => throw new ArgumentException($"Unsupported ssdal action: {action}", nameof(action))
        };

        var output = await LocalProcessRunner.RunWithOutputAsync(cmd, SsdalTimeout, cancellationToken).ConfigureAwait(false);
        if (output.Result.ReturnValue != 0)
        {
            throw new InvalidOperationException(
                $"ssdal settings {action} failed (exit {output.Result.ReturnValue}): {CombineOutput(output.StandardOutput, output.StandardError)}");
        }
    }

    private static async Task<string> MaterializeTemplateAsync(GainschaLabelPreset preset, CancellationToken cancellationToken)
    {
        var text = GainschaLabelTemplateLoader.LoadText(preset);
        var path = Path.Combine(Path.GetTempPath(), "PrinterInstall", $"gainscha-{preset.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.sds");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string CombineOutput(string stdout, string stderr)
    {
        var parts = new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(" | ", parts);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
