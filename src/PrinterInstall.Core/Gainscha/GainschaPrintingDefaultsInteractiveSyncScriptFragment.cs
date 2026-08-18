namespace PrinterInstall.Core.Gainscha;

public static class GainschaPrintingDefaultsInteractiveSyncScriptFragment
{
    public static string BuildInteractiveSyncFunction() =>
        """
    function Invoke-InteractivePrintingDefaultsSync {
        param([string]$PrinterName)
        if (-not ("GainschaInteractivePrintingDefaultsSync" -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class GainschaInteractivePrintingDefaultsSync
{
    private const int DmOutBuffer = 2;
    private const int DmInBuffer = 8;
    private const int PrinterAccessUse = 8;
    private const int PrinterAccessAdminister = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterDefaults
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public int DesiredAccess;
    }

    public static void Sync(string printerName)
    {
        IntPtr hPrinter;
        var defaults = new PrinterDefaults { DesiredAccess = PrinterAccessUse | PrinterAccessAdminister };
        if (!OpenPrinter(printerName, out hPrinter, ref defaults))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenPrinter failed");

        try
        {
            var size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0) throw new InvalidOperationException("DocumentProperties size failed");

            var userDevMode = Marshal.AllocHGlobal(size);
            try
            {
                if (DocumentProperties(IntPtr.Zero, hPrinter, printerName, userDevMode, IntPtr.Zero, DmOutBuffer) < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DocumentProperties read failed");

                if (DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, userDevMode, DmInBuffer) < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DocumentProperties write failed");
            }
            finally { Marshal.FreeHGlobal(userDevMode); }
        }
        finally { ClosePrinter(hPrinter); }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PrinterDefaults pDefault);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);
}
'@ -ErrorAction Stop
        }

        $tempScript = Join-Path $env:TEMP ("PrinterInstall-interactive-defaults-" + [Guid]::NewGuid().ToString('N') + '.ps1')
        $inner = @"
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class GainschaInteractivePrintingDefaultsSync
{
    private const int DmOutBuffer = 2;
    private const int DmInBuffer = 8;
    private const int PrinterAccessUse = 8;
    private const int PrinterAccessAdminister = 4;
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterDefaults { public IntPtr pDatatype; public IntPtr pDevMode; public int DesiredAccess; }
    public static void Sync(string printerName)
    {
        IntPtr hPrinter;
        var defaults = new PrinterDefaults { DesiredAccess = PrinterAccessUse | PrinterAccessAdminister };
        if (!OpenPrinter(printerName, out hPrinter, ref defaults))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenPrinter failed");
        try
        {
            var size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0) throw new InvalidOperationException("DocumentProperties size failed");
            var userDevMode = Marshal.AllocHGlobal(size);
            try
            {
                if (DocumentProperties(IntPtr.Zero, hPrinter, printerName, userDevMode, IntPtr.Zero, DmOutBuffer) < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DocumentProperties read failed");
                if (DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, userDevMode, DmInBuffer) < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DocumentProperties write failed");
            }
            finally { Marshal.FreeHGlobal(userDevMode); }
        }
        finally { ClosePrinter(hPrinter); }
    }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PrinterDefaults pDefault);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);
}
'@
[GainschaInteractivePrintingDefaultsSync]::Sync('$($PrinterName.Replace("'", "''"))')
"@
        try {
            Set-Content -LiteralPath $tempScript -Value $inner -Encoding UTF8
            $proc = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tempScript) -PassThru -WindowStyle Hidden
            if (-not $proc.WaitForExit(30000)) {
                try { $proc.Kill() } catch { }
                throw 'Sync interativo de Padrões de Impressão expirou (30 s).'
            }
            if ($proc.ExitCode -ne 0) {
                throw "Sync interativo de Padrões de Impressão falhou (exit $($proc.ExitCode))."
            }
        } finally {
            if (Test-Path -LiteralPath $tempScript) { Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue }
        }
    }

    function Test-DeployUserHasInteractiveSession {
        param([string]$DeployUser)
        if ([string]::IsNullOrWhiteSpace($DeployUser)) { return $false }
        $account = ($DeployUser -split '[\\@]')[-1]
        $output = cmd.exe /c 'query user' 2>$null
        if (-not $output) { return $false }
        foreach ($line in @($output)) {
            if ($line -notmatch '(?i)active|ativo') { continue }
            if ($line -match ('(?i)\b' + [regex]::Escape($account) + '\b')) { return $true }
        }
        return $false
    }
""";
}
