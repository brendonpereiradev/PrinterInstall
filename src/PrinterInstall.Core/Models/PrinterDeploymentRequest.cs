using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterDeploymentRequest
{
    public required IReadOnlyList<string> TargetComputerNames { get; init; }
    public required PrinterBrand Brand { get; init; }
    public required string DisplayName { get; init; }
    public required string PrinterHostAddress { get; init; }
    public required int PortNumber { get; init; }
    public required TcpPrinterProtocol Protocol { get; init; }
    public required NetworkCredential DomainCredential { get; init; }
}
