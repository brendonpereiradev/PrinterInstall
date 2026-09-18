namespace PrinterInstall.Core.Remote;

/// <summary>
/// Interface para execução de contingência do utilitário schtasks.exe via RPC remoto (/S &lt;host&gt;).
/// </summary>
public interface ISchtasksFallbackRunner
{
    Task<LocalProcessOutput> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Implementação padrão que dispara o schtasks.exe do Windows local apontando para o host remoto.
/// </summary>
public sealed class DefaultSchtasksFallbackRunner : ISchtasksFallbackRunner
{
    public Task<LocalProcessOutput> RunAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return LocalProcessRunner.RunExecutableWithOutputAsync(
            @"C:\Windows\System32\schtasks.exe",
            arguments,
            timeout,
            cancellationToken);
    }
}
