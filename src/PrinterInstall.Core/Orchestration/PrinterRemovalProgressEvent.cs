namespace PrinterInstall.Core.Orchestration;

public sealed record PrinterRemovalProgressEvent(string ComputerName, PrinterRemovalProgressState State, string Message);
