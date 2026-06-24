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
        var paths = RemoteDriverStagingPaths.Create(host);
        var taskName = $"PrinterInstall_{paths.StagingId}";
        var scriptLocal = paths.LocalInfPath("task.ps1");
        var logLocal = paths.LocalLogPath("task.log");
        var transcriptWrapper = WrapScriptWithTranscript(scriptContent, logLocal);

        try
        {
            log?.Report("Executando via tarefa agendada elevada (será removida ao concluir)...");

            await _stager.WriteTextFileAsync(host, credential, paths, "task.ps1", transcriptWrapper, cancellationToken)
                .ConfigureAwait(false);

            var runAt = DateTime.Now.AddMinutes(1);
            var createCmd = string.Format(
                CultureInfo.InvariantCulture,
                "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST {2:HH:mm} /SD {2:MM/dd/yyyy} /RU SYSTEM /RL HIGHEST /F",
                taskName,
                scriptLocal,
                runAt);

            var createResult = await _wmiRunner.RunAsync(host, credential, createCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (createResult.ReturnValue != 0)
                throw new InvalidOperationException(
                    $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}). Verifique permissão para criar tarefas agendadas como SYSTEM.");

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
            var logText = await _stager.ReadLogAsync(host, credential, paths, "task.log", cancellationToken)
                .ConfigureAwait(false);
            var resultLine = WmiPrinterOperationsCore.ExtractResultLine(logText);
            if (!string.IsNullOrEmpty(resultLine))
            {
                if (string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
                    return;
                var detail = resultLine.StartsWith("RESULT>> FAIL ", StringComparison.Ordinal)
                    ? resultLine["RESULT>> FAIL ".Length..]
                    : resultLine;
                throw new InvalidOperationException(
                    $"Acesso negado em {host} mesmo com execução elevada temporária. {detail}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Execução elevada expirou em {host} após {timeout}.");
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
