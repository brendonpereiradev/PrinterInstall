using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelPreferenceConfiguratorTests
{
    [Fact]
    public void TemplateResourceNames_ExistForAllPresets()
    {
        var asm = typeof(GainschaLabelTemplateLoader).Assembly;
        foreach (GainschaLabelPreset preset in Enum.GetValues<GainschaLabelPreset>())
        {
            var name = GainschaLabelTemplateLoader.TemplateResourceName(preset);
            Assert.NotNull(asm.GetManifestResourceStream(name));
        }
    }
}
