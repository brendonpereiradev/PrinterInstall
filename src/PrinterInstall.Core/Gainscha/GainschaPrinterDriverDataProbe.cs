using Microsoft.Win32;

namespace PrinterInstall.Core.Gainscha;

internal static class GainschaPrinterDriverDataProbe
{
    private const string PrintersKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    internal static bool TryFindUserFormDimensionsMm(
        string printerQueueName,
        int expectedWidthMm,
        int expectedHeightMm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerQueueName);

        var keyPath = $@"{PrintersKeyPath}\{printerQueueName}\PrinterDriverData";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
        if (key is null)
            return false;

        foreach (var valueName in key.GetValueNames())
        {
            if (valueName is null)
                continue;

            if (key.GetValue(valueName) is not byte[] bytes || bytes.Length == 0)
                continue;

            if (GainschaLabelUserFormBinary.TryFindUserFormDimensionsMm(bytes, expectedWidthMm, expectedHeightMm))
                return true;
        }

        return false;
    }
}
