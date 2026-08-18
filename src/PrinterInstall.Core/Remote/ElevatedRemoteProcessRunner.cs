using System.Globalization;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class ElevatedRemoteProcessRunner
{
    private static readonly TimeSpan SchtasksBootstrapTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IRemoteWmiProcessRunner _wmiRunner;
    private readonly IRemoteDriverFileStager _stager;

    public ElevatedRemoteProcessRunner(IRemoteWmiProcessRunner wmiRunner, IRemoteDriverFileStager stager)
    {
        _wmiRunner = wmiRunner;
        _stager = stager;
    }

    public async Task RunElevatedScriptAsync(
        string host,
        NetworkCredential credential,
        string scriptContent,
        TimeSpan timeout,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        await RunScheduledScriptAsync(
            host,
            credential,
            scriptContent,
            timeout,
            log,
            runAsSystem: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RunElevatedScriptAsUserAsync(
        string host,
        NetworkCredential credential,
        string scriptContent,
        TimeSpan timeout,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        await RunScheduledScriptAsync(
            host,
            credential,
            scriptContent,
            timeout,
            log,
            runAsSystem: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunScheduledScriptAsync(
        string host,
        NetworkCredential credential,
        string scriptContent,
        TimeSpan timeout,
        IProgress<string>? log,
        bool runAsSystem,
        CancellationToken cancellationToken)
    {
        var paths = RemoteDriverStagingPaths.Create(host);
        var taskName = $"PrinterInstall_{paths.StagingId}";
        var scriptLocal = paths.LocalInfPath("task.ps1");
        var logLocal = paths.LocalLogPath("task.log");
        var resultLocal = paths.LocalLogPath("task.result");
        var scriptWithResultFile = AugmentScriptWithResultFile(scriptContent, resultLocal);
        var transcriptWrapper = WrapScriptWithTranscript(scriptWithResultFile, logLocal);

        try
        {
            log?.Report(runAsSystem
                ? "Executando via tarefa agendada elevada (será removida ao concluir)..."
                : "Executando via tarefa agendada como usuário de deploy (será removida ao concluir)...");

            await _stager.WriteTextFileAsync(host, credential, paths, "task.ps1", transcriptWrapper, cancellationToken)
                .ConfigureAwait(false);

            var runAt = DateTime.Now.AddMinutes(1);
            var createCmd = runAsSystem
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST {2:HH:mm} /SD {2:MM/dd/yyyy} /RU SYSTEM /RL HIGHEST /F",
                    taskName,
                    scriptLocal,
                    runAt)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST {2:HH:mm} /SD {2:MM/dd/yyyy} /RU \"{3}\" /RP \"{4}\" /RL HIGHEST /F",
                    taskName,
                    scriptLocal,
                    runAt,
                    SchtasksRunAsFormatter.EscapeCmdArgument(SchtasksRunAsFormatter.FormatRunAsUser(credential)),
                    SchtasksRunAsFormatter.EscapeCmdArgument(credential.Password ?? string.Empty));

            var createResult = await _wmiRunner.RunAsync(host, credential, createCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (createResult.ReturnValue != 0)
            {
                throw new InvalidOperationException(runAsSystem
                    ? $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}). Verifique permissão para criar tarefas agendadas como SYSTEM."
                    : $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}). Verifique permissão para criar tarefa agendada como o usuário de deploy.");
            }

            var runCmd = $"schtasks /Run /TN \"{taskName}\"";
            await _wmiRunner.RunAsync(host, credential, runCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);

            await PollForResultAsync(host, credential, paths, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var deleteCmd = $"schtasks /Delete /TN \"{taskName}\" /F";
            try
            {
                await _wmiRunner.RunAsync(host, credential, deleteCmd, SchtasksBootstrapTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }

            try
            {
                await _stager.CleanupAsync(host, credential, paths, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
    }

    private async Task PollForResultAsync(
        string host,
        NetworkCredential credential,
        RemoteDriverStagingPaths paths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? resultLine = null;
            foreach (var logName in new[] { "task.result", "task.log" })
            {
                try
                {
                    var logText = await _stager.ReadLogAsync(host, credential, paths, logName, cancellationToken)
                        .ConfigureAwait(false);
                    resultLine = WmiPrinterOperationsCore.ExtractResultLine(logText);
                    if (!string.IsNullOrEmpty(resultLine))
                        break;
                }
                catch (IOException)
                {
                    // Transcript ainda aberto no alvo; tenta novamente no proximo ciclo.
                }
            }

            if (!string.IsNullOrEmpty(resultLine))
            {
                if (string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
                    return;
                var detail = resultLine.StartsWith("RESULT>> FAIL ", StringComparison.Ordinal)
                    ? resultLine["RESULT>> FAIL ".Length..]
                    : resultLine;
                throw new InvalidOperationException(
                    $"Execução elevada em {host} falhou: {detail}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Execução elevada expirou em {host} após {timeout}.");
    }

    private static string AugmentScriptWithResultFile(string wrappedScript, string resultPathOnTarget)
    {
        var escapedResult = resultPathOnTarget.Replace("'", "''", StringComparison.Ordinal);
        return wrappedScript
            .Replace(
                "Write-Output 'RESULT>> OK'",
                $"Set-Content -LiteralPath '{escapedResult}' -Value 'RESULT>> OK' -Encoding UTF8 -Force; Write-Output 'RESULT>> OK'",
                StringComparison.Ordinal)
            .Replace(
                "Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)",
                $"Set-Content -LiteralPath '{escapedResult}' -Value ('RESULT>> FAIL ' + $_.Exception.Message) -Encoding UTF8 -Force; Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)",
                StringComparison.Ordinal);
    }

    private static string WrapScriptWithTranscript(string scriptContent, string logPath)
    {
        var escapedLog = logPath.Replace("'", "''", StringComparison.Ordinal);
        return $@"
Start-Transcript -Path '{escapedLog}' -Force | Out-Null
try {{
{scriptContent}
}} finally {{
    Stop-Transcript | Out-Null
}}";
    }
}
