# Auto test page after printer deployment — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Após criar a fila com sucesso, enviar a página de teste **só quando** `PrinterDeploymentRequest.PrintTestPage` for verdadeiro (opt-in na UI: checkbox desmarcado por defeito, sem persistência entre sessões). Via WinRM (`Print-TestPage`) com fallback WMI (`Win32_Printer.PrintTestPage`); falhas da página de teste não revertem a configuração nem marcam `Error`.

**Architecture:** Método `PrintTestPageAsync` em `IRemotePrinterOperations` e implementações WinRM/CIM/Composite. `PrinterDeploymentOrchestrator` só invoca o envio quando `request.PrintTestPage`; com `try/catch` local que mapeia falha para `CompletedSuccess` com mensagem de aviso. `MainViewModel` + `MainWindow` expõem o checkbox e preenchem o pedido.

**Tech stack:** .NET 8, C#, Moq/xUnit nos testes existentes.

**Spec:** `docs/superpowers/specs/2026-04-20-auto-test-page-after-deployment-design.md`

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | Declarar `PrintTestPageAsync` |
| `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` | PowerShell remoto: `Import-Module PrintManagement` + `Print-TestPage` |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | WMI `Win32_Printer` + `InvokeMethod("PrintTestPage", ...)` |
| `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` | Fallback primary → fallback |
| `src/PrinterInstall.Core/Models/PrinterDeploymentRequest.cs` | Propriedade `PrintTestPage` (predefinição `false`) |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | Enviar página de teste só se `PrintTestPage`; senão `CompletedSuccess` + `"Done"` |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Propriedade `PrintTestPage` → request |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | `CheckBox` + recurso pt-BR |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | `Main_PrintTestPageLabel` |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs` | Verificar chamada com flag `true`; `Never` com flag `false` |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs` | Mock estrito + fluxo com instalação de driver |
| `tests/PrinterInstall.Core.Tests/Remote/CompositeRemotePrinterOperationsTests.cs` | Fallback de `PrintTestPageAsync` |

---

### Task 1: Contrato e implementação remota

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`

- [ ] **Step 1: Adicionar método à interface**

Em `IRemotePrinterOperations.cs`, após `AddPrinterAsync`, adicionar:

```csharp
Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implementar WinRM**

Em `WinRmRemotePrinterOperations.cs`, adicionar:

```csharp
public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
{
    var inner = $"""
Import-Module PrintManagement -ErrorAction Stop
Print-TestPage -PrinterName '{Escape(printerQueueName)}'
""";
    return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
}
```

(Usar string verbatim de múltiplas linhas como acima; garantir que `Escape` já existente é utilizado.)

- [ ] **Step 3: Implementar CIM/WMI**

Em `CimRemotePrinterOperations.cs`, adicionar método público:

```csharp
public Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
{
    return Task.Run(() =>
    {
        var scope = CreateScope(computerName, credential);
        scope.Connect();

        var query = new ObjectQuery($"SELECT * FROM Win32_Printer WHERE Name='{EscapeWql(printerQueueName)}'");
        using var searcher = new ManagementObjectSearcher(scope, query);

        foreach (ManagementObject mo in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (mo)
            {
                mo.InvokeMethod("PrintTestPage", Array.Empty<object>());
                return;
            }
        }

        throw new InvalidOperationException($"Printer queue not found for test page: {printerQueueName}");
    }, cancellationToken);
}
```

- [ ] **Step 4: Implementar Composite**

Em `CompositeRemotePrinterOperations.cs`, após `AddPrinterAsync`, adicionar:

```csharp
public async Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)
{
    try
    {
        await _primary.PrintTestPageAsync(computerName, credential, printerQueueName, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        await _fallback.PrintTestPageAsync(computerName, credential, printerQueueName, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Compilar Core**

Run: `dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Debug`  
Expected: Build **succeeded** (0 errors).

- [ ] **Step 6: Commit** (omitir até autorização explícita do utilizador)

```bash
git add src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs
git commit -m "feat(core): add remote PrintTestPage for WinRM and WMI"
```

---

### Task 2: Orquestrador

**Files:**
- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`

- [ ] **Step 1: Inserir envio da página de teste após AddPrinter**

Substituir o bloco que hoje faz só `AddPrinterAsync` seguido de `CompletedSuccess` por lógica equivalente a:

```csharp
await _remote.AddPrinterAsync(computer, request.DomainCredential, request.DisplayName, expected, portName, cancellationToken).ConfigureAwait(false);
progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Sending test page..."));
try
{
    await _remote.PrintTestPageAsync(computer, request.DomainCredential, request.DisplayName, cancellationToken).ConfigureAwait(false);
    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, "Done"));
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, $"Done — test page failed: {Flatten(ex)}"));
}
```

Manter o resto do método `RunAsync` inalterado (incluindo o `catch` exterior que mapeia erros para `TargetMachineState.Error`).

- [ ] **Step 2: Compilar solução**

Run: `dotnet build "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\PrinterInstall.sln" -c Debug`  
Expected: Build **succeeded**.

- [ ] **Step 3: Commit** (omitir até autorização)

```bash
git add src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs
git commit -m "feat(core): send test page after successful printer add"
```

---

### Task 3: Testes do orquestrador

**Files:**
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`

- [ ] **Step 1: Actualizar `RunAsync_DriverPresent_CreatesPortAndPrinter`**

Após o `Setup` de `AddPrinterAsync`, adicionar:

```csharp
mock.Setup(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

Antes do `Assert`, adicionar:

```csharp
mock.Verify(m => m.PrintTestPageAsync("pc1", It.IsAny<NetworkCredential>(), "Office", It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 2: Adicionar teste de falha da página de teste**

Novo método na mesma classe:

```csharp
[Fact]
public async Task RunAsync_TestPageFails_StillReportsCompletedSuccess_WithWarning()
{
    var expectedDriver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Lexmark);
    var mock = new Mock<IRemotePrinterOperations>();
    mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { expectedDriver });
    mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    mock.Setup(m => m.PrintTestPageAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("spooler"));

    var sut = new PrinterDeploymentOrchestrator(mock.Object);
    var request = new PrinterDeploymentRequest
    {
        TargetComputerNames = new[] { "pc1" },
        Brand = PrinterBrand.Lexmark,
        DisplayName = "Office",
        PrinterHostAddress = "10.0.0.5",
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw,
        DomainCredential = new NetworkCredential("u", "p")
    };

    var events = new List<DeploymentProgressEvent>();
    await sut.RunAsync(request, new Progress<DeploymentProgressEvent>(events.Add));

    var done = Assert.Single(events.Where(e => e.State == TargetMachineState.CompletedSuccess));
    Assert.Contains("test page failed", done.Message, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("spooler", done.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Actualizar teste de driver (`MockBehavior.Strict`)**

Em `PrinterDeploymentOrchestratorDriverInstallTests.cs`, método `DriverMissing_PackageAvailable_InstallSucceeds_Reconfirms_ContinuesFlow`, após o `Setup` de `AddPrinterAsync`, adicionar:

```csharp
remote.Setup(m => m.PrintTestPageAsync("pc1", It.IsAny<NetworkCredential>(), "P1", It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

- [ ] **Step 4: Executar testes Core**

Run: `dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Debug --no-build`  
(Se `--no-build` falhar por binários antigos, omitir `--no-build`.)

Expected: todos os testes **Passed**.

- [ ] **Step 5: Commit** (omitir até autorização)

```bash
git add tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs
git commit -m "test(core): cover PrintTestPage orchestration"
```

---

### Task 4: Testes do Composite

**Files:**
- Modify: `tests/PrinterInstall.Core.Tests/Remote/CompositeRemotePrinterOperationsTests.cs`

- [ ] **Step 1: Adicionar teste de fallback**

```csharp
[Fact]
public async Task PrintTestPageAsync_WhenPrimaryThrows_UsesFallback()
{
    var primary = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
    var fallback = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
    primary.Setup(p => p.PrintTestPageAsync("pc", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("winrm down"));
    fallback.Setup(p => p.PrintTestPageAsync("pc", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var sut = new CompositeRemotePrinterOperations(primary.Object, fallback.Object);
    await sut.PrintTestPageAsync("pc", new NetworkCredential("u", "p"), "Q1");

    primary.VerifyAll();
    fallback.VerifyAll();
}
```

- [ ] **Step 2: Executar testes**

Run: `dotnet test "c:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Debug`  
Expected: **Passed**.

- [ ] **Step 3: Commit** (omitir até autorização)

```bash
git add tests/PrinterInstall.Core.Tests/Remote/CompositeRemotePrinterOperationsTests.cs
git commit -m "test(core): composite PrintTestPage fallback"
```

---

## Revisão do plano vs spec

| Requisito na spec | Tarefa |
|-------------------|--------|
| Gatilho após `AddPrinter` bem-sucedido | Task 2 |
| WinRM + WMI + Composite | Task 1 |
| Falha da página de teste → `CompletedSuccess` com aviso | Task 2 + teste Task 3 |
| Cancelamento não tratado como aviso | Filtro `when (ex is not OperationCanceledException)` em Task 2 |
| Nome da fila = `DisplayName` | Chamadas com `request.DisplayName` em Task 2 + asserts Task 3 |

---

**Plano guardado em** `docs/superpowers/plans/2026-04-20-auto-test-page-after-deployment.md`.

**Spec guardada em** `docs/superpowers/specs/2026-04-20-auto-test-page-after-deployment-design.md` (**sem commit em git** — confirme se deseja `git add` / `git commit` destes ficheiros).

**Opções de execução:**

1. **Subagent-driven (recomendado)** — um subagente por tarefa, revisão entre tarefas (skill `subagent-driven-development`).
2. **Execução inline** — tarefas em sequência nesta conversa com checkpoints (skill `executing-plans`).

Qual prefere?
