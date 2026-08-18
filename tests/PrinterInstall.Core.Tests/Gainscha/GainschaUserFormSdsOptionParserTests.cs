using PrinterInstall.Core.Gainscha;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaUserFormSdsOptionParserTests
{
    [Fact]
    public void ParseUserFormEntries_PacienteTemplate_ContainsExpectedEntries()
    {
        var template = GainschaLabelTemplateLoader.LoadText(Models.GainschaLabelPreset.Paciente);

        var entries = GainschaUserFormSdsOptionParser.ParseUserFormEntries(template);

        Assert.Contains(entries, e => e.Name == "User Form: Data" && e.RegistryType == GainschaUserFormSdsOptionParser.RegBinary);
        Assert.Contains(entries, e => e.Name == "User Form: Name" && e.RegistryType == GainschaUserFormSdsOptionParser.RegSz);
        Assert.Contains(entries, e => e.Name == "User Form: Label Stock Type" && e.RegistryType == GainschaUserFormSdsOptionParser.RegDword);

        var data = entries.Single(e => e.Name == "User Form: Data").Data;
        Assert.True(GainschaLabelUserFormBinary.TryFindUserFormDimensionsMm(data, 89, 36));
    }
}
