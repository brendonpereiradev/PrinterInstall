# Lote de múltiplas impressoras — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir K definições de impressora a aplicar a N computadores em uma execução, com ignição quando a fila já existe, continuação em erro, progresso e resumo alinhados a `docs/superpowers/specs/2026-04-27-lote-multiplas-impressoras-design.md`.

**Architecture:** O modelo `PrinterDeploymentRequest` passa a levar `IReadOnlyList<PrinterQueueDefinition>` (≥1). O orquestrador reestrutura-se em anel externo = PC, interno = definição; no início de cada PC obtém a lista de drivers **uma vez**, resolve/instala drivers para **cada marca distinta** do lote (melhor esforço por marca) antes de qualquer fila, depois, por definição, `PrinterQueueExistsAsync` → porto/fila/página de teste. O contrato remoto ganha `PrinterQueueExistsAsync` (WinRM + CIM + `Composite`). `DeploymentProgressEvent` ganha `string? PrinterQueueName`; novo `TargetMachineState.SkippedAlreadyExists`. A UI WPF mostra **linhas editáveis** de impressoras, uma **grelha de estado** com uma linha por (PC, nome da fila) e, no fim, resumo com **copiar para área de transferência**.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Moq, xUnit. Testes: `dotnet test` na solução `PrinterInstall.sln`.

**Ficheiros a mapear (responsabilidade):**

| Ficheiro | Papel após a feature |
|----------|------------------------|
| `src/PrinterInstall.Core/Models/PrinterQueueDefinition.cs` (novo) | Registo inalterável de uma impressora no lote. |
| `src/PrinterInstall.Core/Models/PrinterDeploymentRequest.cs` | `TargetComputerNames` + `Printers` + `DomainCredential` + `PrintTestPage` (remover campos de impressora única). |
| `src/PrinterInstall.Core/Models/DeploymentProgressEvent.cs` | Quarto membro `PrinterQueueName` opcional. |
| `src/PrinterInstall.Core/Models/TargetMachineState.cs` | Valor `SkippedAlreadyExists`. |
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | `PrinterQueueExistsAsync` com default `NotSupportedException` alinhado aos outros *defaults* do ficheiro. |
| `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` | `Get-Printer -Name` (ou teste de existência equivalente) via `IPowerShellInvoker`. |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | Método público reutilizando a mesma consulta WQL que `PrinterExists` (refactor para método instance ou estático reutilizável). |
| `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` | Fallback WinRM → CIM como `AddPrinterAsync`. |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | Lógica aninhada, instalação por marcas, *skip* e erros por fila. |
| `src/PrinterInstall.App/ViewModels/PrinterFormRowViewModel.cs` (novo) | Uma linha do formulário: marca, nome, anfitrião, porta, protocolo. |
| `src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs` | `PrinterQueueName` (nome da fila) para a grelha; chave = PC + fila. |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | `ObservableCollection<PrinterFormRowViewModel>`, validação, construção do pedido, *progress* que actualiza a grelha e o log, resumo e copiar. |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Secção de linhas (DataGrid editável / ItemsControl) + colunas de estado. |
| `src/PrinterInstall.App/Localization/TargetMachineStateDisplay.cs` + `Main.pt-BR.xaml` + `Resources/UiStrings.resx` | Cadeias pt-BR novas. |
| `tests/PrinterInstall.Core.Tests/.../PrinterDeploymentOrchestratorTests.cs` | Pedido com lista; novos testes (ordem, *skip*, continuação, uma listagem de drivers). |
| `tests/PrinterInstall.Core.Tests/.../PrinterDeploymentOrchestratorDriverInstallTests.cs` | `MakeRequest` com `Printers`. |
| `tests/PrinterInstall.App.Tests/.../TargetMachineStateDisplayTests.cs` | Etiqueta para o novo estado. |

---

### Task 1: Modelo `PrinterQueueDefinition` e `PrinterDeploymentRequest`

**Files:**

- Create: `src/PrinterInstall.Core/Models/PrinterQueueDefinition.cs`
- Modify: `src/PrinterInstall.Core/Models/PrinterDeploymentRequest.cs`

- [ ] **Step 1: Criar o registo de definição**

```csharp
namespace PrinterInstall.Core.Models;

public sealed class PrinterQueueDefinition
{
    public required PrinterBrand Brand { get; init; }
    public required string DisplayName { get; init; }
    public required string PrinterHostAddress { get; init; }
    public required int PortNumber { get; init; }
    public required TcpPrinterProtocol Protocol { get; init; }
}
```

- [ ] **Step 2: Alterar o pedido de deploy (substituir os campos de impressora única)**

O ficheiro `PrinterDeploymentRequest.cs` deve conter **apenas** o que o deploy precisa a nível de pedido, por exemplo:

```csharp
using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterDeploymentRequest
{
    public required IReadOnlyList<string> TargetComputerNames { get; init; }
    public required IReadOnlyList<PrinterQueueDefinition> Printers { get; init; }
    public required NetworkCredential DomainCredential { get; init; }
    public bool PrintTestPage { get; init; }
}
```

- [ ] **Step 3: Compilar só o Core (espera erros ainda nos consumidores)**

Run (PowerShell, directório do repositório):

```powershell
dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" --no-restore
```

Expected: possíveis erros noutros projectos; o Core sozinho **deve** compilar com `--no-restore` apenas se o restore já existir, caso contrário `dotnet build` no `.csproj` sem `--no-restore`.

---

### Task 2: `TargetMachineState` e `DeploymentProgressEvent`

**Files:**

- Modify: `src/PrinterInstall.Core/Models/TargetMachineState.cs`
- Modify: `src/PrinterInstall.Core/Models/DeploymentProgressEvent.cs`

- [ ] **Step 1: Adicionar estado `SkippedAlreadyExists` ao enum (por exemplo após `CompletedSuccess`)**

```csharp
public enum TargetMachineState
{
    // ... existentes ...
    CompletedSuccess,
    SkippedAlreadyExists,
    AbortedDriverMissing,
    Error
}
```

- [ ] **Step 2: Alargar o *record* de evento (último parâmetro com valor por defeito)**

```csharp
namespace PrinterInstall.Core.Orchestration;

public sealed record DeploymentProgressEvent(
    string ComputerName,
    TargetMachineState State,
    string Message,
    string? PrinterQueueName = null);
```

- [ ] **Step 3: Compilar o Core e localizar *breakages***

Run:

```powershell
dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj"
```

---

### Task 3: `PrinterQueueExistsAsync` (interface + três implementações)

**Files:**

- Modify: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`

- [ ] **Step 1: Interface — novo método (default que falha, consistente com o ficheiro)**

```csharp
Task<bool> PrinterQueueExistsAsync(
    string computerName,
    NetworkCredential credential,
    string printerDisplayName,
    CancellationToken cancellationToken = default)
    => throw new NotImplementedException();
```

- [ ] **Step 2: WinRM — `Get-Printer` / existência; devolver `true` se existir, `false` se não; não lançar se “não encontrado”**

Exemplo de script *inner* (reutilise `Escape` existente no ficheiro):

```csharp
public Task<bool> PrinterQueueExistsAsync(
    string computerName,
    NetworkCredential credential,
    string printerDisplayName,
    CancellationToken cancellationToken = default)
{
    var inner = $@"
$p = Get-Printer -Name '{Escape(printerDisplayName)}' -ErrorAction SilentlyContinue
if ($null -ne $p) {{ 'true' }} else {{ 'false' }}
";
    // Invocar via _invoker; normalizar: true se a última linha útil (após trim) for "true" (case-insensitive)
}
```

Use o mesmo padrão de `InvokeOnRemoteRunspaceAsync` que retorna `IReadOnlyList<string>`: interpretar a última linha não vazia como `bool`.

- [ ] **Step 3: CIM — expor a consulta; delegar a `Task.Run` e `CreateScope` como noutros métodos**

Reutilize a lógica de `PrinterExists` existente: extraia para

```csharp
public Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken = default)
{
    return Task.Run(() =>
    {
        var scope = CreateScope(computerName, credential);
        scope.Connect();
        return PrinterExists(scope, printerDisplayName);
    }, cancellationToken);
}
```

(ajustando a visibilidade de `PrinterExists` para `private static` com assinatura `(ManagementScope, string)` já existente.)

- [ ] **Step 4: `Composite` — try primário, em falha (não `OperationCanceled`) tentar *fallback*; devolver o resultado do *fallback***

Copie a estrutura de `AddPrinterAsync` (linhas 46–55 do ficheiro) aplicada a `bool`.

- [ ] **Step 5: Compilar a solução Core**

```powershell
dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj"
```

---

### Task 4: `PrinterDeploymentOrchestrator` — reestruturar e manter *driver install*

**Files:**

- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`

- [ ] **Step 1: Método auxiliar `MapProtocol` e `BuildPortName`** — *sem alterar a semântica*; aplicar a **cada** definição com `def.PrinterHostAddress`, `def.PortNumber`, `def.Protocol`.

- [ ] **Step 2: Substituição de `TryInstallMissingDriverAsync`** — assinatura passando `PrinterBrand brand` e `string expected` em vez de ler `request.Brand`; pacote: `_localDrivers.TryGet(brand)`; revalidar drivers após instalação como hoje. Todas as `DeploymentProgressEvent` de instalação de driver: `PrinterQueueName: null` (nível do PC/Marca).

- [ ] **Step 3: Por cada `computer` em `request.TargetComputerNames`:**

  1. `ContactingRemote` + `GetInstalledDriverNamesAsync` (uma chamada) — *catch* a nível de PC: se falhar, `Error` e `continue` para o próximo PC.
  2. Construir a lista de **marcas distintas** (ordem: primeira ocorrência em `request.Printers`).
  3. Para cada marca `B` nessa lista: `expected = PrinterCatalog.GetExpectedDriverName(B)`; se `DriverNameMatcher.IsDriverInstalled` já satisfaz, continuar; senão `await TryInstallMissingDriverAsync(..., B, expected, ...)`; se após a tentativa ainda faltar, marcar a marca `B` como *indisponível* para este PC (ex.: `HashSet<PrinterBrand>`) e as filas com essa marca reportarão `AbortedDriverMissing` e **não** tentarão criar porta — **não** aborte o resto do PC (outras marcas continuam) — alinhado à spec.
  4. Anel interno: para cada `def` em `request.Printers`:
     - `cancellationToken.ThrowIfCancellationRequested()` no início da iteração.
     - Se `def.Brand` está no conjunto *indisponível*: `AbortedDriverMissing` com `PrinterQueueName = def.DisplayName`, `continue`.
     - Se `await _remote.PrinterQueueExistsAsync(..., def.DisplayName.Trim(), ...) == true`: `SkippedAlreadyExists` com mensagem p.ex. `"Skipped — queue already exists"` e `PrinterQueueName = def.DisplayName`.
     - Senão: validar *de novo* `expected` **para a marca** (defesa); `Configuring` → `CreateTcpPrinterPort` (nome = `BuildPortName` da def) → `AddPrinterAsync` → se `request.PrintTestPage`, página de teste (mesma semântica de excepção *soft* de teste que hoje). Eventos de sucesso/erro de teste: `PrinterQueueName = def.DisplayName`.
  5. Envolver o passo 4.4 num `try`/`catch` **por definição**: em excepção não cancelada, `Error` com `PrinterQueueName = def.DisplayName` e `continue` (não sair do PC).

- [ ] **Step 4: Garantir que *todas* as chamadas a `new DeploymentProgressEvent` passam `PrinterQueueName` quando o evento se refere a uma fila; `null` para fases gerais do PC.**

- [ ] **Step 5: Compilar o Core**

```powershell
dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj"
```

---

### Task 5: Testes do Core (actualizar existentes e novos)

**Files:**

- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorMultiPrinterTests.cs` (nome sugerido)

- [ ] **Step 1: Helper de pedido mínimo (repetir padrão do `MakeRequest` actual)**

```csharp
private static PrinterQueueDefinition P(PrinterBrand b, string name = "P1", string host = "10.0.0.1") => new()
{
    Brand = b,
    DisplayName = name,
    PrinterHostAddress = host,
    PortNumber = 9100,
    Protocol = TcpPrinterProtocol.Raw
};

private static PrinterDeploymentRequest R(IReadOnlyList<string> pcs, params PrinterQueueDefinition[] defs) => new()
{
    TargetComputerNames = pcs,
    Printers = defs,
    DomainCredential = new NetworkCredential("u", "p"),
    PrintTestPage = false
};
```

- [ ] **Step 2: Refactor de todos os `new PrinterDeploymentRequest` nos ficheiros de teste** para o formato com `Printers = new[] { P(...) }`.

- [ ] **Step 3: Em todos os *mocks* `Mock<IRemotePrinterOperations>`**, adicionar:

```csharp
mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(false);
```

a menos que o teste verifique o *skip* (então `ReturnsAsync(true)` para um nome concreto).

- [ ] **Step 4: Novo teste `RunAsync_QueueExists_SkipsPortAndAdd`**

*Request:* `R(new[] { "pc1" }, P(PrinterBrand.Epson, "Q1", "10.0.0.1"), P(PrinterBrand.Epson, "Q2", "10.0.0.2"))`.

```csharp
[Fact]
public async Task RunAsync_QueueExists_SkipsPortAndAdd()
{
    var epson = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
    var m = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
    m.Setup(x => x.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { epson });
    m.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(true);
    m.Setup(x => x.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q2", It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    m.Setup(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.2", 9100, "RAW", It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    m.Setup(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "Q2", epson, It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var sut = new PrinterDeploymentOrchestrator(m.Object);
    var events = new List<DeploymentProgressEvent>();
    await sut.RunAsync(
        R(new[] { "pc1" },
            P(PrinterBrand.Epson, "Q1", "10.0.0.1"),
            P(PrinterBrand.Epson, "Q2", "10.0.0.2")),
        new Progress<DeploymentProgressEvent>(events.Add));

    m.Verify(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.1", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    m.Verify(x => x.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), "Q1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    m.Verify(x => x.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.2", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    Assert.Contains(events, e => e is { State: TargetMachineState.SkippedAlreadyExists, PrinterQueueName: "Q1" });
}
```

(Assinatura de `CreateTcpPrinterPortAsync` no projecto: `portName, printerHostAddress, portNumber, protocol` — alinhar o *quarto* parâmetro ao anfitrião da definição em cada *Setup* / *Verify*.)

- [ ] **Step 5: Novo teste `RunAsync_Pc1ThenPcsOrder_ProcessesPrinterOrderInsidePc`**
Dois computadores, duas definições; rastrear a ordem das chamadas a `AddPrinter` (ou lista de *events*): primeiro PC, ordem *Q1* *Q2*; depois segundo PC, mesma ordem.

- [ ] **Step 6: Novo teste `GetInstalledDriverNamesAsync_OncePerComputer`**
Dois PC, duas definições; `Verify` `GetInstalledDriverNamesAsync` **exatamente uma vez** por `computerName` (não duas vezes no mesmo PC antes dos *queues*).

- [ ] **Step 7: Executar testes do Core**

```powershell
dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --no-build
```

Use `dotnet test ...` **sem** `--no-build` na primeira execução após alterações.

---

### Task 6: WPF — linhas de formulário, grelha (PC + fila), resumo, recursos

**Files:**

- Create: `src/PrinterInstall.App/ViewModels/PrinterFormRowViewModel.cs`
- Modify: `src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/Localization/TargetMachineStateDisplay.cs`
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` (cabeçalhos, rótulos)
- Modify: `src/PrinterInstall.App/Resources/UiStrings.resx` + `UiStrings.Designer.cs` (gerado pelo Visual Studio se aplicável; caso contrário adicionar propriedades à mão coerente com o `.resx`)
- Modify: `tests/PrinterInstall.App.Tests/Localization/TargetMachineStateDisplayTests.cs`

- [ ] **Step 1: `TargetRowViewModel` — adicionar**

```csharp
[ObservableProperty] private string _printerQueueName = "";
```

O *binding* da grelha: coluna *Computador* = `ComputerName`, coluna *Fila* = `PrinterQueueName`, *Estado* = `StateDisplay`, *Mensagem* = `Message`.

- [ ] **Step 2: `PrinterFormRowViewModel`** (ObservableObject) com: `PrinterBrand Brand`, `string DisplayName`, `string PrinterHost`, `int PortNumber` (default 9100), `TcpPrinterProtocol Protocol`, `IEnumerable<PrinterBrand> Brands` (ou bind ao enum a partir de `MainViewModel`).

- [ ] **Step 3: `MainViewModel`**

  - `ObservableCollection<PrinterFormRowViewModel> PrinterRows` com **uma** linha inicial (equivalente ao ecrã antigo: Epson, porta 9100, *Raw*).
  - Comandos `AddPrinterRowCommand` / `RemovePrinterRowCommand` (remover proíbe ficar com zero linhas).
  - `DeployAsync`: após validar PCs, para cada *nome* válido, para **cada** `PrinterRow` com sucesso, `Targets.Add(new TargetRowViewModel { ComputerName = n, PrinterQueueName = row.DisplayName.Trim(), State = Pending })` — ordem: **foreach computer, foreach row** (como a ordem de processamento do orquestrador, para a grelha reflectir a mesma ordem lógica).
  - `BuildRequest`: `Printers` = selecção das linhas com `DisplayName` e `Host` preenchidos; `PortNumber` validado; `bool PrintTestPage` inalterado.
  - *Progress* handler: localizar a linha `TargetRowViewModel` onde `t.ComputerName == e.ComputerName && (e.PrinterQueueName == null || (t.PrinterQueueName == e.PrinterQueueName))` — para `PrinterQueueName == null` (fases de PC), actualizar **todas** as linhas com o mesmo `ComputerName` (regra da spec: eventos a nível de PC afectam o estado geral; implementação mínima: actualizar *todas* as linhas desse PC com o mesmo *State* e *Message*; quando for evento *queue-specific*, exact match).
  - *Log* textual: continuar a concatenar, incluindo o nome da fila quando `e.PrinterQueueName` não for nulo: `ex.: $"{e.ComputerName} [{e.PrinterQueueName ?? "-"}]: ..."`
  - Após `RunAsync`, chamar `BuildSummaryString()` (contar `SkippedAlreadyExists`, `Error`, `CompletedSuccess` no `Targets`) e guardar em propriedade `LastSummaryText` para `MessageBox` + botão *Copiar resumo* (`RelayCommand` que coloca `LastSummaryText` no `Clipboard`).

- [ ] **Step 4: `MainWindow.xaml`**  
Substituir o painel direito (marca, nome, anfitrião únicos) por: `DataGrid` *ou* lista com `ItemsSource={Binding PrinterRows}`; colunas para marca, nome, anfitrião, porta, protocolo; botões *Adicionar impressora* / *Remover linha*. A grelha de *status* (secção inferior do painel esquerdo): cabeçalho actualizado com coluna *Fila*.

- [ ] **Step 5: `TargetMachineStateDisplay`** — mapear `SkippedAlreadyExists` → `"Ignorado (já existia)"` (ou texto escolhido, consistente com o `.xaml`).

- [ ] **Step 6: Teste de localização** —

```csharp
[Fact]
public void SkippedAlreadyExists_returns_Portuguese_label()
{
    Assert.Equal("Ignorado (já existia)", TargetMachineStateDisplay.GetDisplay(TargetMachineState.SkippedAlreadyExists));
}
```

(ajustar a *string* ao que fixar no `TargetMachineStateDisplay`).

- [ ] **Step 7: Compilar o App e testes**

```powershell
dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\PrinterInstall.sln"
```

---

## Auto-revisão (checklist do plano)

1. **Cobertura da spec:** Secção 3.1 (drivers por PC + marcas) → Task 4. Secção 3.2 (exists, *skip*) → Task 3 + Task 4. Secção 4.4 (eventos) → Task 2 + Task 6. Secção 4.5 (UI) → Task 6. Testes spec §6 → Task 5.  
2. **Placeholders:** nenhum `TBD` deliberado; os trechos C# de `WinRm` “interpretar *bool*” têm de ser completados com a lógica de *parse* real na implementação.  
3. **Consistência de tipos:** `PrinterQueueDefinition`, `Printers` em `PrinterDeploymentRequest`, `PrinterQueueName` em `DeploymentProgressEvent` e `TargetRowViewModel` usam o mesmo *DisplayName* da definição.

---

**Plano concluído e guardado em `docs/superpowers/plans/2026-04-27-lote-multiplas-impressoras.md`.**

**Duas opções de execução:**

1. **Subagent-Driven (recomendado)** — um subagente por tarefa, revisão entre tarefas, iteração rápida. *Sub-skill requerida:* `superpowers:subagent-driven-development`

2. **Inline Execution** — executar tarefas nesta sessão com a skill `executing-plans` e *checkpoints* para revisão.

**Qual abordagem preferes?**
