using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class TransientRetryHelperTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_SuccessfulOperation_ExecutesOnceWithoutRetry()
    {
        // Arrange
        var executionCount = 0;
        var retryNotifications = new List<(Exception Ex, int Attempt, TimeSpan Delay)>();

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                return Task.FromResult(42);
            },
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(10),
            onRetry: (ex, attempt, delay) => retryNotifications.Add((ex, attempt, delay)));

        // Assert
        Assert.Equal(42, result);
        Assert.Equal(1, executionCount);
        Assert.Empty(retryNotifications);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientTimeoutException_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;
        var retryNotifications = new List<(Exception Ex, int Attempt, TimeSpan Delay)>();

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Conexão expirou temporariamente.");
                return Task.FromResult("Sucesso");
            },
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(5),
            onRetry: (ex, attempt, delay) => retryNotifications.Add((ex, attempt, delay)));

        // Assert
        Assert.Equal("Sucesso", result);
        Assert.Equal(2, executionCount);
        Assert.Single(retryNotifications);
        Assert.Equal(1, retryNotifications[0].Attempt);
        Assert.IsType<TimeoutException>(retryNotifications[0].Ex);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientSocketException_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new SocketException((int)SocketError.TimedOut);
                return Task.FromResult("Conectado");
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal("Conectado", result);
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RpcUnavailableCOMException_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;
        const int rpcServerUnavailable = unchecked((int)0x800706BA);

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new COMException("O servidor RPC não está disponível.", rpcServerUnavailable);
                return Task.FromResult(100);
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal(100, result);
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientWin32Exception_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new Win32Exception(1722); // RPC_S_SERVER_UNAVAILABLE
                return Task.FromResult("Win32 Ok");
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal("Win32 Ok", result);
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientExceptionExceedsMaxAttempts_ThrowsLastException()
    {
        // Arrange
        var executionCount = 0;
        var retryNotifications = new List<(Exception Ex, int Attempt, TimeSpan Delay)>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<string>(
                ct =>
                {
                    executionCount++;
                    throw new TimeoutException($"Timeout na tentativa {executionCount}");
                },
                maxAttempts: 2,
                initialDelay: TimeSpan.FromMilliseconds(5),
                onRetry: (e, attempt, delay) => retryNotifications.Add((e, attempt, delay))));

        Assert.Equal(2, executionCount);
        Assert.Equal("Timeout na tentativa 2", ex.Message);
        Assert.Single(retryNotifications);
        Assert.Equal(1, retryNotifications[0].Attempt);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonTransientException_ThrowsImmediatelyWithoutRetry()
    {
        // Arrange
        var executionCount = 0;
        var retryNotifications = new List<(Exception Ex, int Attempt, TimeSpan Delay)>();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<int>(
                ct =>
                {
                    executionCount++;
                    throw new InvalidOperationException("Erro de lógica não transitório.");
                },
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(5),
                onRetry: (e, attempt, delay) => retryNotifications.Add((e, attempt, delay))));

        Assert.Equal(1, executionCount);
        Assert.Equal("Erro de lógica não transitório.", ex.Message);
        Assert.Empty(retryNotifications);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_AccessDeniedException_ThrowsImmediatelyWithoutRetry()
    {
        // Arrange
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<int>(
                ct =>
                {
                    executionCount++;
                    throw new UnauthorizedAccessException("Acesso negado.");
                },
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(5)));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancellationTokenAlreadyCancelled_ThrowsImmediately()
    {
        // Arrange
        var executionCount = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync(
                ct =>
                {
                    executionCount++;
                    return Task.FromResult(1);
                },
                maxAttempts: 3,
                cancellationToken: cts.Token));

        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_OperationThrowsOperationCanceledException_PropagatesWithoutRetry()
    {
        // Arrange
        var executionCount = 0;
        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<int>(
                ct =>
                {
                    executionCount++;
                    cts.Cancel();
                    cts.Token.ThrowIfCancellationRequested();
                    return Task.FromResult(1);
                },
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(5),
                cancellationToken: cts.Token));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ExponentialBackoff_AppliesMultipliedDelays()
    {
        // Arrange
        var executionCount = 0;
        var delaysRecorded = new List<TimeSpan>();

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<int>(
                ct =>
                {
                    executionCount++;
                    throw new TimeoutException();
                },
                maxAttempts: 3,
                initialDelay: TimeSpan.FromMilliseconds(10),
                backoffMultiplier: 2.0,
                onRetry: (_, _, delay) => delaysRecorded.Add(delay)));

        Assert.Equal(3, executionCount);
        Assert.Equal(2, delaysRecorded.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(10), delaysRecorded[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(20), delaysRecorded[1]);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOverload_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;

        // Act
        await TransientRetryHelper.ExecuteWithRetryAsync(
            ct =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Timeout");
                return Task.CompletedTask;
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_SimpleFuncOverload_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;

        // Act
        var result = await TransientRetryHelper.ExecuteWithRetryAsync(
            () =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Timeout");
                return Task.FromResult("OK");
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal("OK", result);
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_SimpleVoidFuncOverload_RetriesAndSucceeds()
    {
        // Arrange
        var executionCount = 0;

        // Act
        await TransientRetryHelper.ExecuteWithRetryAsync(
            () =>
            {
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Timeout");
                return Task.CompletedTask;
            },
            maxAttempts: 2,
            initialDelay: TimeSpan.FromMilliseconds(5));

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_InvalidArguments_ThrowsExpectedExceptions()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync<int>((Func<CancellationToken, Task<int>>)null!));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            TransientRetryHelper.ExecuteWithRetryAsync(ct => Task.FromResult(1), maxAttempts: 0));
    }

    [Theory]
    [InlineData("The RPC server is unavailable.")]
    [InlineData("O servidor RPC não está disponível")]
    [InlineData("Erro 0x800706BA ao conectar")]
    [InlineData("Connection timed out")]
    [InlineData("Tempo limite esgotado")]
    [InlineData("WMI transport failure")]
    [InlineData("The network path was not found.")]
    [InlineData("O caminho de rede não foi encontrado.")]
    public void IsTransient_IdentifiesTransientMessages_ReturnsTrue(string message)
    {
        var ex = new Exception(message);
        Assert.True(TransientRetryHelper.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_IdentifiesTransientExceptionTypes_ReturnsTrue()
    {
        Assert.True(TransientRetryHelper.IsTransient(new TimeoutException()));
        Assert.True(TransientRetryHelper.IsTransient(new SocketException((int)SocketError.TimedOut)));
        Assert.True(TransientRetryHelper.IsTransient(new COMException("RPC failed", unchecked((int)0x800706BA))));
        Assert.True(TransientRetryHelper.IsTransient(new COMException("Bad netpath", unchecked((int)0x80070035))));
        Assert.True(TransientRetryHelper.IsTransient(new Win32Exception(1722)));
        Assert.True(TransientRetryHelper.IsTransient(new Win32Exception(53)));
    }

    [Fact]
    public void IsTransient_IdentifiesNestedTransientException_ReturnsTrue()
    {
        var inner = new TimeoutException("Nested timeout");
        var outer = new InvalidOperationException("Outer wrapper", inner);
        Assert.True(TransientRetryHelper.IsTransient(outer));

        var agg = new AggregateException("Aggregated", outer);
        Assert.True(TransientRetryHelper.IsTransient(agg));
    }

    [Fact]
    public void IsTransient_NonTransientExceptions_ReturnsFalse()
    {
        Assert.False(TransientRetryHelper.IsTransient(null));
        Assert.False(TransientRetryHelper.IsTransient(new UnauthorizedAccessException("Acesso negado")));
        Assert.False(TransientRetryHelper.IsTransient(new ArgumentException("Parâmetro inválido")));
        Assert.False(TransientRetryHelper.IsTransient(new NotImplementedException()));
        Assert.False(TransientRetryHelper.IsTransient(new OperationCanceledException()));
        Assert.False(TransientRetryHelper.IsTransient(new Exception("Qualquer erro comum")));
    }
}
