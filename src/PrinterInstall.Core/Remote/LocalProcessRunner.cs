using System.Diagnostics;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Executa processos no host local com timeout (rename, install.ps1).
/// </summary>
public static class LocalProcessRunner
{
    public static async Task<RemoteProcessResult> RunAsync(string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var output = await RunWithOutputAsync(commandLine, timeout, cancellationToken).ConfigureAwait(false);
        return output.Result;
    }

    public static async Task<LocalProcessOutput> RunWithOutputAsync(string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var process = new Process { StartInfo = CreateStartInfo(commandLine) };
            if (!process.Start())
                return new LocalProcessOutput(new RemoteProcessResult(1, null, TimedOut: false), "", "");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            var pid = (uint)process.Id;
            var deadline = DateTime.UtcNow + timeout;
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    TryKill(process);
                    return new LocalProcessOutput(new RemoteProcessResult(0, pid, TimedOut: true), "", "Timed out.");
                }

                if (!process.WaitForExit(500))
                    continue;

                break;
            }

            Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(5));
            var exitCode = process.HasExited ? (uint)process.ExitCode : 0u;
            return new LocalProcessOutput(
                new RemoteProcessResult(exitCode, pid, TimedOut: false),
                stdout.Result,
                stderr.Result);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo(string commandLine)
    {
        const string powershellPrefix = "powershell.exe ";
        if (commandLine.StartsWith(powershellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = commandLine[powershellPrefix.Length..],
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {commandLine}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
