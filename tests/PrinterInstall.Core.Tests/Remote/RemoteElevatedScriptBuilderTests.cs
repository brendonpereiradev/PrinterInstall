using PrinterInstall.Core.Gainscha;

using PrinterInstall.Core.Models;

using PrinterInstall.Core.Remote;



namespace PrinterInstall.Core.Tests.Remote;



public class RemoteElevatedScriptBuilderTests

{

    [Fact]

    public void BuildCreateTcpPortScript_EmitsResultMarker()

    {

        var script = RemoteElevatedScriptBuilder.BuildCreateTcpPortScript(

            "IP_10.0.0.5", "10.0.0.5", 9100, "RAW");

        Assert.Contains("Add-PrinterPort", script, StringComparison.Ordinal);

        Assert.Contains("RESULT>> OK", script, StringComparison.Ordinal);

    }



    [Fact]

    public void BuildAddPrinterScript_EscapesQuotes()

    {

        var script = RemoteElevatedScriptBuilder.BuildAddPrinterScript(

            "Recepção L'Impressora", "Lexmark Universal v4 XL", "IP_10.0.0.5");

        Assert.Contains("Recepção L''Impressora", script, StringComparison.Ordinal);

        Assert.Contains("Add-Printer", script, StringComparison.Ordinal);

    }



    [Fact]
    public void BuildApplyGainschaLabelPresetScript_ImportsTemplateDefaultsAndCleanupThenValidatesDimensions()
    {
        var script = RemoteElevatedScriptBuilder.BuildApplyGainschaLabelPresetScript(
            "Etiquetadora - Teste",
            @"C:\Temp\paciente.sds",
            @"C:\Temp\gainscha-cleanup.sds",
            @"C:\Temp\paciente-defaults.sds",
            @"DOMAIN\deployuser",
            89,
            36,
            "USER (89,0 mm x 36,0 mm)");

        Assert.Contains("Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $templatePath", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-SsdalSettings -SsdalPath $ssdal -PrinterName $printerName -Action import -FilePath $cleanupPath", script, StringComparison.Ordinal);
        Assert.Contains("$expectedWidthMm = 89", script, StringComparison.Ordinal);
        Assert.Contains("$expectedHeightMm = 36", script, StringComparison.Ordinal);
        Assert.Contains("USER (89,0 mm x 36,0 mm)", script, StringComparison.Ordinal);
        Assert.Contains("STEP>> ssdal import template", script, StringComparison.Ordinal);
        Assert.Contains("STEP>> ssdal export preferences", script, StringComparison.Ordinal);
        Assert.Contains("Get-UserFormDimensionsMmFromContent", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/RU SYSTEM", script, StringComparison.Ordinal);
    }



    [Fact]

    public void BuildApplyGainschaLabelPresetScript_FindSsdalUsesExplicitJoinPathChildPath()

    {

        var script = RemoteElevatedScriptBuilder.BuildApplyGainschaLabelPresetScript(

            "Etiquetadora",

            @"C:\Temp\paciente.sds",

            @"C:\Temp\gainscha-cleanup.sds",

            @"C:\Temp\paciente-defaults.sds",

            @"DOMAIN\deployuser",

            89,

            36,

            "USER (89,0 mm x 36,0 mm)");



        Assert.Contains("Join-Path -Path $env:ProgramFiles -ChildPath 'Seagull'", script, StringComparison.Ordinal);

        Assert.DoesNotContain("Join-Path $env:ProgramFiles 'Seagull',", script, StringComparison.Ordinal);

    }



    [Fact]

    public void WrapWithResultHandling_IncludesTryCatch()

    {

        var inner = "Write-Output 'hello'";

        var wrapped = RemoteElevatedScriptBuilder.WrapWithResultHandling(inner);

        Assert.Contains("$ErrorActionPreference = 'Stop'", wrapped, StringComparison.Ordinal);

        Assert.Contains("RESULT>> FAIL", wrapped, StringComparison.Ordinal);

        Assert.Contains("RESULT>> OK", wrapped, StringComparison.Ordinal);

    }

}


