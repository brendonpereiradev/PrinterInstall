using PrinterInstall.Core.Network;
using PrinterInstall.Core.Validation;

if (args.Length != 2 || !Enum.TryParse<PrinterInstall.Core.Models.PrinterBrand>(args[1], true, out var brand))
{
    Console.Error.WriteLine("Uso: dotnet run --project scripts/PrinterIdentityProbe -- <IP-ou-host> <Epson|Lexmark|Brother|Gainscha>");
    return 2;
}

var result = await new NetworkPrinterIdentityService().IdentifyAsync(args[0]);
var check = PrinterCompatibilityValidator.Check(result, brand);
Console.WriteLine($"Endereço: {result.Host}");
Console.WriteLine($"Fabricante: {result.Brand?.ToString() ?? "não identificado"}");
Console.WriteLine($"Modelo: {result.Model ?? "não identificado"}");
Console.WriteLine($"Fonte: {result.Source}");
Console.WriteLine($"Resultado: {check.Message}");
return check.Compatible ? 0 : 1;
