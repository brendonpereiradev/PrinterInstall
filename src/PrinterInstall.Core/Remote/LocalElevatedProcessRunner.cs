namespace PrinterInstall.Core.Remote;

/// <summary>
/// Executa scripts PowerShell no host local, relançando via UAC quando necessário (mesmo padrão do install.ps1).
/// </summary>
public sealed class LocalElevatedProcessRunner
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task RunScriptAsync(
        LocalElevatedStagingPaths staging,
        string scriptContent,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staging);

        var scriptPath = staging.FilePath("apply-label.ps1");
        var logPath = staging.FilePath("apply-label.log");

        try
        {
            var fullScript = WmiPrinterOperationsCore.BuildLocalElevatedScript(logPath, scriptContent);
            await File.WriteAllTextAsync(scriptPath, fullScript, cancellationToken).ConfigureAwait(false);

            var cmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
            var output = await LocalProcessRunner.RunWithOutputAsync(cmd, timeout, cancellationToken)
                .ConfigureAwait(false);
            if (output.Result.TimedOut)
                throw new TimeoutException($"Script local expirou após {timeout}.");

            if (output.Result.ReturnValue != 0 && !LogContainsResultMarker(logPath))
            {
                throw new InvalidOperationException(
                    $"Script local falhou (exit {output.Result.ReturnValue}): " +
                    CombineOutput(output.StandardOutput, output.StandardError));
            }

            await PollForResultAsync(logPath, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(staging.Root);
        }
    }

    private static bool LogContainsResultMarker(string logPath) =>
        File.Exists(logPath) &&
        WmiPrinterOperationsCore.ExtractResultLine(ReadLogSafely(logPath)) is not null;

    private static async Task PollForResultAsync(string logPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var logText = ReadLogSafely(logPath);
            var resultLine = WmiPrinterOperationsCore.ExtractResultLine(logText);
            if (!string.IsNullOrEmpty(resultLine))
            {
                if (string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
                    return;

                var detail = resultLine.StartsWith("RESULT>> FAIL ", StringComparison.Ordinal)
                    ? resultLine["RESULT>> FAIL ".Length..]
                    : resultLine;
                throw new InvalidOperationException(
                    $"Configurar etiqueta Gainscha localmente falhou: {detail}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        var diagnostic = BuildTimeoutDiagnostic(logPath);
        throw new TimeoutException($"Execução local expirou aguardando resultado.{diagnostic}");
    }

    private static string ReadLogSafely(string logPath)
    {
        if (!File.Exists(logPath))
            return "";

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    private static string BuildTimeoutDiagnostic(string logPath)
    {
        if (!File.Exists(logPath))
            return " Nenhum log foi gerado.";

        try
        {
            var tail = ReadLogSafely(logPath);
            if (string.IsNullOrWhiteSpace(tail))
                return " Log vazio.";

            const int maxChars = 500;
            if (tail.Length > maxChars)
                tail = tail[^maxChars..];

            return $" Último log: {tail.Replace('\r', ' ').Replace('\n', ' ')}";
        }
        catch
        {
            return " Log presente mas não pôde ser lido.";
        }
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        var parts = new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(" | ", parts);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }
}
