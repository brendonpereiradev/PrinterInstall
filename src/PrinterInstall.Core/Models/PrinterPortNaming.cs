namespace PrinterInstall.Core.Models;

public static class PrinterPortNaming
{
    public static string BuildPortName(string printerHostAddress, int portNumber)
    {
        var host = printerHostAddress.Trim();
        return portNumber == 9100 ? host : $"{host}_{portNumber}";
    }
}
