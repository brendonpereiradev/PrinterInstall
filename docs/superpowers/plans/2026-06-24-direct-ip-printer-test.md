# Teste de impressora directo por IP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Acção independente do deploy que, a partir da máquina do operador, testa conectividade TCP em `host:9100` e envia payload RAW (PCL5 ou ESC/POS conforme marca) num diálogo dedicado, com mensagens distintas por fase.

**Architecture:** Novo módulo `PrinterInstall.Core/Network` com `IDirectRawPrinterTestService`, builder de payload por `PrinterBrand`, e conexão TCP injectável para testes. UI: `PrinterNetworkTestWindow` + ViewModel; entrada via botão na `MainWindow`. Sem alterações a `IRemotePrinterOperations` ou orquestradores.

**Tech stack:** .NET 8, WPF + Wpf.Ui, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-24-direct-ip-printer-test-design.md`

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestPhase.cs` | Enum de fase de falha |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestResult.cs` | DTO de resultado |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestPageBuilder.cs` | Bytes PCL5 / ESC-POS por marca |
| `src/PrinterInstall.Core/Network/IRawPrinterConnection.cs` | Abstracção TCP (internal) |
| `src/PrinterInstall.Core/Network/TcpRawPrinterConnection.cs` | Implementação real |
| `src/PrinterInstall.Core/Network/IDirectRawPrinterTestService.cs` | Contrato público |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestService.cs` | Fases 1 e 2 |
| `src/PrinterInstall.App/ViewModels/PrinterNetworkTestViewModel.cs` | Lógica do diálogo |
| `src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml` (+ `.cs`) | UI modal |
| `src/PrinterInstall.App/Strings/PrinterNetworkTest.pt-BR.xaml` | Labels pt-BR |
| `src/PrinterInstall.App/Resources/UiStrings.resx` | Mensagens dinâmicas ViewModel |
| `src/PrinterInstall.App/Resources/UiStrings.Designer.cs` | Propriedades geradas |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | Botão na MainWindow |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Botão "Testar impressora…" |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Comando abrir diálogo |
| `src/PrinterInstall.App/App.xaml` | Merge do resource dictionary |
| `src/PrinterInstall.App/App.xaml.cs` | Registo DI |
| `tests/.../Network/DirectRawPrinterTestPageBuilderTests.cs` | Testes do builder |
| `tests/.../Network/DirectRawPrinterTestServiceTests.cs` | Testes do serviço |

---

### Task 1: Modelos e builder de payload

**Files:**
- Create: `src/PrinterInstall.Core/Network/DirectRawPrinterTestPhase.cs`
- Create: `src/PrinterInstall.Core/Network/DirectRawPrinterTestResult.cs`
- Create: `src/PrinterInstall.Core/Network/DirectRawPrinterTestPageBuilder.cs`
- Create: `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestPageBuilderTests.cs`

- [ ] **Step 1: Escrever testes falhando do builder**

Criar `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestPageBuilderTests.cs`:

```csharp
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestPageBuilderTests
{
    [Theory]
    [InlineData(PrinterBrand.Epson)]
    [InlineData(PrinterBrand.Lexmark)]
    public void ForBrand_PclBrands_ReturnsNonEmptyPayload(PrinterBrand brand)
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(brand, "10.0.0.50");
        Assert.NotEmpty(payload);
        Assert.Contains((byte)0x1B, payload); // ESC — PCL reset
    }

    [Fact]
    public void ForBrand_Gainscha_ReturnsEscPosPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.51");
        Assert.NotEmpty(payload);
        Assert.Equal(0x1B, payload[0]); // ESC
        Assert.Equal((byte)'@', payload[1]); // ESC @ init
    }

    [Fact]
    public void ForBrand_Gainscha_DiffersFromEpson()
    {
        var pcl = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "10.0.0.50");
        var escPos = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Gainscha, "10.0.0.50");
        Assert.NotEqual(pcl, escPos);
    }

    [Fact]
    public void ForBrand_IncludesHostInPayload()
    {
        var payload = DirectRawPrinterTestPageBuilder.ForBrand(PrinterBrand.Epson, "192.168.1.99");
        var text = System.Text.Encoding.ASCII.GetString(payload);
        Assert.Contains("192.168.1.99", text);
    }
}
```

- [ ] **Step 2: Correr testes — devem falhar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DirectRawPrinterTestPageBuilderTests" -v n`

Expected: FAIL — tipo `DirectRawPrinterTestPageBuilder` não encontrado

- [ ] **Step 3: Implementar modelos e builder**

`DirectRawPrinterTestPhase.cs`:

```csharp
namespace PrinterInstall.Core.Network;

public enum DirectRawPrinterTestPhase
{
    None,
    Connectivity,
    Send
}
```

`DirectRawPrinterTestResult.cs`:

```csharp
namespace PrinterInstall.Core.Network;

public sealed class DirectRawPrinterTestResult
{
    public required bool Success { get; init; }
    public required DirectRawPrinterTestPhase FailedPhase { get; init; }
    public required string Message { get; init; }
}
```

`DirectRawPrinterTestPageBuilder.cs`:

```csharp
using System.Text;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public static class DirectRawPrinterTestPageBuilder
{
    public static byte[] ForBrand(PrinterBrand brand, string host)
    {
        return brand switch
        {
            PrinterBrand.Gainscha => BuildEscPos(host),
            PrinterBrand.Epson => BuildPcl5(host),
            PrinterBrand.Lexmark => BuildPcl5(host),
            _ => BuildPcl5(host)
        };
    }

    private static byte[] BuildPcl5(string host)
    {
        var lines = new[]
        {
            "Printer Install - Pagina de teste",
            $"Host: {host}",
            $"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "",
            "Se esta pagina imprimiu, a conectividade RAW/PCL esta OK."
        };
        var sb = new List<byte>();
        sb.AddRange([0x1B, (byte)'E']); // Reset
        sb.AddRange(Encoding.ASCII.GetBytes(string.Join("\r\n", lines)));
        sb.Add(0x0C); // Form feed
        sb.AddRange([0x1B, (byte)'E']); // Reset
        return sb.ToArray();
    }

    private static byte[] BuildEscPos(string host)
    {
        var text = new StringBuilder();
        text.AppendLine("Printer Install - Teste");
        text.AppendLine($"Host: {host}");
        text.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine("");
        text.AppendLine("Conectividade ESC/POS OK.");
        text.AppendLine("");
        var bytes = new List<byte> { 0x1B, (byte)'@' }; // Init
        bytes.AddRange(Encoding.ASCII.GetBytes(text.ToString()));
        bytes.AddRange([0x1B, (byte)'d', 4]); // Feed 4 lines
        return bytes.ToArray();
    }
}
```

- [ ] **Step 4: Correr testes — devem passar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DirectRawPrinterTestPageBuilderTests" -v n`

Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Network/DirectRawPrinterTestPhase.cs src/PrinterInstall.Core/Network/DirectRawPrinterTestResult.cs src/PrinterInstall.Core/Network/DirectRawPrinterTestPageBuilder.cs tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestPageBuilderTests.cs
git commit -m "feat(core): add direct RAW printer test page builder"
```

---

### Task 2: Serviço TCP em duas fases

**Files:**
- Create: `src/PrinterInstall.Core/Network/IRawPrinterConnection.cs`
- Create: `src/PrinterInstall.Core/Network/TcpRawPrinterConnection.cs`
- Create: `src/PrinterInstall.Core/Network/IDirectRawPrinterTestService.cs`
- Create: `src/PrinterInstall.Core/Network/DirectRawPrinterTestService.cs`
- Create: `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestServiceTests.cs`

- [ ] **Step 1: Escrever testes falhando do serviço**

Criar `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestServiceTests.cs`:

```csharp
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.Core.Tests.Network;

public class DirectRawPrinterTestServiceTests
{
    private sealed class FakeConnection : IRawPrinterConnection
    {
        public bool ShouldConnectFail { get; init; }
        public bool ShouldWriteFail { get; init; }
        public byte[]? Written { get; private set; }

        public Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (ShouldConnectFail)
                throw new TimeoutException("connect failed");
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (ShouldWriteFail)
                throw new IOException("write failed");
            Written = data.ToArray();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory : IRawPrinterConnectionFactory
    {
        public FakeConnection Next { get; set; } = new();
        public IRawPrinterConnection Create() => Next;
    }

    [Fact]
    public async Task RunAsync_WhenConnectFails_ReturnsConnectivityPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldConnectFail = true } };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.1", PrinterBrand.Epson);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Connectivity, result.FailedPhase);
        Assert.Contains("10.0.0.1:9100", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenConnectSucceedsButWriteFails_ReturnsSendPhase()
    {
        var factory = new FakeFactory { Next = new FakeConnection { ShouldWriteFail = true } };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.2", PrinterBrand.Lexmark);

        Assert.False(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.Send, result.FailedPhase);
        Assert.Contains("Conectou", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenBothSucceed_ReturnsSuccess()
    {
        var fake = new FakeConnection();
        var factory = new FakeFactory { Next = fake };
        var sut = new DirectRawPrinterTestService(factory);

        var result = await sut.RunAsync("10.0.0.3", PrinterBrand.Epson);

        Assert.True(result.Success);
        Assert.Equal(DirectRawPrinterTestPhase.None, result.FailedPhase);
        Assert.NotNull(fake.Written);
        Assert.NotEmpty(fake.Written!);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringConnect_ThrowsOperationCanceledException()
    {
        var factory = new FakeFactory();
        var sut = new DirectRawPrinterTestService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RunAsync("10.0.0.4", PrinterBrand.Gainscha, cts.Token));
    }
}
```

- [ ] **Step 2: Correr testes — devem falhar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DirectRawPrinterTestServiceTests" -v n`

Expected: FAIL — tipos não encontrados

- [ ] **Step 3: Implementar abstracção TCP e serviço**

`IRawPrinterConnection.cs` (ficheiro único com interface + factory):

```csharp
namespace PrinterInstall.Core.Network;

internal interface IRawPrinterConnection : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

internal interface IRawPrinterConnectionFactory
{
    IRawPrinterConnection Create();
}

internal sealed class TcpRawPrinterConnectionFactory : IRawPrinterConnectionFactory
{
    public IRawPrinterConnection Create() => new TcpRawPrinterConnection();
}
```

`TcpRawPrinterConnection.cs`:

```csharp
using System.Net.Sockets;

namespace PrinterInstall.Core.Network;

internal sealed class TcpRawPrinterConnection : IRawPrinterConnection
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        await _client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
        _stream = _client.GetStream();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected.");
        await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }
        _client?.Dispose();
        _client = null;
    }
}
```

`IDirectRawPrinterTestService.cs`:

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Network;

public interface IDirectRawPrinterTestService
{
    Task<DirectRawPrinterTestResult> RunAsync(
        string host,
        PrinterBrand brand,
        CancellationToken cancellationToken = default);
}
```

`DirectRawPrinterTestService.cs`:

```csharp
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(DirectRawPrinterTestPhase.Connectivity,
                $"Sem conectividade em {trimmedHost}:{RawPort} — tempo esgotado.");
        }
        catch (Exception ex)
        {
            return Fail(DirectRawPrinterTestPhase.Connectivity,
                $"Sem conectividade em {trimmedHost}:{RawPort} — {ex.Message}");
        }

        try
        {
            var payload = DirectRawPrinterTestPageBuilder.ForBrand(brand, trimmedHost);
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendCts.CancelAfter(SendTimeout);
            await connection.WriteAsync(payload, sendCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
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
```

Adicionar em `PrinterInstall.Core.csproj` (já existe `InternalsVisibleTo` para testes):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PrinterInstall.Core.Tests" />
</ItemGroup>
```

(Verificar se já presente — não duplicar.)

- [ ] **Step 4: Correr testes — devem passar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DirectRawPrinterTestServiceTests" -v n`

Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Network/IRawPrinterConnection.cs src/PrinterInstall.Core/Network/TcpRawPrinterConnection.cs src/PrinterInstall.Core/Network/IDirectRawPrinterTestService.cs src/PrinterInstall.Core/Network/DirectRawPrinterTestService.cs tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestServiceTests.cs
git commit -m "feat(core): add direct RAW printer test service with two-phase TCP"
```

---

### Task 3: ViewModel e strings

**Files:**
- Create: `src/PrinterInstall.App/ViewModels/PrinterNetworkTestViewModel.cs`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.resx`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.Designer.cs`
- Create: `src/PrinterInstall.App/Strings/PrinterNetworkTest.pt-BR.xaml`
- Modify: `src/PrinterInstall.App/App.xaml`

- [ ] **Step 1: Adicionar strings ao UiStrings.resx**

Inserir entradas:

```xml
<data name="NetworkTest_Validation_HostRequired" xml:space="preserve"><value>O endereço IP ou hostname é obrigatório.</value></data>
<data name="NetworkTest_Progress_Connectivity" xml:space="preserve"><value>A testar conectividade…</value></data>
<data name="NetworkTest_Progress_Sending" xml:space="preserve"><value>A enviar página de teste…</value></data>
<data name="NetworkTest_Cancelled" xml:space="preserve"><value>Teste cancelado.</value></data>
```

Adicionar propriedades correspondentes em `UiStrings.Designer.cs`:

```csharp
public static string NetworkTest_Validation_HostRequired => ResourceManager.GetString(nameof(NetworkTest_Validation_HostRequired), ResourceCulture)!;
public static string NetworkTest_Progress_Connectivity => ResourceManager.GetString(nameof(NetworkTest_Progress_Connectivity), ResourceCulture)!;
public static string NetworkTest_Progress_Sending => ResourceManager.GetString(nameof(NetworkTest_Progress_Sending), ResourceCulture)!;
public static string NetworkTest_Cancelled => ResourceManager.GetString(nameof(NetworkTest_Cancelled), ResourceCulture)!;
```

- [ ] **Step 2: Criar PrinterNetworkTest.pt-BR.xaml**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">
  <sys:String x:Key="NetworkTest_WindowTitle">Testar impressora</sys:String>
  <sys:String x:Key="NetworkTest_BrandLabel">Marca</sys:String>
  <sys:String x:Key="NetworkTest_HostLabel">IP ou hostname</sys:String>
  <sys:String x:Key="NetworkTest_Hint">Teste de conectividade RAW na porta 9100. Confirma envio na rede; verifique fisicamente se a página saiu.</sys:String>
  <sys:String x:Key="NetworkTest_RunButton">Testar</sys:String>
  <sys:String x:Key="NetworkTest_CloseButton">Fechar</sys:String>
</ResourceDictionary>
```

Registar em `App.xaml` dentro de `MergedDictionaries`:

```xml
<ResourceDictionary Source="Strings/PrinterNetworkTest.pt-BR.xaml"/>
```

- [ ] **Step 3: Implementar PrinterNetworkTestViewModel**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterInstall.App.Resources;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Network;

namespace PrinterInstall.App.ViewModels;

public partial class PrinterNetworkTestViewModel : ObservableObject
{
    private readonly IDirectRawPrinterTestService _testService;
    private CancellationTokenSource? _cts;

    public PrinterNetworkTestViewModel(IDirectRawPrinterTestService testService)
    {
        _testService = testService;
    }

    [ObservableProperty] private PrinterBrand _selectedBrand = PrinterBrand.Epson;
    [ObservableProperty] private string _hostAddress = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isRunning;

    public IEnumerable<PrinterBrand> BrandChoices => Enum.GetValues<PrinterBrand>();

    public bool CanRun => !IsRunning && !string.IsNullOrWhiteSpace(HostAddress);

    partial void OnHostAddressChanged(string value) => RunTestCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
        RunTestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(HostAddress))
        {
            StatusMessage = UiStrings.NetworkTest_Validation_HostRequired;
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = UiStrings.NetworkTest_Progress_Connectivity;

        try
        {
            StatusMessage = UiStrings.NetworkTest_Progress_Sending;
            var result = await _testService.RunAsync(
                HostAddress.Trim(),
                SelectedBrand,
                _cts.Token).ConfigureAwait(true);
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = UiStrings.NetworkTest_Cancelled;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        _cts?.Cancel();
    }
}
```

Nota: mover `StatusMessage = Connectivity` **antes** do await de connect exigiria expor callbacks do serviço; na v1 o ViewModel mostra conectividade brevemente e avança para envio (aceitável). Alternativa: dividir serviço em `TestConnectivityAsync` + `SendAsync` — **não** necessário na v1.

Ajuste: mostrar conectividade, depois chamar serviço (serviço faz ambas internamente):

```csharp
StatusMessage = UiStrings.NetworkTest_Progress_Connectivity;
await Task.Yield();
StatusMessage = UiStrings.NetworkTest_Progress_Sending;
var result = await _testService.RunAsync(...);
```

- [ ] **Step 4: Build da app**

Run: `dotnet build "src/PrinterInstall.App/PrinterInstall.App.csproj" -v n`

Expected: BUILD succeeded (ViewModel compila; Window ainda não existe — OK neste passo se só ViewModel + strings)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/PrinterNetworkTestViewModel.cs src/PrinterInstall.App/Resources/UiStrings.resx src/PrinterInstall.App/Resources/UiStrings.Designer.cs src/PrinterInstall.App/Strings/PrinterNetworkTest.pt-BR.xaml src/PrinterInstall.App/App.xaml
git commit -m "feat(app): add printer network test viewmodel and strings"
```

---

### Task 4: Janela modal e integração na MainWindow

**Files:**
- Create: `src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml`
- Create: `src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml.cs`
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/App.xaml.cs`

- [ ] **Step 1: Criar PrinterNetworkTestWindow.xaml**

```xml
<Window x:Class="PrinterInstall.App.Views.PrinterNetworkTestWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="{DynamicResource NetworkTest_WindowTitle}"
        Height="320" Width="480"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{DynamicResource NetworkTest_Hint}" TextWrapping="Wrap" Margin="0,0,0,12"/>

        <StackPanel Grid.Row="1" Margin="0,0,0,8">
            <TextBlock Text="{DynamicResource NetworkTest_BrandLabel}" Margin="0,0,0,4"/>
            <ComboBox ItemsSource="{Binding BrandChoices}" SelectedItem="{Binding SelectedBrand}"/>
        </StackPanel>

        <StackPanel Grid.Row="2" Margin="0,0,0,8">
            <TextBlock Text="{DynamicResource NetworkTest_HostLabel}" Margin="0,0,0,4"/>
            <TextBox Text="{Binding HostAddress, UpdateSourceTrigger=PropertyChanged}"/>
        </StackPanel>

        <TextBlock Grid.Row="4" Text="{Binding StatusMessage}" TextWrapping="Wrap" Margin="0,8,0,8"/>

        <StackPanel Grid.Row="5" Orientation="Horizontal" HorizontalAlignment="Right">
            <ui:Button Content="{DynamicResource NetworkTest_RunButton}"
                       Command="{Binding RunTestCommand}"
                       Appearance="Primary" Margin="0,0,8,0"/>
            <ui:Button Content="{DynamicResource NetworkTest_CloseButton}"
                       IsCancel="True" Appearance="Secondary"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Criar code-behind**

`PrinterNetworkTestWindow.xaml.cs`:

```csharp
using PrinterInstall.App.ViewModels;

namespace PrinterInstall.App.Views;

public partial class PrinterNetworkTestWindow
{
    public PrinterNetworkTestWindow(PrinterNetworkTestViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Botão e comando na MainWindow**

Em `Strings/Main.pt-BR.xaml`, adicionar:

```xml
<sys:String x:Key="Main_TestPrinterButton">Testar impressora…</sys:String>
```

Em `MainWindow.xaml`, após o botão `Main_RemovePrintersButton`:

```xml
<ui:Button Content="{DynamicResource Main_TestPrinterButton}"
           Command="{Binding OpenPrinterNetworkTestCommand}"
           Appearance="Secondary" Margin="0,8,0,0"/>
```

Em `MainViewModel.cs`, adicionar comando (espelhar `OpenRemovalWizard`):

```csharp
[RelayCommand]
private void OpenPrinterNetworkTest()
{
    var window = _serviceProvider.GetRequiredService<Views.PrinterNetworkTestWindow>();
    var owner = Application.Current.Windows
        .OfType<Window>()
        .FirstOrDefault(w => w.IsLoaded && w.IsVisible && !ReferenceEquals(w, window));
    if (owner is not null)
        window.Owner = owner;
    window.ShowDialog();
}
```

- [ ] **Step 4: Registar DI em App.xaml.cs**

```csharp
using PrinterInstall.Core.Network;

// Após outros singletons:
builder.Services.AddSingleton<IDirectRawPrinterTestService, DirectRawPrinterTestService>();

// Após outros transients:
builder.Services.AddTransient<PrinterNetworkTestViewModel>();
builder.Services.AddTransient<PrinterNetworkTestWindow>();
```

- [ ] **Step 5: Build completo**

Run: `dotnet build "PrinterInstall.sln" -v n`

Expected: BUILD succeeded

- [ ] **Step 6: Correr todos os testes**

Run: `dotnet test "PrinterInstall.sln" -v n`

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml.cs src/PrinterInstall.App/Views/MainWindow.xaml src/PrinterInstall.App/Strings/Main.pt-BR.xaml src/PrinterInstall.App/ViewModels/MainViewModel.cs src/PrinterInstall.App/App.xaml.cs
git commit -m "feat(app): add direct IP printer test dialog and main window entry"
```

---

### Task 5: Verificação manual

- [ ] **Step 1: Checklist manual (spec secção Testes)**

1. Abrir app → login → clicar **Testar impressora…**
2. Epson/Lexmark + IP válido na rede → mensagem de sucesso; verificar papel
3. Gainscha + IP válido → ticket ESC/POS
4. IP inexistente → erro fase conectividade (`Sem conectividade em …:9100`)
5. IP válido com porta 9100 fechada → erro fase conectividade
6. Fechar diálogo durante teste → cancelamento limpo

- [ ] **Step 2: Commit final (se ajustes menores de QA)**

Apenas se correções forem necessárias após QA manual.

---

## Self-review (spec coverage)

| Requisito spec | Task |
|----------------|------|
| Acção independente do deploy | Task 4 (botão + diálogo separado) |
| Origem local | Task 2 (`TcpClient` local) |
| 9100 RAW fixo | Task 2 (`RawPort = 9100`) |
| Marca obrigatória | Task 3 + Task 4 (ComboBox) |
| Duas fases + mensagens distintas | Task 2 (serviço) + Task 3 (strings) |
| PCL5 Epson/Lexmark, ESC/POS Gainscha | Task 1 |
| Sem remoting / credenciais | Nenhuma alteração remota |
| Testes unitários | Task 1 + Task 2 |
| i18n pt-BR | Task 3 + Task 4 |
| Fora de scope respeitado | Plano não inclui deploy/orquestrador |

**Placeholder scan:** nenhum TBD/TODO encontrado.

**Type consistency:** `IDirectRawPrinterTestService.RunAsync(string, PrinterBrand, CancellationToken)` usado de forma consistente em ViewModel, serviço e testes.
