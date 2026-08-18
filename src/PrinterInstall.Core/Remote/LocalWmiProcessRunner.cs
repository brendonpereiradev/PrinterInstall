namespace PrinterInstall.Core.Remote;

/// <summary>
/// Executa processos via Win32_Process.Create no WMI local (sem credencial alternativa).
/// </summary>
public sealed class LocalWmiProcessRunner
{
    public Task<RemoteProcessResult> RunAsync(
        string commandLine,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var scope = WmiPrinterOperationsCore.CreateLocalScope();
            return WmiProcessRunnerCore.Run(scope, commandLine, timeout, cancellationToken);
        }, cancellationToken);
    }
}
