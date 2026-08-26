using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public sealed class DirectRawPrinterTestService : IDirectRawPrinterTestService
{
    private const int RawPort = 9100;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private readonly IRawPrinterConnectionFactory _connectionFactory;

    public DirectRawPrinterTestService()
        : this(new TcpRawPrinterConnectionFactory())
    {
    }

    internal DirectRawPrinterTestService(IRawPrinterConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DirectRawPrinterTestResult> RunAsync(
        string host,
        PrinterBrand brand,
        GainschaLabelPreset? gainschaLabelPreset = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedHost = host.Trim();
        await using var connection = _connectionFactory.Create();

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);
            await connection.ConnectAsync(trimmedHost, RawPort, ConnectTimeout, connectCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Fail(DirectRawPrinterTestPhase.Connectivity,
                $"Sem conectividade com {trimmedHost}.");
        }

        try
        {
            var payload = DirectRawPrinterTestPageBuilder.ForBrand(brand, trimmedHost, gainschaLabelPreset);
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendCts.CancelAfter(SendTimeout);
            await connection.WriteAsync(payload, sendCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fail(DirectRawPrinterTestPhase.Send,
                "Conectou, mas falhou ao enviar — tempo esgotado.");
        }
        catch (Exception ex)
        {
            return Fail(DirectRawPrinterTestPhase.Send,
                $"Conectou, mas falhou ao enviar — {ex.Message}");
        }

        return new DirectRawPrinterTestResult
        {
            Success = true,
            FailedPhase = DirectRawPrinterTestPhase.None,
            Message = "Teste enviado com sucesso. Verifique se a impressora imprimiu a página."
        };
    }

    private static DirectRawPrinterTestResult Fail(DirectRawPrinterTestPhase phase, string message) =>
        new() { Success = false, FailedPhase = phase, Message = message };
}
