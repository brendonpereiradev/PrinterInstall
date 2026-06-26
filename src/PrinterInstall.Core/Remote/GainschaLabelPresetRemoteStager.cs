using System.Net;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public static class GainschaLabelPresetRemoteStager
{
    public static Task<(RemoteDriverStagingPaths Paths, string SdsFileName)> StageAsync(
        string host,
        NetworkCredential credential,
        GainschaLabelPreset preset,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var paths = RemoteDriverStagingPaths.Create(host);
            var sdsFileName = GainschaLabelTemplateLoader.TemplateFileName(preset);
            var content = GainschaLabelTemplateLoader.LoadText(preset);

            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            Directory.CreateDirectory(paths.UncRoot);
            File.WriteAllText(
                Path.Combine(paths.UncRoot, sdsFileName),
                content,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return (paths, sdsFileName);
        }, cancellationToken);
    }

    public static Task CleanupAsync(
        string host,
        NetworkCredential credential,
        RemoteDriverStagingPaths paths,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
                if (Directory.Exists(paths.UncRoot))
                    Directory.Delete(paths.UncRoot, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }, cancellationToken);
    }
}
