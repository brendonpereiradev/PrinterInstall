namespace PrinterInstall.Core.Remote;

public sealed record LocalProcessOutput(RemoteProcessResult Result, string StandardOutput, string StandardError);
