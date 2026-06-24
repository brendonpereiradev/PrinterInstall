namespace PrinterInstall.Core.Network;

public sealed class DirectRawPrinterTestResult
{
    public required bool Success { get; init; }
    public required DirectRawPrinterTestPhase FailedPhase { get; init; }
    public required string Message { get; init; }
}
