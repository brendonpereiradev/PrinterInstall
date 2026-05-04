# Validação Lexmark Universal v2 e v4 XL — Plano de implementação

> **Para agentes:** sub-skill obrigatório recomendado: `superpowers:subagent-driven-development` ou `superpowers:executing-plans`. Os passos usam checkbox (`- [ ]`) para acompanhamento.

**Goal:** Aceitar **`Lexmark Universal v2 XL`** e **`Lexmark Universal v4 XL`** como drivers válidos para Lexmark, resolver o nome instalado na ordem de preferência (**v4** antes de **v2**) ao criar filas e alinhar mensagens de erro / reconfirmação após instalação remota, conforme `docs/superpowers/specs/2026-05-04-lexmark-driver-v2-v4-validation-design.md`.

**Architecture:** O catálogo passa a expor uma lista **ordenada** por marca (`GetDriverResolutionOrder`) mantendo `GetExpectedDriverName` como nome **principal** do pacote local (**v4** para Lexmark — usado por `LocalDriverPackage` / scripts de instalação). `DriverNameMatcher` ganha dois métodos estáticos: verificar se **algum** nome aceite está instalado e **resolver** qual usar na API remota. `PrinterDeploymentOrchestrator` substitui todas as verificações que hoje usam um único `expected` por estas APIs; `AddPrinterAsync` recebe sempre o nome **resolvido**.

**Tech stack:** .NET 8, C#, xUnit, Moq.

**Commits:** O proprietário pode pedir commits apenas quando quiser; os passos de *commit* são checkpoints sugeridos e podem ser omitidos ou agrupados.

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Catalog/PrinterCatalog.cs` | Lista ordenada Lexmark `[v4, v2]`; `GetExpectedDriverName` inalterado para pacote v4; novo `GetDriverResolutionOrder`; novo `DescribeAcceptableDrivers` para mensagens. |
| `src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs` | `IsAnyAcceptedDriverInstalled`, `ResolveInstalledDriverName` (reutilizam `IsDriverInstalled`). |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | Ciclo por marca e por fila usam ordem + matcher novo; `TryInstallMissingDriverAsync` sem parâmetro `string expected`; mensagens com `DescribeAcceptableDrivers`. |
| `src/PrinterInstall.Core/Drivers/LocalDriverPackageCatalog.cs` | Sem alteração obrigatória (continua `GetExpectedDriverName` = v4 Lexmark). |
| `tests/PrinterInstall.Core.Tests/Catalog/PrinterCatalogTests.cs` | Testes para ordem Lexmark e `DescribeAcceptableDrivers`. |
| `tests/PrinterInstall.Core.Tests/Drivers/DriverNameMatcherTests.cs` | Testes para `IsAnyAcceptedDriverInstalled` e `ResolveInstalledDriverName`. |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs` | Novos cenários Lexmark v2-only e v2+v4; ajustes se algum teste assumir nome único onde a verificação mudou. |

---

### Task 1: `PrinterCatalog` — contrato e testes (TDD)

**Files:**
- Modify: `src/PrinterInstall.Core/Catalog/PrinterCatalog.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Catalog/PrinterCatalogTests.cs`

- [ ] **Step 1: Escrever testes que falham**

Acrescentar ao ficheiro de testes:

```csharp
[Fact]
public void GetDriverResolutionOrder_Lexmark_V4ThenV2()
{
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
    Assert.Equal(2, order.Count);
    Assert.Equal("Lexmark Universal v4 XL", order[0]);
    Assert.Equal("Lexmark Universal v2 XL", order[1]);
}

[Fact]
public void GetDriverResolutionOrder_Epson_SingleEntry()
{
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Epson);
    Assert.Single(order);
    Assert.Equal("EPSON Universal Print Driver", order[0]);
}

[Fact]
public void DescribeAcceptableDrivers_Lexmark_JoinsWithOr()
{
    var text = PrinterCatalog.DescribeAcceptableDrivers(PrinterBrand.Lexmark);
    Assert.Equal("Lexmark Universal v4 XL or Lexmark Universal v2 XL", text);
}

[Fact]
public void DescribeAcceptableDrivers_Gainscha_SingleName()
{
    Assert.Equal("Gainscha GA-2408T", PrinterCatalog.DescribeAcceptableDrivers(PrinterBrand.Gainscha));
}
```

- [ ] **Step 2: Correr testes — devem falhar a compilar ou falhar em runtime**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~PrinterCatalogTests" -v n`

Expected: erros de compilação (`GetDriverResolutionOrder` / `DescribeAcceptableDrivers` não existem) ou falhas.

- [ ] **Step 3: Implementação mínima em `PrinterCatalog.cs`**

Substituir / estender o conteúdo da classe para incluir o mapa ordenado e os métodos (mantendo `DriverNames` e `GetExpectedDriverName` como estão):

```csharp
private static readonly IReadOnlyDictionary<PrinterBrand, IReadOnlyList<string>> DriverResolutionOrder =
    new Dictionary<PrinterBrand, IReadOnlyList<string>>
    {
        [PrinterBrand.Epson] = new[] { "EPSON Universal Print Driver" },
        [PrinterBrand.Gainscha] = new[] { "Gainscha GA-2408T" },
        [PrinterBrand.Lexmark] = new[] { "Lexmark Universal v4 XL", "Lexmark Universal v2 XL" },
    };

public static IReadOnlyList<string> GetDriverResolutionOrder(PrinterBrand brand) => DriverResolutionOrder[brand];

public static string DescribeAcceptableDrivers(PrinterBrand brand)
{
    var order = GetDriverResolutionOrder(brand);
    return order.Count == 1 ? order[0] : string.Join(" or ", order);
}
```

(garantir que `using System.Collections.Generic;` existe se necessário.)

- [ ] **Step 4: Correr testes do catálogo**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~PrinterCatalogTests" -v n`

Expected: **Passed.**

- [ ] **Step 5: Commit (opcional)**

```bash
git add src/PrinterInstall.Core/Catalog/PrinterCatalog.cs tests/PrinterInstall.Core.Tests/Catalog/PrinterCatalogTests.cs
git commit -m "feat(catalog): Lexmark driver resolution order v4 before v2"
```

---

### Task 2: `DriverNameMatcher` — novos métodos e testes

**Files:**
- Modify: `src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Drivers/DriverNameMatcherTests.cs`

- [ ] **Step 1: Escrever testes que falham**

Acrescentar:

```csharp
[Fact]
public void IsAnyAcceptedDriverInstalled_LexmarkV2Only_ReturnsTrue()
{
    var installed = new[] { "Lexmark Universal v2 XL", "Other" };
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
    Assert.True(DriverNameMatcher.IsAnyAcceptedDriverInstalled(installed, order));
}

[Fact]
public void ResolveInstalledDriverName_BothV2AndV4_ReturnsV4()
{
    var installed = new[] { "Lexmark Universal v2 XL", "Lexmark Universal v4 XL" };
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
    Assert.Equal("Lexmark Universal v4 XL", DriverNameMatcher.ResolveInstalledDriverName(installed, order));
}

[Fact]
public void ResolveInstalledDriverName_OnlyV2_ReturnsV2()
{
    var installed = new[] { "Lexmark Universal v2 XL" };
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
    Assert.Equal("Lexmark Universal v2 XL", DriverNameMatcher.ResolveInstalledDriverName(installed, order));
}

[Fact]
public void ResolveInstalledDriverName_None_ReturnsNull()
{
    var installed = new[] { "Other" };
    var order = PrinterCatalog.GetDriverResolutionOrder(PrinterBrand.Lexmark);
    Assert.Null(DriverNameMatcher.ResolveInstalledDriverName(installed, order));
}
```

(adicionar `using PrinterInstall.Core.Catalog;` no ficheiro de testes.)

- [ ] **Step 2: Correr testes — falham**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~DriverNameMatcherTests" -v n`

Expected: falha de compilação ou métodos em falta.

- [ ] **Step 3: Implementação em `DriverNameMatcher.cs`**

```csharp
public static bool IsAnyAcceptedDriverInstalled(IEnumerable<string> installedDriverNames, IReadOnlyList<string> preferenceOrder)
{
    foreach (var candidate in preferenceOrder)
    {
        if (IsDriverInstalled(installedDriverNames, candidate))
            return true;
    }
    return false;
}

public static string? ResolveInstalledDriverName(IEnumerable<string> installedDriverNames, IReadOnlyList<string> preferenceOrder)
{
    foreach (var candidate in preferenceOrder)
    {
        if (IsDriverInstalled(installedDriverNames, candidate))
            return candidate.Trim();
    }
    return null;
}
```

- [ ] **Step 4: Correr testes**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~DriverNameMatcherTests" -v n`

Expected: **Passed.**

- [ ] **Step 5: Commit (opcional)**

```bash
git add src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs tests/PrinterInstall.Core.Tests/Drivers/DriverNameMatcherTests.cs
git commit -m "feat(drivers): match and resolve Lexmark v2/v4 by preference order"
```

---

### Task 3: `PrinterDeploymentOrchestrator` — orquestração e testes de integração leve

**Files:**
- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`

- [ ] **Step 1: Escrever novos testes que falham**

Acrescentar dois testes ao fim da classe `PrinterDeploymentOrchestratorTests`:

```csharp
[Fact]
public async Task RunAsync_LexmarkV2Only_UsesV2ForAddPrinter()
{
    var v2 = "Lexmark Universal v2 XL";
    var mock = new Mock<IRemotePrinterOperations>();
    mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { v2 });
    mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var sut = new PrinterDeploymentOrchestrator(mock.Object);
    var request = new PrinterDeploymentRequest
    {
        TargetComputerNames = new[] { "pc1" },
        Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
        DomainCredential = new NetworkCredential("u", "p"),
        PrintTestPage = false
    };

    await sut.RunAsync(request, new DeploymentRollbackJournal(), new Progress<DeploymentProgressEvent>(_ => { }));

    mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", v2, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task RunAsync_LexmarkBothV2AndV4_UsesV4ForAddPrinter()
{
    var v4 = "Lexmark Universal v4 XL";
    var v2 = "Lexmark Universal v2 XL";
    var mock = new Mock<IRemotePrinterOperations>();
    mock.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { v2, v4 });
    mock.Setup(m => m.PrinterQueueExistsAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    mock.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    mock.Setup(m => m.AddPrinterAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var sut = new PrinterDeploymentOrchestrator(mock.Object);
    var request = new PrinterDeploymentRequest
    {
        TargetComputerNames = new[] { "pc1" },
        Printers = new[] { OnePrinter(PrinterBrand.Lexmark, "Office", "10.0.0.5") },
        DomainCredential = new NetworkCredential("u", "p"),
        PrintTestPage = false
    };

    await sut.RunAsync(request, new DeploymentRollbackJournal(), new Progress<DeploymentProgressEvent>(_ => { }));

    mock.Verify(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Office", v4, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

- [ ] **Step 2: Correr só estes testes — devem falhar**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~RunAsync_LexmarkV2Only|FullyQualifiedName~RunAsync_LexmarkBothV2AndV4" -v n`

Expected: **Fail** (AddPrinter chamado com v4 ou não chamado com v2).

- [ ] **Step 3: Alterar `PrinterDeploymentOrchestrator.cs`**

Alterações pontuais:

1. **Bloco `foreach (var brand in brandOrder)`** (~linhas 51–59): substituir `var expected = PrinterCatalog.GetExpectedDriverName(brand)` e `IsDriverInstalled(drivers, expected)` por:

```csharp
var driverOrder = PrinterCatalog.GetDriverResolutionOrder(brand);
if (DriverNameMatcher.IsAnyAcceptedDriverInstalled(drivers, driverOrder))
    continue;
```

Chamar `TryInstallMissingDriverAsync` **sem** passar `expected`; nova assinatura:

```csharp
private async Task<(bool Success, string? ErrorForQueues)> TryInstallMissingDriverAsync(
    string computer,
    PrinterDeploymentRequest request,
    PrinterBrand brand,
    IProgress<DeploymentProgressEvent> progress,
    CancellationToken cancellationToken)
```

2. **Corpo `TryInstallMissingDriverAsync`**: definir `var acceptable = PrinterCatalog.GetDriverResolutionOrder(brand);` e `var describe = PrinterCatalog.DescribeAcceptableDrivers(brand);`. Substituir todas as interpolações que usavam `{expected}` por `{describe}` nos retornos de erro (“No local package”, “install unsupported”). Na reconfirmação após instalar, usar:

```csharp
if (!DriverNameMatcher.IsAnyAcceptedDriverInstalled(drivers, acceptable))
{
    var sample = string.Join(" | ", drivers.Take(10));
    return (false, $"Driver installed does not match expected. Expected one of: {describe}. Found: [{sample}]");
}
```

3. **Bloco por impressora** (~124–166): substituir validação `expectedDriver` por:

```csharp
var driverOrder = PrinterCatalog.GetDriverResolutionOrder(def.Brand);
var resolvedDriver = DriverNameMatcher.ResolveInstalledDriverName(drivers, driverOrder);
if (resolvedDriver is null)
{
    var describe = PrinterCatalog.DescribeAcceptableDrivers(def.Brand);
    progress.Report(new DeploymentProgressEvent(
        computer,
        TargetMachineState.AbortedDriverMissing,
        $"Driver not installed: {describe}",
        displayName));
    continue;
}
```

E em `AddPrinterAsync` usar `resolvedDriver` em vez de `expectedDriver`.

- [ ] **Step 4: Correr testes novos + regresso orquestrador**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~PrinterDeploymentOrchestratorTests" -v n`

Expected: **Passed** (incluindo os testes Lexmark existentes que usam só v4 — `ResolveInstalledDriverName` deve devolver v4 quando a lista remota contém apenas o nome v4).

- [ ] **Step 5: Commit (opcional)**

```bash
git add src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs
git commit -m "feat(orchestration): accept Lexmark v2 or v4; prefer v4 for new queues"
```

---

### Task 4: Suite completa Core e documentação do spec

**Files:**
- Opcional: `docs/superpowers/specs/2026-05-04-lexmark-driver-v2-v4-validation-design.md` (actualizar **Estado** para *Implementado* quando a funcionalidade estiver merged)

- [ ] **Step 1: Correr toda a bateria `PrinterInstall.Core.Tests`**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj -v n`

Expected: **Passed** (se `PrinterDeploymentOrchestratorDriverInstallTests` ou outros referirem mensagens literais com um único nome Lexmark, ajustar asserts — Gainscha/Epson não mudam).

- [ ] **Step 2: (Opcional) Commit final / tag**

```bash
git commit --allow-empty -m "chore: verify Lexmark v2/v4 validation suite"
```

---

## Auto-revisão do plano vs spec

| Secção do spec | Tarefa que cobre |
|----------------|------------------|
| §2.1 Nomes aceites (lista fechada, igualdade exacta) | Task 1 (`DriverResolutionOrder`) + Task 2 (`IsAnyAcceptedDriverInstalled` delega em `IsDriverInstalled`) |
| §2.2 Resolver nome para `AddPrinterAsync` | Task 2 `ResolveInstalledDriverName` + Task 3 uso no orquestrador |
| §2.3 Instalação remota + reconfirmação + mensagens “v2 ou v4” | Task 3 `TryInstallMissingDriverAsync` com `DescribeAcceptableDrivers` e `IsAnyAcceptedDriverInstalled` |
| §2.4 Sem regex | Tasks 1–3 não introduzem regex |
| §5 Critérios de verificação | Task 3 testes `RunAsync_Lexmark*` + Task 4 suite |

**Placeholder scan:** nenhum `TBD` / passos vagos sem código.

**Consistência de tipos:** `GetDriverResolutionOrder` devolve `IReadOnlyList<string>` em todo o fluxo; assinatura `TryInstallMissingDriverAsync` já não usa `string expected`.

---

## Handoff de execução

Plano gravado em `docs/superpowers/plans/2026-05-04-lexmark-driver-v2-v4-validation.md`.

**Duas formas de executar:**

1. **Subagent-driven (recomendado)** — um agente por tarefa, revisão entre tarefas. Sub-skill: `superpowers:subagent-driven-development`.

2. **Execução inline** — mesmo chat, checkpoints entre tasks. Sub-skill: `superpowers:executing-plans`.

Qual prefere?
