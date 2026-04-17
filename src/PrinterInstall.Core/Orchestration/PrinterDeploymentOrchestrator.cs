using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterDeploymentOrchestrator
{
    private readonly IRemotePrinterOperations _remote;

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote)
    {
        _remote = remote;
    }

    public async Task RunAsync(PrinterDeploymentRequest request, IProgress<DeploymentProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var computer in request.TargetComputerNames)
        {
            try
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ContactingRemote, "Connecting..."));
                var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ValidatingDriver, $"Checking driver (found {drivers.Count})..."));
                var expected = PrinterCatalog.GetExpectedDriverName(request.Brand);
                if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
                {
                    var sample = string.Join(" | ", drivers.Take(10));
                    progress.Report(new DeploymentProgressEvent(
                        computer,
                        TargetMachineState.AbortedDriverMissing,
                        $"Driver not installed: {expected}. Drivers found: [{sample}]"));
                    continue;
                }

                var portName = BuildPortName(request.PrinterHostAddress, request.PortNumber);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Creating port..."));
                var protocol = MapProtocol(request.Protocol);
                await _remote.CreateTcpPrinterPortAsync(computer, request.DomainCredential, portName, request.PrinterHostAddress, request.PortNumber, protocol, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Adding printer..."));
                await _remote.AddPrinterAsync(computer, request.DomainCredential, request.DisplayName, expected, portName, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, "Done"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, Flatten(ex)));
            }
        }
    }

    /// <summary>
    /// Nome da porta segue a convenção padrão do Windows (assistente TCP/IP): o próprio
    /// IP/host. Só se a porta usar um número fora do padrão RAW 9100 é que anexamos
    /// `_<porta>` para a tornar única sem quebrar a consistência do parque.
    /// </summary>
    private static string BuildPortName(string printerHostAddress, int portNumber)
    {
        var host = printerHostAddress.Trim();
        return portNumber == 9100 ? host : $"{host}_{portNumber}";
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message?.Trim();
            if (!string.IsNullOrEmpty(msg))
                messages.Add(msg);
        }
        return string.Join(" | ", messages);
    }

    private static string MapProtocol(TcpPrinterProtocol p) => p switch
    {
        TcpPrinterProtocol.Raw => "RAW",
        TcpPrinterProtocol.Lpr => "LPR",
        TcpPrinterProtocol.Ipp => "IPP",
        _ => "RAW"
    };
}
