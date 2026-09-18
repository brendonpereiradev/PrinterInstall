using System.Globalization;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class ElevatedRemoteProcessRunner
{
    private static readonly TimeSpan SchtasksBootstrapTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IRemoteWmiProcessRunner _wmiRunner;
    private readonly IRemoteDriverFileStager _stager;
    private readonly ISchtasksFallbackRunner _schtasksFallbackRunner;

    public ElevatedRemoteProcessRunner(
        IRemoteWmiProcessRunner wmiRunner,
        IRemoteDriverFileStager stager,
        ISchtasksFallbackRunner? schtasksFallbackRunner = null)
    {
        _wmiRunner = wmiRunner;
        _stager = stager;
        _schtasksFallbackRunner = schtasksFallbackRunner ?? new DefaultSchtasksFallbackRunner();
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

        var usedFallback = false;
        try
        {
            log?.Report(runAsSystem
                ? "Executando via tarefa agendada elevada (será removida ao concluir)..."
                : "Executando via tarefa agendada como usuário de deploy (será removida ao concluir)...");

            await _stager.WriteTextFileAsync(host, credential, paths, "task.ps1", transcriptWrapper, cancellationToken)
                .ConfigureAwait(false);

            var createCmd = runAsSystem
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST 00:00 /RU SYSTEM /RL HIGHEST /F",
                    taskName,
                    scriptLocal)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST 00:00 /RU \"{2}\" /RP \"{3}\" /RL HIGHEST /F",
                    taskName,
                    scriptLocal,
                    SchtasksRunAsFormatter.EscapeCmdArgument(SchtasksRunAsFormatter.FormatRunAsUser(credential)),
                    SchtasksRunAsFormatter.EscapeCmdArgument(credential.Password ?? string.Empty));

            var createResult = await _wmiRunner.RunAsync(host, credential, createCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (createResult.ReturnValue != 0)
            {
                log?.Report($"WMI Win32_Process retornou {createResult.ReturnValue} ao tentar iniciar schtasks em {host}. Tentando via RPC remoto (schtasks /S)...");

                var remoteUser = SchtasksRunAsFormatter.FormatRunAsUser(credential);
                var remotePass = credential.Password ?? string.Empty;

                var fallbackCreateArgs = runAsSystem
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "/Create /S \"{0}\" /U \"{1}\" /P \"{2}\" /TN \"{3}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{4}\\\"\" /SC ONCE /ST 00:00 /RU SYSTEM /RL HIGHEST /F",
                        host,
                        SchtasksRunAsFormatter.EscapeCmdArgument(remoteUser),
                        SchtasksRunAsFormatter.EscapeCmdArgument(remotePass),
                        taskName,
                        scriptLocal)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "/Create /S \"{0}\" /U \"{1}\" /P \"{2}\" /TN \"{3}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{4}\\\"\" /SC ONCE /ST 00:00 /RU \"{5}\" /RP \"{6}\" /RL HIGHEST /F",
                        host,
                        SchtasksRunAsFormatter.EscapeCmdArgument(remoteUser),
                        SchtasksRunAsFormatter.EscapeCmdArgument(remotePass),
                        taskName,
                        scriptLocal,
                        SchtasksRunAsFormatter.EscapeCmdArgument(remoteUser),
                        SchtasksRunAsFormatter.EscapeCmdArgument(remotePass));

                var fallbackResult = await _schtasksFallbackRunner.RunAsync(fallbackCreateArgs, SchtasksBootstrapTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (fallbackResult.Result.ReturnValue != 0)
                {
                    var detail = !string.IsNullOrWhiteSpace(fallbackResult.StandardError)
                        ? fallbackResult.StandardError.Trim()
                        : (!string.IsNullOrWhiteSpace(fallbackResult.StandardOutput)
                            ? fallbackResult.StandardOutput.Trim()
                            : $"código {fallbackResult.Result.ReturnValue}");

                    throw new InvalidOperationException(runAsSystem
                        ? $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}; RPC fallback: {detail}). Verifique permissões para criar tarefas agendadas como SYSTEM ou restrições de criação de processo WMI."
                        : $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}; RPC fallback: {detail}). Verifique permissões para criar tarefa agendada como o usuário de deploy ou restrições de processo WMI.");
                }

                usedFallback = true;
            }

            if (usedFallback)
            {
                var remoteUser = SchtasksRunAsFormatter.FormatRunAsUser(credential);
                var remotePass = credential.Password ?? string.Empty;
                var fallbackRunArgs = string.Format(
                    CultureInfo.InvariantCulture,
                    "/Run /S \"{0}\" /U \"{1}\" /P \"{2}\" /TN \"{3}\"",
                    host,
                    SchtasksRunAsFormatter.EscapeCmdArgument(remoteUser),
                    SchtasksRunAsFormatter.EscapeCmdArgument(remotePass),
                    taskName);

                var runResult = await _schtasksFallbackRunner.RunAsync(fallbackRunArgs, SchtasksBootstrapTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (runResult.Result.ReturnValue != 0)
                {
                    var detail = !string.IsNullOrWhiteSpace(runResult.StandardError)
                        ? runResult.StandardError.Trim()
                        : $"código {runResult.Result.ReturnValue}";
                    throw new InvalidOperationException($"schtasks /Run falhou via RPC remoto em {host}: {detail}");
                }
            }
            else
            {
                var runCmd = $"schtasks /Run /TN \"{taskName}\"";
                await _wmiRunner.RunAsync(host, credential, runCmd, SchtasksBootstrapTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            await PollForResultAsync(host, credential, paths, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (usedFallback)
            {
                try
                {
                    var remoteUser = SchtasksRunAsFormatter.FormatRunAsUser(credential);
                    var remotePass = credential.Password ?? string.Empty;
                    var fallbackDeleteArgs = string.Format(
                        CultureInfo.InvariantCulture,
                        "/Delete /S \"{0}\" /U \"{1}\" /P \"{2}\" /TN \"{3}\" /F",
                        host,
                        SchtasksRunAsFormatter.EscapeCmdArgument(remoteUser),
                        SchtasksRunAsFormatter.EscapeCmdArgument(remotePass),
                        taskName);
                    await _schtasksFallbackRunner.RunAsync(fallbackDeleteArgs, SchtasksBootstrapTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
            else
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

        string? lastLog = null;
        try
        {
            lastLog = await _stager.ReadLogAsync(host, credential, paths, "task.log", CancellationToken.None).ConfigureAwait(false);
        }
        catch { }

        var diag = !string.IsNullOrWhiteSpace(lastLog) ? $" Último log capturado: {lastLog.Trim()}" : "";
        throw new TimeoutException($"Execução elevada expirou em {host} após {timeout}.{diag}");
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
