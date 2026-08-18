using System.Globalization;

namespace PrinterInstall.Core.Gainscha;

public static class GainschaPrintingDefaultsSyncScriptFragment
{
    public static string BuildValidateFunctions(int expectedWidthMm, int expectedHeightMm)
    {
        _ = expectedWidthMm;
        _ = expectedHeightMm;

        return """
    function Test-GainschaPrintingDefaultsFromExport {
        param(
            [string]$Content,
            [int]$ExpectedWidthMm,
            [int]$ExpectedHeightMm,
            [string]$ExpectedStockName,
            [string]$ContextLabel
        )
        if ($Content -notmatch [regex]::Escape("Name=$ExpectedStockName")) {
            throw "$ContextLabel stock esperado '$ExpectedStockName' nao encontrado no export."
        }
        $actual = Get-UserFormDimensionsMmFromContent -Content $Content
        if ($null -eq $actual) {
            throw "$ContextLabel export nao contem User Form: Data."
        }
        if ($actual.WidthMm -ne $ExpectedWidthMm -or $actual.HeightMm -ne $ExpectedHeightMm) {
            throw "$ContextLabel dimensao USER incorreta: esperado ${ExpectedWidthMm}x${ExpectedHeightMm} mm, encontrado $($actual.WidthMm)x$($actual.HeightMm) mm."
        }
    }
""";
    }
}
