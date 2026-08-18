using System.Net;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public static class GainschaLabelPresetRemoteStager
{
    public sealed record StageResult(RemoteDriverStagingPaths Paths, string TemplateFileName, string DefaultsTemplateFileName);

    public static Task<StageResult> StageAsync(
        string host,
        NetworkCredential credential,
        GainschaLabelPreset preset,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var paths = RemoteDriverStagingPaths.Create(host);
            var templateFileName = GainschaLabelTemplateLoader.TemplateFileName(preset);
            var defaultsFileName = GainschaLabelTemplateLoader.DefaultsTemplateFileName(preset);
            var templateContent = GainschaLabelTemplateLoader.LoadText(preset);
            var defaultsContent = GainschaLabelTemplateLoader.LoadDefaultsText(preset);
            var cleanupContent = GainschaLabelCleanupImportSdsBuilder.Build(preset);

            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            Directory.CreateDirectory(paths.UncRoot);
            SeagullSdsFileWriter.Write(
                Path.Combine(paths.UncRoot, templateFileName),
                templateContent);
            SeagullSdsFileWriter.Write(
                Path.Combine(paths.UncRoot, defaultsFileName),
                defaultsContent);
            SeagullSdsFileWriter.Write(
                Path.Combine(paths.UncRoot, GainschaLabelCleanupImportSdsBuilder.CleanupFileName),
                cleanupContent);

            return new StageResult(paths, templateFileName, defaultsFileName);
        }, cancellationToken);
    }

    public static string CleanupFileLocalPath(RemoteDriverStagingPaths paths) =>
        paths.LocalInfPath(GainschaLabelCleanupImportSdsBuilder.CleanupFileName);

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
