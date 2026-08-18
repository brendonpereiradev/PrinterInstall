using PrinterInstall.Core.Gainscha;



namespace PrinterInstall.Core.Tests.Gainscha;



public class GainschaPrintingDefaultsSyncScriptFragmentTests

{

    [Fact]

    public void BuildValidateFunctions_ExportsOnlyValidationWithoutRegistryOrWin32()

    {

        var script = GainschaPrintingDefaultsSyncScriptFragment.BuildValidateFunctions(89, 36);



        Assert.Contains("Test-GainschaPrintingDefaultsFromExport", script, StringComparison.Ordinal);

        Assert.Contains("Get-UserFormDimensionsMmFromContent", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Sync-GainschaPrintingDefaults", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Set-ItemProperty", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Add-Type", script, StringComparison.Ordinal);

        Assert.DoesNotContain("DocumentProperties", script, StringComparison.Ordinal);

    }

}


