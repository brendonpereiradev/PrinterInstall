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

    public static async Task<LocalProcessOutput> RunExecutableWithOutputAsync(
        string executablePath,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var process = new Process { StartInfo = CreateExecutableStartInfo(executablePath, arguments) };
        if (!process.Start())
            return new LocalProcessOutput(new RemoteProcessResult(1, null, TimedOut: false), "", "");

        return await WaitForProcessOutputAsync(process, timeout, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<LocalProcessOutput> RunWithOutputAsync(string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(commandLine) };
        if (!process.Start())
            return new LocalProcessOutput(new RemoteProcessResult(1, null, TimedOut: false), "", "");

        return await WaitForProcessOutputAsync(process, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LocalProcessOutput> WaitForProcessOutputAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var pid = (uint)process.Id;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Timeout de segurança ao aguardar encerramento dos streams de saída.
            }

            var exitCode = process.HasExited ? (uint)process.ExitCode : 0u;
            var stdout = stdoutTask.IsCompletedSuccessfully ? await stdoutTask.ConfigureAwait(false) : "";
            var stderr = stderrTask.IsCompletedSuccessfully ? await stderrTask.ConfigureAwait(false) : "";

            return new LocalProcessOutput(
                new RemoteProcessResult(exitCode, pid, TimedOut: false),
                stdout,
                stderr);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            return new LocalProcessOutput(new RemoteProcessResult(0, pid, TimedOut: true), "", "Timed out.");
        }
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
                WorkingDirectory = GetLocalWorkingDirectory(),
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
            WorkingDirectory = GetLocalWorkingDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static ProcessStartInfo CreateExecutableStartInfo(string executablePath, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = GetLocalWorkingDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    /// <summary>
    /// cmd.exe não aceita UNC como pasta atual; quando o app roda de rede, o cwd herdado quebra subprocessos.
    /// </summary>
    private static string GetLocalWorkingDirectory() => Path.GetTempPath();

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
