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

    [ObservableProperty]
    private string _portText = "9100";

    [ObservableProperty]
    private TcpPrinterProtocol _protocol = TcpPrinterProtocol.Raw;

    public static IEnumerable<PrinterBrand> BrandChoices => Enum.GetValues<PrinterBrand>();

    public static IEnumerable<TcpPrinterProtocol> ProtocolChoices => Enum.GetValues<TcpPrinterProtocol>();
}
