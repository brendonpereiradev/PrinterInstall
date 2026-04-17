# Printer Removal (remote wizard) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement remote printer queue listing, per-machine multi-select removal wizard, sequential execution with WinRM+CIM parity, idempotent queue removal, orphan TCP port removal when safe, extra confirmation for default printers, and progress logging per spec `docs/superpowers/specs/2026-04-17-printer-removal-design.md`.

**Architecture:** Extend `IRemotePrinterOperations` with list/remove/count-port/remove-port methods; implement in `WinRmRemotePrinterOperations` (PowerShell JSON + commands) and `CimRemotePrinterOperations` (WMI `Win32_Printer` / `Win32_TCPIPPrinterPort`); wrap in `CompositeRemotePrinterOperations`. Add `PrinterRemovalOrchestrator` consuming `PrinterRemovalRequest` (targets with queue items including captured `PortName` + `WasDefaultAtSelection`). WPF: dedicated wizard window + viewmodel steps, opened from `MainWindow`, using `ISessionContext` credentials.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, CommunityToolkit.Mvvm, `System.Text.Json` for PowerShell JSON, existing `IPowerShellInvoker`, System.Management (WMI), xUnit, Moq.

---

## File map (create / modify)

| Path | Responsibility |
|------|------------------|
| `src/PrinterInstall.Core/Models/RemotePrinterQueueInfo.cs` | DTO: nome da fila, porta, predefinida (deserialização JSON + CIM). |
| `src/PrinterInstall.Core/Models/PrinterRemovalQueueItem.cs` | Item de plano: `PrinterName`, `PortName`, `WasDefaultAtSelection`. |
| `src/PrinterInstall.Core/Models/PrinterRemovalTarget.cs` | `ComputerName` + lista de `PrinterRemovalQueueItem`. |
| `src/PrinterInstall.Core/Models/PrinterRemovalRequest.cs` | Credencial + `IReadOnlyList<PrinterRemovalTarget>`. |
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressState.cs` | Enum de estado para eventos de remoção. |
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressEvent.cs` | `record` com `ComputerName`, `State`, `Message`. |
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalOrchestrator.cs` | Pipeline sequencial; ordem de filas: **OrdinalIgnoreCase** por nome. |
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | Novos métodos (ver Task 2). |
| `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` | Implementação WinRM. |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | Implementação WMI. |
| `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` | Fallback em cada método novo. |
| `src/PrinterInstall.Core/Remote/RemotePrinterQueueInfoJsonParser.cs` | Parse seguro de JSON devolvido pelo PowerShell (array ou vazio). |
| `tests/PrinterInstall.Core.Tests/Remote/RemotePrinterQueueInfoJsonParserTests.cs` | Testes do parser. |
| `tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsRemovalTests.cs` | Contratos de scripts PowerShell para remoção/listagem. |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterRemovalOrchestratorTests.cs` | Orquestrador com mock. |
| `src/PrinterInstall.App/ViewModels/RemovalWizardViewModel.cs` | MVVM do assistente (passos, selecções, revisão, execução). |
| `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml` + `.cs` | UI do wizard. |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Botão "Remove printers…". |
| `src/PrinterInstall.App/App.xaml.cs` | Registar `PrinterRemovalOrchestrator`, `RemovalWizardWindow`, `RemovalWizardViewModel`. |

---

### Task 1: Core models + progress types + JSON parser

**Files:**
- Create: `src/PrinterInstall.Core/Models/RemotePrinterQueueInfo.cs`
- Create: `src/PrinterInstall.Core/Models/PrinterRemovalQueueItem.cs`
- Create: `src/PrinterInstall.Core/Models/PrinterRemovalTarget.cs`
- Create: `src/PrinterInstall.Core/Models/PrinterRemovalRequest.cs`
- Create: `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressState.cs`
- Create: `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressEvent.cs`
- Create: `src/PrinterInstall.Core/Remote/RemotePrinterQueueInfoJsonParser.cs`
- Modify: `src/PrinterInstall.Core/PrinterInstall.Core.csproj` (only if implicit usings need adjustment — normally none)

- [ ] **Step 1: Add model records**

`RemotePrinterQueueInfo.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PrinterInstall.Core.Models;

public sealed record RemotePrinterQueueInfo(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("PortName")] string? PortName,
    [property: JsonPropertyName("IsDefault")] bool IsDefault);
```

`PrinterRemovalQueueItem.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public sealed record PrinterRemovalQueueItem(string PrinterName, string PortName, bool WasDefaultAtSelection);
```

`PrinterRemovalTarget.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public sealed class PrinterRemovalTarget
{
    public required string ComputerName { get; init; }
    public required IReadOnlyList<PrinterRemovalQueueItem> QueuesToRemove { get; init; }
}
```

`PrinterRemovalRequest.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterRemovalRequest
{
    public required NetworkCredential DomainCredential { get; init; }
    public required IReadOnlyList<PrinterRemovalTarget> Targets { get; init; }
}
```

`PrinterRemovalProgressState.cs`:

```csharp
namespace PrinterInstall.Core.Orchestration;

public enum PrinterRemovalProgressState
{
    ContactingRemote,
    RemovingQueue,
    RemovingOrphanPort,
    TargetCompleted,
    Warning,
    Error
}
```

`PrinterRemovalProgressEvent.cs`:

```csharp
namespace PrinterInstall.Core.Orchestration;

public sealed record PrinterRemovalProgressEvent(string ComputerName, PrinterRemovalProgressState State, string Message);
```

- [ ] **Step 2: Add JSON parser (pure)**

`RemotePrinterQueueInfoJsonParser.cs`:

```csharp
using System.Text.Json;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public static class RemotePrinterQueueInfoJsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<RemotePrinterQueueInfo> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RemotePrinterQueueInfo>();

        json = json.Trim();
        if (json == "null" || json == "[]")
            return Array.Empty<RemotePrinterQueueInfo>();

        try
        {
            if (json.StartsWith('['))
                return JsonSerializer.Deserialize<List<RemotePrinterQueueInfo>>(json, Options) ?? new List<RemotePrinterQueueInfo>();

            var single = JsonSerializer.Deserialize<RemotePrinterQueueInfo>(json, Options);
            return single is null ? Array.Empty<RemotePrinterQueueInfo>() : new[] { single };
        }
        catch (JsonException)
        {
            return Array.Empty<RemotePrinterQueueInfo>();
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 4: Commit (when repository owner authorizes commits)**

```bash
git add src/PrinterInstall.Core/Models/RemotePrinterQueueInfo.cs src/PrinterInstall.Core/Models/PrinterRemovalQueueItem.cs src/PrinterInstall.Core/Models/PrinterRemovalTarget.cs src/PrinterInstall.Core/Models/PrinterRemovalRequest.cs src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressState.cs src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressEvent.cs src/PrinterInstall.Core/Remote/RemotePrinterQueueInfoJsonParser.cs
git commit -m "feat(core): add printer removal DTOs and JSON parser for queue listing"
```

---

### Task 2: Parser unit tests

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Remote/RemotePrinterQueueInfoJsonParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemotePrinterQueueInfoJsonParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse(null));
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse(""));
        Assert.Empty(RemotePrinterQueueInfoJsonParser.Parse("   "));
    }

    [Fact]
    public void Parse_Array_TwoItems()
    {
        var json = """[{"Name":"A","PortName":"IP_10.0.0.1","IsDefault":true},{"Name":"B","PortName":"COM1:","IsDefault":false}]""";
        var list = RemotePrinterQueueInfoJsonParser.Parse(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0].Name);
        Assert.Equal("IP_10.0.0.1", list[0].PortName);
        Assert.True(list[0].IsDefault);
    }

    [Fact]
    public void Parse_SingleObject_OneItem()
    {
        var json = """{"Name":"Only","PortName":"X","IsDefault":false}""";
        var list = RemotePrinterQueueInfoJsonParser.Parse(json);
        Assert.Single(list);
        Assert.Equal("Only", list[0].Name);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~RemotePrinterQueueInfoJsonParserTests" -c Release`  
Expected: All tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/PrinterInstall.Core.Tests/Remote/RemotePrinterQueueInfoJsonParserTests.cs
git commit -m "test(core): cover RemotePrinterQueueInfo JSON parsing"
```

---

### Task 3: Extend `IRemotePrinterOperations` and implement WinRM

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`

**Interface methods to append:**

```csharp
Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default);

Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default);

Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default);

Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default);
```

(Add `using PrinterInstall.Core.Models;` to the interface file.)

- [ ] **Step 1: Implement WinRM `ListPrinterQueuesAsync`**

Inner script (must return **one** JSON string as first output line — `@()` garante array JSON mesmo com 0 ou 1 impressora):

```csharp
const string inner = """
$printers = Get-Printer | Select-Object Name, PortName, @{n='IsDefault';e={$_.IsDefault}}
@($printers) | ConvertTo-Json -Compress -Depth 4
""";
```

After `InvokeOnRemoteRunspaceAsync`, take `result[0]`, pass to `RemotePrinterQueueInfoJsonParser.Parse`, filter entries with `string.IsNullOrWhiteSpace(Name)` out, return list.

- [ ] **Step 2: Implement WinRM `RemovePrinterQueueAsync` (idempotente)**

```csharp
var inner = $@"
$p = Get-Printer -Name '{Escape(printerName)}' -ErrorAction SilentlyContinue
if ($null -ne $p) {{ Remove-Printer -Name '{Escape(printerName)}' -Confirm:$false }}
";
```

- [ ] **Step 3: Implement WinRM `CountPrintersUsingPortAsync`**

Emitir **uma** linha de texto só com o número inteiro (o `PowerShellInvoker` junta as saídas em `List<string>`):

```csharp
var inner = $@"
$c = @(Get-Printer | Where-Object {{ $_.PortName -eq '{Escape(portName)}' }}).Count
$c.ToString()
";
```

Parse: `int.Parse(result[^1].Trim(), CultureInfo.InvariantCulture)` (adicionar `using System.Globalization;`). Se `result` estiver vazio, tratar como 0.

- [ ] **Step 4: Implement WinRM `RemoveTcpPrinterPortAsync`**

```csharp
var inner = $@"
$port = Get-PrinterPort -Name '{Escape(portName)}' -ErrorAction SilentlyContinue
if ($null -ne $port) {{ Remove-PrinterPort -Name '{Escape(portName)}' -Confirm:$false }}
";
```

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs
git commit -m "feat(core): WinRM list/remove printer queues and TCP ports"
```

---

### Task 4: WinRM contract tests for removal scripts

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsRemovalTests.cs`

- [ ] **Step 1: Add tests with Moq**

```csharp
using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class WinRmRemotePrinterOperationsRemovalTests
{
    [Fact]
    public async Task ListPrinterQueuesAsync_UsesGetPrinterAndConvertToJson()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { """[{"Name":"Q","PortName":"P","IsDefault":false}]""" });

        var sut = new WinRmRemotePrinterOperations(mock.Object);
        var cred = new NetworkCredential("DOM\\u", "p");
        var list = await sut.ListPrinterQueuesAsync("pc1", cred);

        Assert.Single(list);
        Assert.Equal("Q", list[0].Name);
        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", cred, It.Is<string>(s => s.Contains("Get-Printer") && s.Contains("ConvertTo-Json")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovePrinterQueueAsync_UsesRemovePrinterWhenPresent()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var sut = new WinRmRemotePrinterOperations(mock.Object);
        await sut.RemovePrinterQueueAsync("pc1", new NetworkCredential("u", "p"), "MyQueue");

        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.Is<string>(s => s.Contains("Remove-Printer") && s.Contains("MyQueue")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~WinRmRemotePrinterOperationsRemovalTests" -c Release`  
Expected: All tests passed.

- [ ] **Step 3: Commit**

```bash
git add tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsRemovalTests.cs
git commit -m "test(core): WinRM removal scripts contract"
```

---

### Task 5: CIM/WMI implementations

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`

- [ ] **Step 1: `ListPrinterQueuesAsync`**

Dentro de `Task.Run`: `SELECT Name, PortName, Default FROM Win32_Printer`, mapear `Default` → `IsDefault`, ignorar `Name` vazio.

- [ ] **Step 2: `RemovePrinterQueueAsync`**

Query `Win32_Printer WHERE Name='...'`, se existir `ManagementObject.Delete()`. Se não existir, retornar sem excepção.

- [ ] **Step 3: `CountPrintersUsingPortAsync`**

Query `SELECT Name FROM Win32_Printer WHERE PortName='...'` com `EscapeWql(portName)`, contar.

- [ ] **Step 4: `RemoveTcpPrinterPortAsync`**

Query `Win32_TCPIPPrinterPort WHERE Name='...'`; se existir, `Delete()`. Se não existir (porta não TCP/IP ou já removida), retornar sem erro.

- [ ] **Step 5: Build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs
git commit -m "feat(core): CIM list/remove queues and count port usage"
```

---

### Task 6: Composite fallback for new methods

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`

- [ ] **Step 1: Mirror existing try/catch pattern** for each of the four new methods: try `_primary`, on non-cancelled exception call `_fallback`.

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs
git commit -m "feat(core): composite fallback for printer removal operations"
```

---

### Task 7: `PrinterRemovalOrchestrator`

**Files:**
- Create: `src/PrinterInstall.Core/Orchestration/PrinterRemovalOrchestrator.cs`

**Rules to encode:**

1. Para cada `PrinterRemovalTarget` na ordem do pedido: se `QueuesToRemove` estiver vazio, emitir `TargetCompleted` com mensagem "Nothing to do" e `continue`.
2. Ordenar filas por `PrinterName` com `StringComparer.OrdinalIgnoreCase`.
3. Para cada fila: opcionalmente `ListPrinterQueuesAsync` e localizar o nome — se `IsDefault` e `WasDefaultAtSelection`, mensagem informativa (progresso `RemovingQueue`) antes de remover; se divergir, apenas log na mensagem (ainda remover conforme plano).
4. `RemovePrinterQueueAsync`.
5. `CountPrintersUsingPortAsync(portName)` — se `portName` estiver vazio ou só espaços, saltar remoção de porta.
6. Se contagem `== 0`, `RemoveTcpPrinterPortAsync`; se lançar, capturar, reportar `Warning` com mensagem que inclui o nome da porta, **não** falhar o alvo inteiro.
7. Se `RemovePrinterQueueAsync` falhar, reportar `Error` com nome da fila, **continuar** para as restantes filas do mesmo alvo.
8. No fim do alvo (sem excepção não tratada que aborte o método), `TargetCompleted` com sucesso.

Reuse a mesma técnica de `Flatten` de excepções que `PrinterDeploymentOrchestrator` (copiar o método privado estático ou extrair util partilhado — **YAGNI:** copiar 10 linhas para não abrir refactor extra neste plano).

- [ ] **Step 1: Implement class**

```csharp
public sealed class PrinterRemovalOrchestrator
{
    private readonly IRemotePrinterOperations _remote;

    public PrinterRemovalOrchestrator(IRemotePrinterOperations remote)
    {
        _remote = remote;
    }

    public async Task RunAsync(PrinterRemovalRequest request, IProgress<PrinterRemovalProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        // implementation per rules above
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Orchestration/PrinterRemovalOrchestrator.cs
git commit -m "feat(core): sequential printer removal with orphan port handling"
```

---

### Task 8: Orchestrator unit tests

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterRemovalOrchestratorTests.cs`

- [ ] **Step 1: Test `QueuesToRemove` empty skips remote remove**

Mock: nenhuma chamada a `RemovePrinterQueueAsync`. Verificar `TargetCompleted` ou mensagem "Nothing".

- [ ] **Step 2: Test continua após falha numa fila**

Primeira fila lança, segunda sucede — verificar duas tentativas e evento `Error` + sucesso posterior.

- [ ] **Step 3: Test orphan port**

`CountPrintersUsingPortAsync` retorna 0 após remoção → `RemoveTcpPrinterPortAsync` chamado uma vez com o `PortName` certo.

- [ ] **Step 4: Test warning on orphan port failure**

`RemoveTcpPrinterPortAsync` lança → estado `Warning` e orquestração continua.

- [ ] **Step 5: Run tests**

Run: `dotnet test "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~PrinterRemovalOrchestratorTests" -c Release`  
Expected: All tests passed.

- [ ] **Step 6: Commit**

```bash
git add tests/PrinterInstall.Core.Tests/Orchestration/PrinterRemovalOrchestratorTests.cs
git commit -m "test(core): PrinterRemovalOrchestrator behaviour"
```

---

### Task 9: WPF wizard — ViewModel + Window + DI + entry point

**Files:**
- Create: `src/PrinterInstall.App/ViewModels/RemovalWizardViewModel.cs`
- Create: `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml`
- Create: `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml.cs`
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/App.xaml.cs`

**ViewModel behaviour (mínimo viável):**

- Propriedades: `ComputersText`, `CurrentStepIndex` (0=alvos, 1..N=lista por PC, N+1=revisão, N+2=execução), `CurrentComputerName`, `ObservableCollection<SelectableQueueRow>` com `Name`, `PortName`, `IsDefault`, `IsSelected`.
- Comando `LoadQueuesForCurrentComputerAsync`: chama `_remote.ListPrinterQueuesAsync`, preenche grelha.
- Comando `NextFromTargets`: `ComputerNameListParser.Parse`, guardar `IReadOnlyList<string> _machineOrder`, inicializar índice 0, ir para passo de listagem.
- Comando `NextFromQueueSelection`: guardar selecções num `Dictionary<string, List<PrinterRemovalQueueItem>>` por `ComputerName`; avançar índice; se último PC, ir a revisão com texto agregado.
- Revisão: lista de linhas "PC → fila"; se `WasDefaultAtSelection`, mostrar `TextBlock` a vermelho ou ícone; botão "Executar" só habilitado após `DefaultConfirmed` boolean se existir alguma predefinida (checkbox "Confirmo remoção de impressora predefinida").
- Execução: `Task.Run` ou `async void` com `await _orchestrator.RunAsync(new PrinterRemovalRequest { ... }, new Progress<PrinterRemovalProgressEvent>(AppendLog), ct)`; `AppendLog` usa `Dispatcher.Invoke` para `LogText`.

**DI:**

```csharp
builder.Services.AddSingleton<PrinterRemovalOrchestrator>();
builder.Services.AddTransient<RemovalWizardViewModel>();
builder.Services.AddTransient<RemovalWizardWindow>();
```

**MainViewModel:** propriedade `IRelayCommand OpenRemovalWizardCommand` que resolve `RemovalWizardWindow` via `serviceProvider` — o padrão mais simples: injectar `IServiceProvider` no `MainViewModel` **ou** abrir com `new RemovalWizardWindow(App.Current.Host...)` — **preferir** constructor injection de `Func<RemovalWizardWindow> factory` ou `IServiceProvider`:

```csharp
public partial class MainViewModel
{
    private readonly Func<RemovalWizardWindow> _openWizard;
    // ctor stores factory; OpenRemovalWizardCommand executes _openWizard().ShowDialog();
}
```

Register:

```csharp
builder.Services.AddTransient<Func<RemovalWizardWindow>>(sp => () => sp.GetRequiredService<RemovalWizardWindow>());
```

**RemovalWizardViewModel constructor:** `(IRemotePrinterOperations remote, PrinterRemovalOrchestrator orchestrator, ISessionContext session)`. Credenciais: `session.Credential` (`NetworkCredential?`) — se `null`, mostrar mensagem e não chamar remoting (igual a `MainViewModel.DeployAsync`).

- [ ] **Step 1: Use session credentials**

`ISessionContext.Credential` é `NetworkCredential?` — validar não nulo antes de listar/remover (mesmo padrão que `MainViewModel.cs` linhas 71–76).

- [ ] **Step 2: Implement XAML wizard** with `<TabControl>` or step visibility bound to `CurrentStepIndex`; include DataGrid with `CheckBox` column bound to `IsSelected` for multi-select.

- [ ] **Step 3: Wire MainWindow button**

```xml
<Button Content="Remove printers…" Command="{Binding OpenRemovalWizardCommand}" Margin="0,8,0,0"/>
```

Place above or below Deploy in the right column.

- [ ] **Step 4: Build app**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.App\PrinterInstall.App.csproj" -c Release`  
Expected: Build succeeded.

- [ ] **Step 5: Manual test script (human)**

1. Login, open wizard, enter two computer names, load queues on first, select printers, advance, repeat for second, review, confirm defaults if shown, execute.  
2. Verify log lines and that app remains responsive.

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/RemovalWizardViewModel.cs src/PrinterInstall.App/ViewModels/MainViewModel.cs src/PrinterInstall.App/Views/RemovalWizardWindow.xaml src/PrinterInstall.App/Views/RemovalWizardWindow.xaml.cs src/PrinterInstall.App/Views/MainWindow.xaml src/PrinterInstall.App/App.xaml.cs
git commit -m "feat(app): printer removal wizard and entry point"
```

---

### Task 10: Full test + solution build

- [ ] **Step 1: Run all tests**

Run: `dotnet test "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\PrinterInstall.sln" -c Release`  
Expected: All tests passed.

- [ ] **Step 2: Run solution build**

Run: `dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\PrinterInstall.sln" -c Release`  
Expected: Build succeeded.

---

## Plan self-review

**1. Spec coverage**

| Spec section | Tasks |
|--------------|-------|
| List + select per machine | Task 9 wizard |
| Wizard UX | Task 9 |
| Remove queue + orphan port | Tasks 3–7 |
| Default warning + extra confirm | Task 9 (`DefaultConfirmed`) |
| WinRM + CIM parity | Tasks 3, 5, 6 |
| Sequential processing | Task 7 |
| Continue on per-queue error | Task 7, 8 |
| Port removal warning | Task 7, 8 |
| Idempotent remove | Task 3 PowerShell + Task 5 WMI |
| Progress + cancellation | Task 7 (`CancellationToken` passed through) |
| Revalidate default (optional) | Task 7 optional list before remove |

**2. Placeholder scan:** None; `ISessionContext.Credential` is specified for Task 9.

**3. Type consistency:** `RemotePrinterQueueInfo`, `PrinterRemovalQueueItem`, `PrinterRemovalRequest` used end-to-end; progress uses `PrinterRemovalProgressEvent` only for removal (deploy keeps `DeploymentProgressEvent`).

**Gap closed:** Empty `QueuesToRemove` per target explicitly in orchestrator Task 7.

---

Plan complete and saved to `docs/superpowers/plans/2026-04-17-printer-removal.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
