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
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var trimmedHost = host.Trim();
        log?.Report($"Autenticando sessão remota em {trimmedHost} (IPC$)...");

        try
        {
            using (SmbShareConnection.Open(trimmedHost, "IPC$", credential)) { }
            using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential)) { }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não foi possível autenticar sessão SMB em {trimmedHost}. Verifique firewall (445) e permissões de admin.",
                ex);
        }

        try
        {
            var scope = WmiPrinterOperationsCore.CreateRemoteScope(trimmedHost, credential);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM Win32_PrinterDriver"));
            foreach (ManagementObject mo in searcher.Get())
            {
                mo.Dispose();
                break;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"WMI remoto indisponível em {trimmedHost} (RPC 135, firewall WMI-In).",
                ex);
        }

        var paths = RemoteDriverStagingPaths.Create(trimmedHost);
        var probeLogLocal = paths.LocalLogPath("probe.log");
        var probeCmd = $"cmd.exe /c \"{ElevationProbeCommand} > {probeLogLocal} 2>&1\"";

        using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential))
            Directory.CreateDirectory(paths.UncRoot);

        await _processRunner.RunAsync(trimmedHost, credential, probeCmd, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        string probeText;
        using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential))
        {
            var uncProbe = paths.UncLogPath("probe.log");
            probeText = File.Exists(uncProbe) ? await File.ReadAllTextAsync(uncProbe, cancellationToken).ConfigureAwait(false) : string.Empty;
            try { Directory.Delete(paths.UncRoot, recursive: true); } catch { /* best effort */ }
        }

        var requiresElevated = ParseElevationProbeOutput(probeText);
        var session = new RemoteHostSession(trimmedHost, requiresElevated);
        _cache[key] = session;

        if (requiresElevated)
            log?.Report($"Token administrativo filtrado detectado em {trimmedHost} — execução elevada temporária");

        return session;
    }

    public static string NormalizeHostKey(string host) => host.Trim().ToUpperInvariant();

    public static bool ParseElevationProbeOutput(string output)
    {
        foreach (var line in WmiPrinterOperationsCore.SplitLines(output))
        {
            if (line.Contains("ELEVATION_PROBE>> FALSE", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("ELEVATION_PROBE>> TRUE", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
