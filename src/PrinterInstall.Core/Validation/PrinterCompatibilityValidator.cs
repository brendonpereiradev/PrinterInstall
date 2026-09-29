using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Validation;

public static class PrinterCompatibilityValidator
{
    private static readonly IReadOnlyDictionary<PrinterBrand, string[]> ApprovedModels = new Dictionary<PrinterBrand, string[]>
    {
        [PrinterBrand.Epson] = ["M1180", "WF-M5899", "WF-M5799", "WF-C5890", "WF-C5790"],
        [PrinterBrand.Lexmark] = ["CX532ADWE"],
        [PrinterBrand.Brother] = ["HL-L5212DW"],
        [PrinterBrand.Gainscha] = ["GA-2408T"]
    };

    public static (bool Compatible, string Message) Check(PrinterIdentityResult identity, PrinterBrand selectedBrand)
    {
        if (!ApprovedModels.TryGetValue(selectedBrand, out var approvedModels))
            return (false, $"Marca selecionada inválida para a impressora em {identity.Host}.");

        if (identity.ConflictingEvidence)
            return (false, $"Identificação conflitante da impressora em {identity.Host}: {identity.Detail}");

        if (identity.Brand is null)
            return (false, $"Não foi possível identificar a impressora em {identity.Host}: {identity.Detail ?? "fabricante desconhecido"}");

        if (identity.Brand != selectedBrand)
            return (false, $"A impressora {identity.Host} foi identificada como {identity.Brand} {identity.Model}, enquanto o modelo selecionado é {selectedBrand}.");

        if (string.IsNullOrWhiteSpace(identity.Model))
            return (false, $"Não foi possível identificar o modelo da impressora {selectedBrand} em {identity.Host}.");

        if (!approvedModels.Contains(identity.Model, StringComparer.OrdinalIgnoreCase))
            return (false, $"Modelo {identity.Model} em {identity.Host} não homologado para o driver {PrinterCatalog.GetExpectedDriverName(selectedBrand)}.");

        return (true, $"{identity.Brand} {identity.Model} em {identity.Host} identificado via {identity.Source}.");
    }
}
