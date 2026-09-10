using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Gainscha;

public sealed class GainschaLabelPreferenceConfigurator : IGainschaLabelPreferenceConfigurator
{
    private static readonly TimeSpan SsdalTimeout = TimeSpan.FromMinutes(2);

    private readonly LocalElevatedProcessRunner _runner;

    public GainschaLabelPreferenceConfigurator()
        : this(new LocalElevatedProcessRunner())
    {
    }

    internal GainschaLabelPreferenceConfigurator(LocalElevatedProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task ApplyAsync(string printerQueueName, GainschaLabelPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);

        var templateText = GainschaLabelTemplateLoader.LoadText(preset);
        GainschaLabelSdsValidator.ValidateEmbeddedTemplate(templateText, preset);

        var staging = LocalElevatedStagingPaths.Create();
        var templateFileName = GainschaLabelTemplateLoader.TemplateFileName(preset);
        var defaultsFileName = GainschaLabelTemplateLoader.DefaultsTemplateFileName(preset);
        var defaultsPath = staging.FilePath(defaultsFileName);
        var templatePath = staging.FilePath(templateFileName);
        var cleanupPath = staging.FilePath(GainschaLabelCleanupImportSdsBuilder.CleanupFileName);

        await SeagullSdsFileWriter.WriteAsync(templatePath, templateText, cancellationToken).ConfigureAwait(false);
        await SeagullSdsFileWriter.WriteAsync(
            defaultsPath,
            GainschaLabelTemplateLoader.LoadDefaultsText(preset),
            cancellationToken).ConfigureAwait(false);
        await SeagullSdsFileWriter.WriteAsync(
            cleanupPath,
            GainschaLabelCleanupImportSdsBuilder.Build(preset),
            cancellationToken).ConfigureAwait(false);

        var def = GainschaLabelPresetCatalog.GetDefinition(preset);
        var deployUser = Environment.UserDomainName.Equals(".", StringComparison.Ordinal)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";
        var script = RemoteElevatedScriptBuilder.BuildApplyGainschaLabelPresetScript(
            printerQueueName,
            templatePath,
            cleanupPath,
            defaultsPath,
            deployUser,
            def.WidthMm,
            def.HeightMm,
            def.DriverStockDisplayName);
        await _runner.RunScriptAsync(staging, script, SsdalTimeout, cancellationToken).ConfigureAwait(false);
    }
}
