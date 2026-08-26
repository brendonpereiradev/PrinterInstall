using System.ComponentModel;
using System.Management;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Utilitário para repetição automática de operações com suporte a backoff exponencial
/// em caso de falhas transitórias de rede, RPC ou WMI/CIM durante implantação remota de impressoras.
/// </summary>
public static class TransientRetryHelper
{
    /// <summary>
    /// Número padrão de tentativas máximas para operações transitórias.
    /// </summary>
    public const int DefaultMaxAttempts = 2;

    /// <summary>
    /// Atraso inicial padrão antes da primeira repetição.
    /// </summary>
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Fator padrão de multiplicação para backoff exponencial.
    /// </summary>
    public const double DefaultBackoffMultiplier = 2.0;

    /// <summary>
    /// Executa uma operação assíncrona com valor de retorno, aplicando repetições automáticas
    /// caso ocorram exceções classificadas como transitórias.
    /// </summary>
    /// <typeparam name="T">Tipo do resultado retornado pela operação.</typeparam>
    /// <param name="operation">Função assíncrona a ser executada, recebendo um CancellationToken.</param>
    /// <param name="maxAttempts">Número máximo de tentativas (deve ser maior ou igual a 1).</param>
    /// <param name="initialDelay">Atraso antes da primeira repetição (opcional).</param>
    /// <param name="backoffMultiplier">Multiplicador de atraso para tentativas subsequentes (padrão: 2.0).</param>
    /// <param name="onRetry">Callback acionado a cada repetição, informando a exceção, o índice da tentativa e o atraso.</param>
    /// <param name="isTransient">Predicado opcional para definir se a exceção é transitória. Quando nulo, utiliza <see cref="IsTransient"/>.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado da operação.</returns>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = DefaultBackoffMultiplier,
        Action<Exception, int, TimeSpan>? onRetry = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "O número de tentativas deve ser pelo menos 1.");

        var delay = initialDelay ?? DefaultInitialDelay;
        var predicate = isTransient ?? IsTransient;

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= maxAttempts || !predicate(ex))
                {
                    throw;
                }

                onRetry?.Invoke(ex, attempt, delay);

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                if (backoffMultiplier > 0)
                {
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * backoffMultiplier);
                }
            }
        }
    }

    /// <summary>
    /// Sobrecarga para operações que não requerem parâmetro CancellationToken diretamente no delegado.
    /// </summary>
    public static Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = DefaultBackoffMultiplier,
        Action<Exception, int, TimeSpan>? onRetry = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteWithRetryAsync(_ => operation(), maxAttempts, initialDelay, backoffMultiplier, onRetry, isTransient, cancellationToken);
    }

    /// <summary>
    /// Executa uma operação assíncrona sem retorno com suporte a repetições automáticas em falhas transitórias.
    /// </summary>
    public static async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = DefaultBackoffMultiplier,
        Action<Exception, int, TimeSpan>? onRetry = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteWithRetryAsync<bool>(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            maxAttempts,
            initialDelay,
            backoffMultiplier,
            onRetry,
            isTransient,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sobrecarga para operações sem retorno que não requerem parâmetro CancellationToken diretamente no delegado.
    /// </summary>
    public static Task ExecuteWithRetryAsync(
        Func<Task> operation,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? initialDelay = null,
        double backoffMultiplier = DefaultBackoffMultiplier,
        Action<Exception, int, TimeSpan>? onRetry = null,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteWithRetryAsync(_ => operation(), maxAttempts, initialDelay, backoffMultiplier, onRetry, isTransient, cancellationToken);
    }

    /// <summary>
    /// Determina se uma exceção representa uma falha transitória (timeout, falha de conexão RPC/WMI/Sockets, blip de rede).
    /// </summary>
    public static bool IsTransient(Exception? ex)
    {
        if (ex is null)
            return false;

        // Falhas de permissão / acesso negado nunca são transitórias
        if (AccessDeniedDetector.IsAccessDenied(ex))
            return false;

        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
                return false;

            if (IsTransientSingleException(current))
                return true;
        }

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.Flatten().InnerExceptions)
            {
                if (IsTransient(inner))
                    return true;
            }
        }

        return false;
    }

    private static bool IsTransientSingleException(Exception ex)
    {
        if (ex is TimeoutException)
            return true;

        if (ex is SocketException)
            return true;

        if (ex is ManagementException mgmt)
        {
            if (mgmt.ErrorCode is ManagementStatus.Timedout
                or ManagementStatus.TransportFailure
                or ManagementStatus.CallCanceled
                or ManagementStatus.ServerTooBusy
                or ManagementStatus.ShuttingDown)
            {
                return true;
            }

            if (IsTransientHResult(mgmt.HResult))
                return true;
        }

        if (ex is COMException com && IsTransientHResult(com.HResult))
            return true;

        if (ex is Win32Exception win32 && IsTransientWin32ErrorCode(win32.NativeErrorCode))
            return true;

        if (IsTransientHResult(ex.HResult))
            return true;

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return IsTransientMessage(message);
    }

    private static bool IsTransientHResult(int hresult)
    {
        return hresult switch
        {
            unchecked((int)0x800706BA) => true, // RPC_S_SERVER_UNAVAILABLE
            unchecked((int)0x800706BE) => true, // RPC_S_CALL_FAILED
            unchecked((int)0x800706BF) => true, // RPC_S_CALL_FAILED_DNE
            unchecked((int)0x80070035) => true, // ERROR_BAD_NETPATH
            unchecked((int)0x80070036) => true, // ERROR_NETWORK_BUSY
            unchecked((int)0x80070040) => true, // ERROR_DEV_NOT_EXIST / ERROR_NETNAME_DELETED
            unchecked((int)0x80070079) => true, // ERROR_SEM_TIMEOUT
            unchecked((int)0x80010108) => true, // RPC_E_DISCONNECTED
            unchecked((int)0x8001011F) => true, // RPC_E_TIMEOUT
            unchecked((int)0x8007274C) => true, // WSAETIMEDOUT (10060)
            unchecked((int)0x8007274D) => true, // WSAECONNREFUSED (10061)
            unchecked((int)0x80072751) => true, // WSAEHOSTUNREACH (10065)
            unchecked((int)0x80072749) => true, // WSAENETUNREACH (10051)
            unchecked((int)0x80072746) => true, // WSAECONNRESET (10054)
            unchecked((int)0x80041068) => true, // WBEM_E_TIMED_OUT
            unchecked((int)0x80041015) => true, // WBEM_E_TRANSPORT_FAILURE
            unchecked((int)0x8004100C) => true, // WBEM_E_CALL_CANCELLED
            unchecked((int)0x80041045) => true, // WBEM_E_SERVER_TOO_BUSY
            _ => false
        };
    }

    private static bool IsTransientWin32ErrorCode(int errorCode)
    {
        return errorCode switch
        {
            53 => true,    // ERROR_BAD_NETPATH
            54 => true,    // ERROR_NETWORK_BUSY
            64 => true,    // ERROR_NETNAME_DELETED
            121 => true,   // ERROR_SEM_TIMEOUT
            1722 => true,  // RPC_S_SERVER_UNAVAILABLE
            1726 => true,  // RPC_S_CALL_FAILED
            1727 => true,  // RPC_S_CALL_FAILED_DNE
            10051 => true, // WSAENETUNREACH
            10054 => true, // WSAECONNRESET
            10060 => true, // WSAETIMEDOUT
            10061 => true, // WSAECONNREFUSED
            10065 => true, // WSAEHOSTUNREACH
            _ => false
        };
    }

    private static bool IsTransientMessage(string message)
    {
        if (message.Contains("0x800706BA", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("800706BA", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("0x800706BE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("800706BE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("0x80070079", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("80070079", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("RPC server is unavailable", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("servidor RPC não está disponível", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("servidor RPC indisponível", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("RPC call failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("tempo limite", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("transport failure", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("falha de transporte", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("network path was not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("caminho de rede não foi encontrado", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("network name is no longer available", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("nome de rede especificado não está mais disponível", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
