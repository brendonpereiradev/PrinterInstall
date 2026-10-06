using System.Collections.Concurrent;
using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class RemoteHostSessionFactory
{
    internal const string ElevationProbeCommand =
        "powershell.exe -NoProfile -Command \"if(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){'ELEVATION_PROBE>> TRUE'}else{'ELEVATION_PROBE>> FALSE'}\"";

    private readonly IRemoteWmiProcessRunner _processRunner;
    private readonly ConcurrentDictionary<string, RemoteHostSession> _cache = new();

    public RemoteHostSessionFactory(IRemoteWmiProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<RemoteHostSession> PrepareAsync(
        string host,
        NetworkCredential credential,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeHostKey(host);
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var trimmedHost = host.Trim();
        log?.Report($"Autenticando sessão remota em {trimmedHost} (IPC$)...");

        try
        {
            using (await SmbShareConnection.OpenAsync(trimmedHost, "IPC$", credential, cancellationToken).ConfigureAwait(false)) { }
            using (await SmbShareConnection.OpenAsync(trimmedHost, "ADMIN$", credential, cancellationToken).ConfigureAwait(false)) { }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Não foi possível acessar IPC$/ADMIN$ em {trimmedHost} com a conta informada. " +
                RemoteAuthenticationDiagnostics.GetGuidance(ex) + " " +
                Logging.DiagnosticLogFormatter.FormatException(ex),
                ex);
        }

        try
        {
            await Task.Run(() =>
            {
                var scope = WmiPrinterOperationsCore.CreateRemoteScope(trimmedHost, credential);
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM Win32_PrinterDriver"));
                foreach (ManagementObject mo in searcher.Get())
                {
                    mo.Dispose();
                    break;
                }
            }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && AccessDeniedDetector.IsAccessDenied(ex))
        {
            log?.Report($"Acesso WMI/DCOM negado em {trimmedHost}; usando tarefa agendada com as credenciais informadas.");
            return RememberElevatedSession(trimmedHost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"WMI remoto indisponível em {trimmedHost} (RPC 135, firewall WMI-In).",
                ex);
        }

        var paths = RemoteDriverStagingPaths.Create(trimmedHost);
        var probeLogLocal = paths.LocalLogPath("probe.log");
        var probeCmd = $"cmd.exe /c \"{ElevationProbeCommand} > {probeLogLocal} 2>&1\"";

        using (await SmbShareConnection.OpenAsync(trimmedHost, "ADMIN$", credential, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(paths.UncRoot);
        }

        var requiresElevated = false;
        try
        {
            var probeResult = await _processRunner.RunAsync(trimmedHost, credential, probeCmd, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);

            if (probeResult.ReturnValue == 0 && !probeResult.TimedOut)
            {
                string probeText;
                using (await SmbShareConnection.OpenAsync(trimmedHost, "ADMIN$", credential, cancellationToken).ConfigureAwait(false))
                {
                    var uncProbe = paths.UncLogPath("probe.log");
                    probeText = File.Exists(uncProbe) ? await File.ReadAllTextAsync(uncProbe, cancellationToken).ConfigureAwait(false) : string.Empty;
                    try { Directory.Delete(paths.UncRoot, recursive: true); } catch { /* best effort */ }
                }

                requiresElevated = ParseElevationProbeOutput(probeText);
            }
            else
            {
                using (await SmbShareConnection.OpenAsync(trimmedHost, "ADMIN$", credential, cancellationToken).ConfigureAwait(false))
                {
                    try { Directory.Delete(paths.UncRoot, recursive: true); } catch { /* best effort */ }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Em caso de exceção no probe, assume falso para permitir a tentativa direta via WMI CIM.
            requiresElevated = false;
        }

        var session = new RemoteHostSession(trimmedHost, requiresElevated);
        _cache[key] = session;

        if (requiresElevated)
            log?.Report($"Token administrativo filtrado detectado em {trimmedHost} — execução elevada temporária");

        return session;
    }

    public static string NormalizeHostKey(string host) => host.Trim().ToUpperInvariant();

    internal bool RequiresElevatedExecution(string host) =>
        _cache.TryGetValue(NormalizeHostKey(host), out var session) && session.RequiresElevatedExecution;

    internal RemoteHostSession RememberElevatedSession(string host)
    {
        var session = _cache.GetOrAdd(NormalizeHostKey(host), _ => new RemoteHostSession(host.Trim(), true));
        session.MarkRequiresElevatedExecution();
        return session;
    }

    public static bool ParseElevationProbeOutput(string output)
    {
        foreach (var line in WmiPrinterOperationsCore.SplitLines(output))
        {
            if (line.Contains("ELEVATION_PROBE>> FALSE", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("ELEVATION_PROBE>> TRUE", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return false;
    }
}
