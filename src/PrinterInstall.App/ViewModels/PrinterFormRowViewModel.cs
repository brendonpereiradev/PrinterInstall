using CommunityToolkit.Mvvm.ComponentModel;
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;
namespace PrinterInstall.App.ViewModels;

public partial class PrinterFormRowViewModel : ObservableObject
{
    [ObservableProperty]
    private PrinterBrand _brand = PrinterBrand.Epson;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _printerHostAddress = "";

    [ObservableProperty]
    private GainschaLabelPreset? _gainschaLabelPreset;

    public bool IsGainschaBrand => Brand == PrinterBrand.Gainscha;

    public static IEnumerable<GainschaLabelPreset> GainschaLabelPresetChoices =>
        GainschaLabelPresetCatalog.UiDisplayOrder;
    partial void OnBrandChanged(PrinterBrand value)
    {
        if (value != PrinterBrand.Gainscha)
            GainschaLabelPreset = null;

        OnPropertyChanged(nameof(IsGainschaBrand));
    }

    public static IEnumerable<PrinterBrand> BrandChoices => Enum.GetValues<PrinterBrand>();
}
