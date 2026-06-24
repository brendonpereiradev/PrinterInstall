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
            var (createReturn, pid) = WmiProcessRunnerCore.TryStart(scope, commandLine);
            if (createReturn != 0)
                return new RemoteProcessResult(createReturn, null, TimedOut: false);

            if (pid is null)
                return new RemoteProcessResult(1, null, TimedOut: false);

            return WmiProcessRunnerCore.WaitForLocalProcessExit(pid.Value, scope, timeout, cancellationToken);
        }, cancellationToken);
    }
}
