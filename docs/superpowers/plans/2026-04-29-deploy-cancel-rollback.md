# Cancelar deploy e reverter alterações — Plano de implementação

> **Para agentes:** sub-skill recomendado: `superpowers:subagent-driven-development` ou `superpowers:executing-plans`. Os passos usam checkbox (`- [ ]`) para acompanhamento.

**Goal:** Durante o deploy remoto, permitir **«Cancelar deploy»** (cancelamento cooperativo) e **reverter nos alvos** apenas o que esta execução criou com sucesso, via **journal na Core** e reutilização de `PrinterControlOrchestrator`, conforme `docs/superpowers/specs/2026-04-29-deploy-cancel-rollback-design.md`.

**Architecture:** O `PrinterDeploymentOrchestrator` passa a receber `CancellationToken` e um `DeploymentRollbackJournal` mutável: regista porta após `CreateTcpPrinterPortAsync` bem-sucedido; ao registar fila após `AddPrinterAsync`, remove o par (computador, porta) do conjunto “só porta”. Um `DeploymentRollbackOrchestrator` (ou nome equivalente) constrói `PrinterControlRequest` a partir das filas registadas, executa `PrinterControlOrchestrator`, depois trata portas “só porta” com `CountPrintersUsingPortAsync` + `RemoveTcpPrinterPortAsync`. A `MainViewModel` usa `CancellationTokenSource`, desactiva **Implantar** durante a corrida e expõe o comando **Cancelar deploy**.

**Tech stack:** .NET, WPF, CommunityToolkit.Mvvm, `Microsoft.Extensions.DependencyInjection`, xUnit, Moq.

**Commits:** A política do proprietário do produto é solicitar commits explicitamente; os passos de *commit* listados podem ser omitidos ou agrupados até haver pedido.

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Orchestration/DeploymentRollbackJournal.cs` | Append-only: filas criadas + portas “só porta”; API `RecordPortCreated`, `RecordQueueCreated`, construção de dados para rollback. |
| `src/PrinterInstall.Core/Orchestration/DeploymentRollbackRunner.cs` | Orquestra reversão: `PrinterControlOrchestrator` + limpeza de portas órfãs sem fila no journal. |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | `RunAsync(..., DeploymentRollbackJournal journal, CancellationToken ct)`; pontos de cancelamento; chamadas ao journal. |
| `src/PrinterInstall.Core/Models/TargetMachineState.cs` | Novos valores para UI pós-cancelamento/reversão (se adoptados nas tarefas abaixo). |
| `src/PrinterInstall.App/Localization/TargetMachineStateDisplay.cs` | Textos pt-BR para novos estados. |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | CTS, `CancelDeployCommand`, `DeployCommand` com `CanExecute`, fluxo `try/catch (OperationCanceledException)`, chamada ao rollback. |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Botão **Cancelar deploy** (ligado ao comando, visível ou activo só com deploy em curso). |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | `Main_CancelDeployButton` = «Cancelar deploy». |
| `src/PrinterInstall.App/Resources/UiStrings.resx` (+ Designer) | Mensagens de log/resumo: cancelamento pedido, reverso em curso, reverso concluído, aviso de limite cooperativo. |
| `tests/PrinterInstall.Core.Tests/Orchestration/DeploymentRollbackJournalTests.cs` | Journal: porta+fila supersede, port-only duplicado. |
| `tests/PrinterInstall.Core.Tests/Orchestration/DeploymentRollbackRunnerTests.cs` | Mocks: só remove entradas do journal; ordem fila antes de port-only. |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestrator*Tests.cs` | Actualizar todas as chamadas `RunAsync` com `journal` + `CancellationToken`; novos testes de cancelamento e preenchimento do journal. |

---

### Task 1: `DeploymentRollbackJournal` e testes unitários

**Files:**
- Create: `src/PrinterInstall.Core/Orchestration/DeploymentRollbackJournal.cs`
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/DeploymentRollbackJournalTests.cs`

- [ ] **Step 1: Escrever testes que falham**

```csharp
using PrinterInstall.Core.Orchestration;

namespace PrinterInstall.Core.Tests.Orchestration;

public class DeploymentRollbackJournalTests
{
    [Fact]
    public void RecordQueueCreated_RemovesMatchingPortOnlyEntry()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "10.0.0.1");
        Assert.True(j.HasRollbackWork);
        j.RecordQueueCreated("pc1", "Q1", "10.0.0.1");
        Assert.Single(j.QueueEntries);
        Assert.Empty(j.PortOnlyEntries);
    }

    [Fact]
    public void PortOnly_WithoutQueue_RemainsForRollback()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "10.0.0.2");
        Assert.Single(j.PortOnlyEntries);
        Assert.Contains(("pc1", "10.0.0.2"), j.PortOnlyEntries);
    }

    [Fact]
    public void DuplicatePortOnly_SameComputerAndPort_IsIdempotent()
    {
        var j = new DeploymentRollbackJournal();
        j.RecordPortCreated("pc1", "P");
        j.RecordPortCreated("pc1", "P");
        Assert.Single(j.PortOnlyEntries);
    }
}
```

- [ ] **Step 2: Correr testes (devem falhar a compilar)**

Run: `dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DeploymentRollbackJournalTests"`

Expected: erro de compilação (`DeploymentRollbackJournal` não existe).

- [ ] **Step 3: Implementar o journal**

```csharp
namespace PrinterInstall.Core.Orchestration;

public sealed class DeploymentRollbackJournal
{
    private readonly List<DeploymentRollbackQueueEntry> _queues = new();
    private readonly HashSet<(string Computer, string PortName)> _portOnly = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DeploymentRollbackQueueEntry> QueueEntries => _queues;
    public IReadOnlyCollection<(string Computer, string PortName)> PortOnlyEntries => _portOnly;

    public bool HasRollbackWork => _queues.Count > 0 || _portOnly.Count > 0;

    public void RecordPortCreated(string computerName, string portName)
    {
        var c = computerName.Trim();
        var p = portName.Trim();
        if (c.Length == 0 || p.Length == 0)
            return;
        _portOnly.Add((c, p));
    }

    public void RecordQueueCreated(string computerName, string printerName, string portName)
    {
        var c = computerName.Trim();
        var q = printerName.Trim();
        var p = portName.Trim();
        if (c.Length == 0 || q.Length == 0 || p.Length == 0)
            return;

        _portOnly.Remove((c, p));
        _queues.Add(new DeploymentRollbackQueueEntry(c, q, p));
    }
}

public sealed record DeploymentRollbackQueueEntry(string ComputerName, string PrinterName, string PortName);
```

(Criar o `record` no mesmo ficheiro ou em `Models/` se preferir consistência com outros DTOs.)

- [ ] **Step 4: Correr testes**

Run: mesmo comando do Step 2.

Expected: **Passed**.

- [ ] **Step 5: Commit** (se o PO solicitar)

---

### Task 2: `DeploymentRollbackRunner` e testes com mocks

**Files:**
- Create: `src/PrinterInstall.Core/Orchestration/DeploymentRollbackRunner.cs`
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/DeploymentRollbackRunnerTests.cs`
- Modify: `src/PrinterInstall.App/App.xaml.cs` (registar `DeploymentRollbackRunner` na Task 4)

- [ ] **Step 1: Teste — rollback de fila chama `RemovePrinterQueueAsync` e remove porta órfã**

```csharp
using System.Net;
using Moq;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;
using PrinterInstall.Core.Tests.TestSupport;

namespace PrinterInstall.Core.Tests.Orchestration;

public class DeploymentRollbackRunnerTests
{
    [Fact]
    public async Task RunAsync_QueueEntry_CallsRemoveQueue_ThenOrphanPort()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(r => r.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        remote.Setup(r => r.CountPrintersUsingPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remote.Setup(r => r.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var control = new PrinterControlOrchestrator(remote.Object);
        var sut = new DeploymentRollbackRunner(remote.Object, control);
        var journal = new DeploymentRollbackJournal();
        journal.RecordQueueCreated("pc1", "Q1", "10.0.0.1");

        var cred = new NetworkCredential("u", "p");
        var events = new List<PrinterRemovalProgressEvent>();
        await sut.RunAsync(journal, cred, new InlineProgress<PrinterRemovalProgressEvent>(events.Add), CancellationToken.None);

        remote.Verify(r => r.RemovePrinterQueueAsync("pc1", cred, "Q1", It.IsAny<CancellationToken>()), Times.Once);
        remote.Verify(r => r.CountPrintersUsingPortAsync("pc1", cred, "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
        remote.Verify(r => r.RemoveTcpPrinterPortAsync("pc1", cred, "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_PortOnly_SkipsRemoveQueue_RemovesPortWhenUnused()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(r => r.CountPrintersUsingPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        remote.Setup(r => r.RemoveTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var control = new PrinterControlOrchestrator(remote.Object);
        var sut = new DeploymentRollbackRunner(remote.Object, control);
        var journal = new DeploymentRollbackJournal();
        journal.RecordPortCreated("pc1", "10.0.0.5");

        await sut.RunAsync(journal, new NetworkCredential("u", "p"), new Progress<PrinterRemovalProgressEvent>(_ => { }), CancellationToken.None);

        remote.Verify(r => r.RemovePrinterQueueAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        remote.Verify(r => r.RemoveTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), "10.0.0.5", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Correr testes — esperado falha de compilação**

Run: `dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~DeploymentRollbackRunnerTests"`

- [ ] **Step 3: Implementar `DeploymentRollbackRunner`**

O runner recebe **`IRemotePrinterOperations` + `PrinterControlOrchestrator`**: o orquestrador aplica remoções de fila (e portas órfãs associadas); o remoto trata **port-only** com `CountPrintersUsingPortAsync` / `RemoveTcpPrinterPortAsync`.

```csharp
using System.Net;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class DeploymentRollbackRunner
{
    private readonly IRemotePrinterOperations _remote;
    private readonly PrinterControlOrchestrator _control;

    public DeploymentRollbackRunner(IRemotePrinterOperations remote, PrinterControlOrchestrator control)
    {
        _remote = remote;
        _control = control;
    }

    public async Task RunAsync(
        DeploymentRollbackJournal journal,
        NetworkCredential domainCredential,
        IProgress<PrinterRemovalProgressEvent> progress,
        CancellationToken cancellationToken = default)
    {
        if (!journal.HasRollbackWork)
            return;

        var targetsByComputer = journal.QueueEntries
            .GroupBy(e => e.ComputerName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PrinterControlTarget
            {
                ComputerName = g.Key,
                QueuesToRemove = g.Select(e => new PrinterRemovalQueueItem(e.PrinterName, e.PortName)).ToList()
            })
            .ToList();

        var request = new PrinterControlRequest
        {
            DomainCredential = domainCredential,
            Targets = targetsByComputer
        };

        if (targetsByComputer.Count > 0)
            await _control.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);

        foreach (var (computer, portName) in journal.PortOnlyEntries
                     .OrderBy(x => x.Computer, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.PortName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new PrinterRemovalProgressEvent(
                computer,
                PrinterRemovalProgressState.RemovingOrphanPort,
                $"Rollback: checking orphan port '{portName}'..."));

            int count;
            try
            {
                count = await _remote.CountPrintersUsingPortAsync(computer, domainCredential, portName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.Warning,
                    $"Rollback: could not check port '{portName}': {ex.Message}"));
                continue;
            }

            if (count != 0)
                continue;

            try
            {
                await _remote.RemoveTcpPrinterPortAsync(computer, domainCredential, portName, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report(new PrinterRemovalProgressEvent(
                    computer,
                    PrinterRemovalProgressState.Warning,
                    $"Rollback: could not remove port '{portName}': {ex.Message}"));
            }
        }
    }
}
```

- [ ] **Step 4: Ajustar testes** para constructor `DeploymentRollbackRunner(IRemotePrinterOperations remote, PrinterControlOrchestrator control)` e verificações coerentes.

- [ ] **Step 5: `dotnet test` no projecto Core.Tests** — esperado: **tudo a verde** para journal + runner.

---

### Task 3: `PrinterDeploymentOrchestrator` — `CancellationToken`, journal e cancelamento entre passos

**Files:**
- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorMultiPrinterTests.cs`

- [ ] **Step 1: Teste — `RunAsync` propaga cancelamento e preenche journal quando cria fila**

```csharp
[Fact]
public async Task RunAsync_AfterAddPrinter_RecordsQueueInJournal()
{
    var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
    var mock = new Mock<IRemotePrinterOperations>();
    mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { expectedDriver });
    mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var journal = new DeploymentRollbackJournal();
    var sut = new PrinterDeploymentOrchestrator(mock.Object);
    var request = new PrinterDeploymentRequest
    {
        TargetComputerNames = new[] { "pc1" },
        Printers = new[] { new PrinterQueueDefinition { Brand = PrinterBrand.Lexmark, DisplayName = "Q1", PrinterHostAddress = "10.0.0.5", PortNumber = 9100, Protocol = TcpPrinterProtocol.Raw } },
        DomainCredential = new NetworkCredential("u", "p"),
        PrintTestPage = false
    };

    await sut.RunAsync(request, journal, new Progress<DeploymentProgressEvent>(_ => { }), CancellationToken.None);

    Assert.Single(journal.QueueEntries);
    Assert.Equal("Q1", journal.QueueEntries[0].PrinterName);
    Assert.Empty(journal.PortOnlyEntries);
}
```

Adicionar teste com `CancellationTokenSource.Cancel()` **depois** da primeira chamada simulada (usar `TaskCompletionSource` ou contador no `Setup`) para garantir que `OperationCanceledException` sai e que `journal` contém **só** porta ou **só** fila conforme o ponto de cancelamento.

- [ ] **Step 2: Alterar assinatura**

```csharp
public async Task RunAsync(
    PrinterDeploymentRequest request,
    DeploymentRollbackJournal rollbackJournal,
    IProgress<DeploymentProgressEvent> progress,
    CancellationToken cancellationToken = default)
```

Propagar `cancellationToken` a **todas** as chamadas `_remote.*Async(..., cancellationToken)` no ficheiro. Após `await _remote.CreateTcpPrinterPortAsync(...)`, chamar `rollbackJournal.RecordPortCreated(computer, portName);` Depois `cancellationToken.ThrowIfCancellationRequested();` Antes de `AddPrinterAsync`. Após `AddPrinterAsync` com sucesso: `rollbackJournal.RecordQueueCreated(computer, displayName, portName);`

No ramo `PrinterQueueExistsAsync == true`: **não** registar journal (mantém spec A).

Se `CreateTcpPrinterPortAsync` falhar: **não** registar porta.

Se `AddPrinterAsync` lançar: a porta já registada permanece em `PortOnly` (correcto).

- [ ] **Step 3: `dotnet test`** — **todos** os testes Core.

---

### Task 4: DI e `MainViewModel` + UI

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.resx` (e Designer regenerado pelo VS/`dotnet` se aplicável)
- Optional: `src/PrinterInstall.Core/Models/TargetMachineState.cs`, `TargetMachineStateDisplay.cs`

- [ ] **Step 1: Registar `DeploymentRollbackRunner`**

```csharp
builder.Services.AddSingleton<DeploymentRollbackRunner>(sp =>
    new DeploymentRollbackRunner(
        sp.GetRequiredService<IRemotePrinterOperations>(),
        sp.GetRequiredService<PrinterControlOrchestrator>()));
```

- [ ] **Step 2: `MainViewModel`** — injectar `DeploymentRollbackRunner`. Em `DeployAsync`:

```csharp
using var deployCts = CancellationTokenSource.CreateLinkedTokenSource(/* optional parent */);
IsDeployRunning = true;
DeployCommand.NotifyCanExecuteChanged();
CancelDeployCommand.NotifyCanExecuteChanged();

try
{
    var journal = new DeploymentRollbackJournal();
    await _orchestrator.RunAsync(request, journal, progress, deployCts.Token).ConfigureAwait(true);

    // sucesso normal: caixa de resumo actual
}
catch (OperationCanceledException)
{
    AppendLog(UiStrings.Main_DeployCancelRequested);
    if (journal.HasRollbackWork)
    {
        AppendLog(UiStrings.Main_DeployRollbackStarting);
        var rbProgress = new Progress<PrinterRemovalProgressEvent>(e =>
        {
            Application.Current.Dispatcher.Invoke(() =>
                AppendLog($"{e.ComputerName}: {e.Message}"));
        });
        await _rollbackRunner.RunAsync(journal, cred, rbProgress, CancellationToken.None).ConfigureAwait(true);
        AppendLog(UiStrings.Main_DeployRollbackFinished);
    }
    // Não mostrar MessageBox de sucesso do lote; opcional MessageBox informativo de cancelamento
}
finally
{
    IsDeployRunning = false;
    DeployCommand.NotifyCanExecuteChanged();
    CancelDeployCommand.NotifyCanExecuteChanged();
}
```

**Nota:** declarar `DeploymentRollbackJournal journal` **antes** do `try` para estar visível no `catch`.

- [ ] **Step 3: Comandos**

```csharp
private bool CanDeploy() => !IsDeployRunning;

[RelayCommand(CanExecute = nameof(CanDeploy))]
private async Task DeployAsync() { ... }

[RelayCommand(CanExecute = nameof(CanCancelDeploy))]
private void CancelDeploy()
{
    _deployCts?.Cancel();
}

private bool CanCancelDeploy() => IsDeployRunning;
```

Manter referência `CancellationTokenSource? _deployCts` criada no início de `DeployAsync` e atribuída para `CancelDeploy` a usar.

- [ ] **Step 4: `MainWindow.xaml`** — botão junto a Implantar:

```xml
<ui:Button Content="{DynamicResource Main_CancelDeployButton}" Command="{Binding CancelDeployCommand}"
           Appearance="Secondary" IsEnabled="{Binding IsDeployRunning}" Margin="8,0,0,0"/>
```

(Ajustar `Margin`/`Grid` conforme layout existente; se `RelayCommand` já gerir `CanExecute`, o `IsEnabled` pode ser redundante — usar só um mecanismo.)

- [ ] **Step 5: `Main.pt-BR.xaml`**

```xml
<sys:String x:Key="Main_CancelDeployButton">Cancelar deploy</sys:String>
```

- [ ] **Step 6: `UiStrings.resx`** — chaves sugeridas (valores em pt-BR):

- `Main_DeployCancelRequested` — «Cancelamento pedido pelo utilizador.»
- `Main_DeployRollbackStarting` — «A reverter alterações criadas nesta execução…»
- `Main_DeployRollbackFinished` — «Reversão concluída (ver log).»
- `Main_DeployCooperativeCancelHint` — (opcional) «Se uma fila tiver sido criada no alvo após o pedido de cancelamento, use Controle de impressoras para concluir a limpeza.»

- [ ] **Step 7: Estados na grelha (opcional mas recomendado)** — adicionar `TargetMachineState.Cancelled` e `TargetMachineState.RevertedAfterCancel` (ou um único `DeploymentCancelled`), actualizar `BuildSummaryText` e `TargetMachineStateDisplay`, e no `catch` actualizar linhas `Targets` afectadas pelo `journal` para «Reverted» e as pendentes para «Cancelled».

- [ ] **Step 8: Compilar solução**

Run: `dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\PrinterInstall.sln"`

---

### Task 5: Testes da App (opcional, smoke)

**Files:**
- `tests/PrinterInstall.App.Tests/...` — se existir harness para ViewModel, testar que `CancelDeploy` invoca `Cancel` no CTS (mock de orquestrador não trivial; **YAGNI** se não houver infra).

---

## Verificação manual (obrigatória antes de dar como fechado)

1. Domínio de teste, 1 PC, 1 fila nova: iniciar deploy, **Cancelar deploy** a meio; verificar log + ausência/presença de fila conforme o momento do cancel.
2. Mesmo cenário, deixar concluir uma fila, cancelar na segunda; verificar reversão remove apenas o que o journal tinha.
3. Fila já existente (`SkippedAlreadyExists`): cancelar; garantir que **não** remove fila pré-existente.

---

## Auto-revisão do plano vs spec

| Requisito na spec | Tarefa |
|-------------------|--------|
| Botão «Cancelar deploy» | Task 4, `Main.pt-BR.xaml` |
| Journal na Core, critério A | Task 1–3 |
| Cancelamento cooperativo + entre passos | Task 3 |
| Reutilizar política porta órfã / controlo | Task 2 (`PrinterControlOrchestrator` + port-only) |
| Não reverter drivers | Task 2/3 (só fila/porta) |
| Log e resumo não como sucesso normal | Task 4 |
| Caso-limite cooperativo documentado | `UiStrings` opcional + comentário no plano de testes manuais |

**Placeholder scan:** removido o excerto inválido com `GetType()`; usar `IRemotePrinterOperations` no runner.

**Consistência de tipos:** `DeploymentRollbackQueueEntry`, `DeploymentRollbackJournal`, assinatura única de `RunAsync` com journal obrigatório nos testes.

---

**Plano guardado em:** `docs/superpowers/plans/2026-04-29-deploy-cancel-rollback.md`.

**Execução:** duas opções habituais —

1. **Subagent-driven (recomendado)** — um subagente por tarefa, revisão entre tarefas.  
2. **Execução em linha** — seguir as tarefas nesta sessão com checkpoints.

Qual abordagem preferes para implementar?
