using CommunityToolkit.Mvvm.ComponentModel;
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

    public static IEnumerable<PrinterBrand> BrandChoices => Enum.GetValues<PrinterBrand>();
}
