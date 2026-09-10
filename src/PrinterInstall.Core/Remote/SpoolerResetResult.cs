namespace PrinterInstall.Core.Remote;

/// <summary>
/// Representa o resultado de uma operação de reinício do Spooler de impressão e limpeza de fila.
/// </summary>
public sealed class SpoolerResetResult
{
    public bool IsSuccess { get; }
    public string? Message { get; }
    public string? ErrorMessage { get; }

    private SpoolerResetResult(bool isSuccess, string? message, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Message = message;
        ErrorMessage = errorMessage;
    }

    public static SpoolerResetResult Success(string message = "Serviço Spooler reiniciado com sucesso.") =>
        new(true, message, null);

    public static SpoolerResetResult Failure(string errorMessage) =>
        new(false, null, errorMessage);
}
