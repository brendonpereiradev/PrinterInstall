using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed record LocalDriverPackage(
    PrinterBrand Brand,
    string RootFolder,
    string InfFileName,
    string ExpectedDriverName);
