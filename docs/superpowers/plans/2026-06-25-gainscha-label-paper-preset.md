# Preferências de tamanho de etiqueta Gainscha — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ao instalar uma fila Gainscha nova (local ou remota), aplicar automaticamente o preset de etiqueta escolhido na UI, deixando só `USER (largura mm x altura mm)` nas preferências; rollback de fila+porta se falhar.

**Architecture:** Catálogo fixo de presets na Core; `GainschaLabelPreferenceConfigurator` aplica templates de `PrinterDriverData` capturados do driver Seagull (spike); novo método em `IRemotePrinterOperations`; orquestrador adia `RecordQueueCreated` até preferência OK e reverte fila não journalizada em falha.

**Tech stack:** .NET 8, WPF + Wpf.Ui, CommunityToolkit.Mvvm, WMI/CIM remoto, PowerShell elevado (schtasks), xUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-06-25-gainscha-label-paper-preset-design.md`

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Models/GainschaLabelPreset.cs` | Enum Pulseira/Matrix/Paciente/Dupla |
| `src/PrinterInstall.Core/Gainscha/GainschaLabelPresetCatalog.cs` | Dimensões + string `USER (...)` |
| `src/PrinterInstall.Core/Gainscha/IGainschaLabelPreferenceConfigurator.cs` | Contrato aplicar preset |
| `src/PrinterInstall.Core/Gainscha/GainschaLabelPreferenceConfigurator.cs` | Escreve `PrinterDriverData` + validação |
| `src/PrinterInstall.Core/Gainscha/GainschaLabelDriverDataPaths.cs` | Caminho registry por fila |
| `src/PrinterInstall.Core/Gainscha/Templates/*.bin` | Blobs capturados no spike (4 presets) |
| `src/PrinterInstall.Core/Models/PrinterQueueDefinition.cs` | Campo `GainschaLabelPreset?` |
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | `ConfigureGainschaLabelPresetAsync` |
| `src/PrinterInstall.Core/Remote/LocalPrinterOperations.cs` | Delegação local |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | Staging + script remoto |
| `src/PrinterInstall.Core/Remote/RoutingRemotePrinterOperations.cs` | Routing |
| `src/PrinterInstall.Core/Remote/RemoteElevatedScriptBuilder.cs` | `BuildApplyGainschaLabelPresetScript` |
| `src/PrinterInstall.Core/Remote/GainschaLabelPresetRemoteStager.cs` | Copia `.bin` + script via SMB |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | Passo pós-AddPrinter + rollback inline |
| `src/PrinterInstall.Core/Orchestration/DeploymentRollbackJournal.cs` | `AbandonPortOnly` |
| `src/PrinterInstall.App/ViewModels/PrinterFormRowViewModel.cs` | Preset + `IsGainschaBrand` |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Validação + mapeamento request |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Dropdown condicional |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | Labels pt-BR |
| `src/PrinterInstall.App/Resources/UiStrings.resx` | Mensagens de validação |
| `tests/.../Gainscha/GainschaLabelPresetCatalogTests.cs` | Catálogo |
| `tests/.../Orchestration/PrinterDeploymentOrchestratorGainschaLabelTests.cs` | Orquestrador |
| `tests/.../ViewModels/MainViewModelGainschaValidationTests.cs` | Validação UI |

---

### Task 0: Spike — capturar templates Seagull (manual, bloqueante)

**Files:**
- Create: `drivers/Gainscha/label-presets/README.md`
- Create: `drivers/Gainscha/label-presets/pulseira.driverdata.bin` (após captura)
- Create: `drivers/Gainscha/label-presets/matrix.driverdata.bin`
- Create: `drivers/Gainscha/label-presets/paciente.driverdata.bin`
- Create: `drivers/Gainscha/label-presets/dupla.driverdata.bin`

- [ ] **Step 1: Preparar impressora de referência**

Num PC Windows com driver `Gainscha GA-2408T` instalado, criar fila de teste `PrinterInstall-Spike`.

- [ ] **Step 2: Capturar um preset (Paciente)**

1. Preferências → Configuração de página → excluir todos os stocks excepto USER.
2. Criar/editar USER para `89,0 mm x 36,0 mm`.
3. Exportar registry:

```powershell
$printer = 'PrinterInstall-Spike'
$base = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\$printer"
reg export "$base\PrinterDriverData" "$env:TEMP\paciente-driverdata.reg" /y
```

4. Guardar também captura binária (para embed):

```powershell
$key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(
  "SYSTEM\CurrentControlSet\Control\Print\Printers\$printer\PrinterDriverData")
$names = $key.GetValueNames()
# Documentar nomes de valores e tipos no README
```

- [ ] **Step 3: Repetir para Pulseira (25×270), Matrix (50×30), Dupla (45×13)**

Cada preset → ficheiro `.reg` + notas no README sobre valores que representam a lista de stocks.

- [ ] **Step 4: Documentar procedimento de validação visual**

No `drivers/Gainscha/label-presets/README.md`, registar:
- Versão do driver (`2021.1.4_GN`)
- Passos para confirmar só um USER com dimensões correctas
- Se `DocumentProperties`/`SetPrinter` **não** alterou stocks → confirmar abordagem registry-only

- [ ] **Step 5: Commit dos templates**

```bash
git add drivers/Gainscha/label-presets/
git commit -m "chore: add Gainscha label preset driverdata templates from spike"
```

**Não avançar Task 1+ sem estes ficheiros commitados.**

---

### Task 1: Enum, catálogo e modelo

**Files:**
- Create: `src/PrinterInstall.Core/Models/GainschaLabelPreset.cs`
- Create: `src/PrinterInstall.Core/Gainscha/GainschaLabelPresetCatalog.cs`
- Modify: `src/PrinterInstall.Core/Models/PrinterQueueDefinition.cs`
- Create: `tests/PrinterInstall.Core.Tests/Gainscha/GainschaLabelPresetCatalogTests.cs`

- [ ] **Step 1: Escrever testes falhando**

```csharp
using PrinterInstall.Core.Gainscha;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelPresetCatalogTests
{
    [Theory]
    [InlineData(GainschaLabelPreset.Pulseira, 25, 270, "USER (25,0 mm x 270,0 mm)")]
    [InlineData(GainschaLabelPreset.Matrix, 50, 30, "USER (50,0 mm x 30,0 mm)")]
    [InlineData(GainschaLabelPreset.Paciente, 89, 36, "USER (89,0 mm x 36,0 mm)")]
    [InlineData(GainschaLabelPreset.Dupla, 45, 13, "USER (45,0 mm x 13,0 mm)")]
    public void GetDefinition_ReturnsExpectedDimensionsAndDisplayName(
        GainschaLabelPreset preset, int widthMm, int heightMm, string displayName)
    {
        var def = GainschaLabelPresetCatalog.GetDefinition(preset);
        Assert.Equal(widthMm, def.WidthMm);
        Assert.Equal(heightMm, def.HeightMm);
        Assert.Equal(displayName, def.DriverStockDisplayName);
    }

    [Fact]
    public void AllPresets_AreDistinct()
    {
        var names = Enum.GetValues<GainschaLabelPreset>()
            .Select(GainschaLabelPresetCatalog.GetDefinition)
            .Select(d => d.DriverStockDisplayName)
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
```

- [ ] **Step 2: Correr testes — devem falhar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~GainschaLabelPresetCatalogTests" -v n`

Expected: FAIL — tipos não encontrados

- [ ] **Step 3: Implementar enum e catálogo**

`GainschaLabelPreset.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public enum GainschaLabelPreset
{
    Pulseira,
    Matrix,
    Paciente,
    Dupla
}
```

`GainschaLabelPresetCatalog.cs`:

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public sealed record GainschaLabelPresetDefinition(
    GainschaLabelPreset Preset,
    int WidthMm,
    int HeightMm,
    string DriverStockDisplayName,
    string UiDisplayName);

public static class GainschaLabelPresetCatalog
{
    public static IReadOnlyList<GainschaLabelPresetDefinition> All { get; } =
    [
        Def(GainschaLabelPreset.Pulseira, 25, 270, "Pulseira"),
        Def(GainschaLabelPreset.Matrix, 50, 30, "Matrix"),
        Def(GainschaLabelPreset.Paciente, 89, 36, "Paciente"),
        Def(GainschaLabelPreset.Dupla, 45, 13, "Dupla"),
    ];

    public static GainschaLabelPresetDefinition GetDefinition(GainschaLabelPreset preset) =>
        All.First(d => d.Preset == preset);

    private static GainschaLabelPresetDefinition Def(
        GainschaLabelPreset preset, int w, int h, string uiName) =>
        new(preset, w, h, FormatUserStock(w, h), uiName);

    public static string FormatUserStock(int widthMm, int heightMm) =>
        $"USER ({widthMm},0 mm x {heightMm},0 mm)";
}
```

Modificar `PrinterQueueDefinition.cs` — acrescentar:

```csharp
public GainschaLabelPreset? GainschaLabelPreset { get; init; }
```

- [ ] **Step 4: Correr testes — devem passar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~GainschaLabelPresetCatalogTests" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Models/GainschaLabelPreset.cs \
  src/PrinterInstall.Core/Gainscha/GainschaLabelPresetCatalog.cs \
  src/PrinterInstall.Core/Models/PrinterQueueDefinition.cs \
  tests/PrinterInstall.Core.Tests/Gainscha/GainschaLabelPresetCatalogTests.cs
git commit -m "feat(core): add Gainscha label preset catalog"
```

---

### Task 2: Configurator — aplicar template de DriverData

**Files:**
- Create: `src/PrinterInstall.Core/Gainscha/IGainschaLabelPreferenceConfigurator.cs`
- Create: `src/PrinterInstall.Core/Gainscha/GainschaLabelDriverDataPaths.cs`
- Create: `src/PrinterInstall.Core/Gainscha/GainschaLabelPreferenceConfigurator.cs`
- Modify: `src/PrinterInstall.Core/PrinterInstall.Core.csproj` (embed templates)
- Create: `tests/PrinterInstall.Core.Tests/Gainscha/GainschaLabelPreferenceConfiguratorTests.cs`

- [ ] **Step 1: Escrever interface e paths**

`IGainschaLabelPreferenceConfigurator.cs`:

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Gainscha;

public interface IGainschaLabelPreferenceConfigurator
{
    Task ApplyAsync(string printerQueueName, GainschaLabelPreset preset, CancellationToken cancellationToken = default);
}
```

`GainschaLabelDriverDataPaths.cs`:

```csharp
namespace PrinterInstall.Core.Gainscha;

public static class GainschaLabelDriverDataPaths
{
    public static string PrinterDriverDataKey(string printerQueueName) =>
        $@"SYSTEM\CurrentControlSet\Control\Print\Printers\{printerQueueName}\PrinterDriverData";

    public static string TemplateResourceName(GainschaLabelPreset preset) =>
        $"PrinterInstall.Core.Gainscha.Templates.{preset.ToString().ToLowerInvariant()}.driverdata.bin";
}
```

- [ ] **Step 2: Configurar embedded resources no csproj**

Em `PrinterInstall.Core.csproj`, dentro de `<ItemGroup>`:

```xml
<EmbeddedResource Include="Gainscha\Templates\*.bin" Link="Gainscha\Templates\%(Filename).bin" />
```

Copiar os 4 `.bin` de `drivers/Gainscha/label-presets/` para `src/PrinterInstall.Core/Gainscha/Templates/`.

- [ ] **Step 3: Implementar configurator**

`GainschaLabelPreferenceConfigurator.cs` — lógica:

1. Verificar fila existe (`Get-Printer` equivalente via registry key da fila).
2. Apagar subchave `PrinterDriverData` se existir (`Registry.LocalMachine.DeleteSubKeyTree`, swallow se missing).
3. Recriar `PrinterDriverData`.
4. Importar bytes do embedded resource do preset (formato documentado no spike README — tipicamente replicar valores REG_BINARY do export).
5. Reiniciar spooler **não** necessário se apenas DriverData; se stocks não reflectirem, `Restart-Service Spooler` como fallback documentado.
6. Validar via leitura registry que dimensões/nome USER correspondem a `GainschaLabelPresetCatalog.GetDefinition(preset).DriverStockDisplayName`.

Expor método interno `ImportDriverDataFromTemplate(RegistryKey key, byte[] template)` para testes.

- [ ] **Step 4: Testes unitários com registry fake**

Usar `IGainschaLabelPreferenceConfigurator` com wrapper injectável ou testar `ImportDriverDataFromTemplate` directamente com `MemoryStream` — **não** exigir impressora real no CI.

Teste mínimo:

```csharp
[Fact]
public void TemplateResourceNames_ExistForAllPresets()
{
    var asm = typeof(GainschaLabelPreferenceConfigurator).Assembly;
    foreach (GainschaLabelPreset preset in Enum.GetValues<GainschaLabelPreset>())
    {
        var name = GainschaLabelDriverDataPaths.TemplateResourceName(preset);
        Assert.NotNull(asm.GetManifestResourceStream(name));
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Gainscha/ src/PrinterInstall.Core/PrinterInstall.Core.csproj \
  tests/PrinterInstall.Core.Tests/Gainscha/GainschaLabelPreferenceConfiguratorTests.cs
git commit -m "feat(core): apply Gainscha label preset via PrinterDriverData templates"
```

---

### Task 3: Contrato remoto e implementações local/remota

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/RoutingRemotePrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/LocalPrinterOperations.cs`
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`
- Create: `src/PrinterInstall.Core/Remote/GainschaLabelPresetRemoteStager.cs`
- Modify: `src/PrinterInstall.Core/Remote/RemoteElevatedScriptBuilder.cs`

- [ ] **Step 1: Acrescentar método ao contrato**

`IRemotePrinterOperations.cs`:

```csharp
Task ConfigureGainschaLabelPresetAsync(
    string computerName,
    NetworkCredential credential,
    string printerQueueName,
    GainschaLabelPreset preset,
    CancellationToken cancellationToken = default)
    => throw new NotImplementedException();
```

- [ ] **Step 2: Implementação local**

`LocalPrinterOperations` — injectar/instanciar `GainschaLabelPreferenceConfigurator`:

```csharp
public Task ConfigureGainschaLabelPresetAsync(
    string computerName, NetworkCredential credential,
    string printerQueueName, GainschaLabelPreset preset,
    CancellationToken cancellationToken = default)
{
    _ = computerName; _ = credential;
    return _gainschaLabelConfigurator.ApplyAsync(printerQueueName, preset, cancellationToken);
}
```

- [ ] **Step 3: Script PowerShell remoto**

`RemoteElevatedScriptBuilder.BuildApplyGainschaLabelPresetScript(printerName, presetName, templateFileLocalPath)`:

```csharp
public static string BuildApplyGainschaLabelPresetScript(
    string printerQueueName, string templateFilePathOnTarget)
{
    var n = EscapePs(printerQueueName);
    var t = EscapePs(templateFilePathOnTarget);
    var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    $null = Get-Printer -Name '{n}' -ErrorAction Stop
    $regPath = ""HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers\{n}\PrinterDriverData""
    if (Test-Path $regPath) {{ Remove-Item $regPath -Recurse -Force }}
    New-Item -Path $regPath -Force | Out-Null
    # Import-DriverDataFromBin: implementar função PS inline gerada pelo stager
    # (ler bytes de '{t}' e escrever valores REG_* conforme spike README)
    . ""$PSScriptRoot\Import-GainschaLabelPreset.ps1"" -PrinterName '{n}' -TemplatePath '{t}'
";
    return WrapWithResultHandling(body);
}
```

`GainschaLabelPresetRemoteStager` — copia `Import-GainschaLabelPreset.ps1` + `.bin` do preset para `\\host\ADMIN$\Temp\PrinterInstall\<guid>\`.

- [ ] **Step 4: CimRemotePrinterOperations**

Seguir padrão `ExecuteMutationAsync`:
- **direct:** não aplicável cross-machine — usar sempre staging+script no remoto (WMI `Win32_Process` ou elevated runner).
- **elevated:** `_stager.StageGainschaLabelPresetAsync` + `_elevatedRunner.RunElevatedScriptAsync`.

Timeout: 2 minutos (igual test page).

- [ ] **Step 5: RoutingRemotePrinterOperations** — delegar ao resolver local/remoto.

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.Core/Remote/
git commit -m "feat(remote): configure Gainscha label preset locally and over CIM"
```

---

### Task 4: Orquestrador — passo de preferência e rollback inline

**Files:**
- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`
- Modify: `src/PrinterInstall.Core/Orchestration/DeploymentRollbackJournal.cs`
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorGainschaLabelTests.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs` (mocks Gainscha se necessário)
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`

- [ ] **Step 1: Escrever testes falhando do orquestrador**

`PrinterDeploymentOrchestratorGainschaLabelTests.cs` — casos:

1. `Gainscha_LabelConfigSuccess_RecordsQueueAfterPreset_AndOptionalTestPage`
2. `Gainscha_LabelConfigFailure_RemovesQueueAndPort_ReportsError`
3. `Gainscha_SkippedAlreadyExists_DoesNotCallConfigurePreset`
4. `Lexmark_DoesNotCallConfigurePreset`

Exemplo (sucesso):

```csharp
[Fact]
public async Task Gainscha_LabelConfigSuccess_RecordsQueueAfterPreset()
{
    var driver = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
    var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
    remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { driver });
    remote.Setup(m => m.PrinterQueueExistsAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);
    remote.Setup(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), "RAW", It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
    remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", driver, It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
    remote.Setup(m => m.ConfigureGainschaLabelPresetAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", GainschaLabelPreset.Paciente, It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);

    var journal = new DeploymentRollbackJournal();
    var request = new PrinterDeploymentRequest
    {
        TargetComputerNames = new[] { "pc1" },
        Printers = new[] { new PrinterQueueDefinition
        {
            Brand = PrinterBrand.Gainscha,
            DisplayName = "Q1",
            PrinterHostAddress = "10.0.0.10",
            PortNumber = 9100,
            Protocol = TcpPrinterProtocol.Raw,
            GainschaLabelPreset = GainschaLabelPreset.Paciente
        }},
        DomainCredential = new NetworkCredential("u", "p"),
        PrintTestPage = false
    };

    var events = new List<DeploymentProgressEvent>();
    await new PrinterDeploymentOrchestrator(remote.Object).RunAsync(
        request, journal, new InlineProgress<DeploymentProgressEvent>(events.Add));

    Assert.Single(journal.QueueEntries);
    remote.Verify(m => m.ConfigureGainschaLabelPresetAsync(
        "pc1", It.IsAny<NetworkCredential>(), "Q1", GainschaLabelPreset.Paciente, It.IsAny<CancellationToken>()), Times.Once);
}
```

Exemplo (falha + rollback):

```csharp
remote.Setup(m => m.ConfigureGainschaLabelPresetAsync(...))
      .ThrowsAsync(new InvalidOperationException("driverdata"));
remote.Setup(m => m.RemovePrinterQueueAsync("pc1", It.IsAny<NetworkCredential>(), "Q1", It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
remote.Setup(m => m.CountPrintersUsingPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(0);
remote.Setup(m => m.RemoveTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
// Assert journal.QueueEntries empty, event Error, message contains "revert"
```

- [ ] **Step 2: Correr testes — devem falhar**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~PrinterDeploymentOrchestratorGainschaLabelTests" -v n`

- [ ] **Step 3: Implementar journal.AbandonPortOnly**

`DeploymentRollbackJournal.cs`:

```csharp
public void AbandonPortOnly(string computerName, string portName)
{
    var c = computerName.Trim();
    var p = portName.Trim();
    _portOnly.Remove((c, p));
}
```

- [ ] **Step 4: Alterar orquestrador**

Em `PrinterDeploymentOrchestrator`, substituir bloco pós-`AddPrinterAsync` para Gainscha:

```csharp
await _remote.AddPrinterAsync(...);

if (def.Brand == PrinterBrand.Gainscha)
{
    var preset = def.GainschaLabelPreset
        ?? throw new InvalidOperationException("Gainscha queue requires GainschaLabelPreset.");

    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring,
        "Configurando tamanho de etiqueta...", displayName));

    await Task.Delay(SpoolerSettleDelay, cancellationToken).ConfigureAwait(false);

    try
    {
        await _remote.ConfigureGainschaLabelPresetAsync(
            computer, request.DomainCredential, displayName, preset.Value, cancellationToken)
            .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring,
            "Falha na preferência de etiqueta — revertendo fila e porta...", displayName));
        await RevertUnjournaledQueueAsync(computer, request.DomainCredential, displayName, portName, rollbackJournal, cancellationToken)
            .ConfigureAwait(false);
        progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error,
            $"Revertido — preferência de etiqueta não aplicada: {Flatten(ex)}", displayName));
        continue;
    }

    progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring,
        "Preferência de etiqueta aplicada.", displayName));
}

rollbackJournal.RecordQueueCreated(computer, displayName, portName);
// ... test page ...
```

Método privado `RevertUnjournaledQueueAsync`:
- `RemovePrinterQueueAsync`
- `CountPrintersUsingPortAsync` → se 0, `RemoveTcpPrinterPortAsync`
- `rollbackJournal.AbandonPortOnly(computer, portName)`

Constante `SpoolerSettleDelay` — reutilizar 2 s (extrair para campo estático partilhado ou duplicar `TimeSpan.FromSeconds(2)`).

- [ ] **Step 5: Actualizar testes existentes**

Qualquer teste Gainscha que chegue a `AddPrinterAsync` deve setup `ConfigureGainschaLabelPresetAsync` ou usar marca Lexmark/Epson.

- [ ] **Step 6: Correr suite Core**

Run: `dotnet test "tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj" -v n`

Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/PrinterInstall.Core/Orchestration/ tests/PrinterInstall.Core.Tests/Orchestration/
git commit -m "feat(orchestrator): apply Gainscha label preset with rollback on failure"
```

---

### Task 5: UI — dropdown condicional e validação

**Files:**
- Modify: `src/PrinterInstall.App/ViewModels/PrinterFormRowViewModel.cs`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.resx`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.Designer.cs`
- Create: `tests/PrinterInstall.App.Tests/ViewModels/MainViewModelGainschaValidationTests.cs`

- [ ] **Step 1: PrinterFormRowViewModel**

```csharp
[ObservableProperty]
private GainschaLabelPreset? _gainschaLabelPreset;

public bool IsGainschaBrand => Brand == PrinterBrand.Gainscha;

public static IEnumerable<GainschaLabelPreset> GainschaLabelPresetChoices =>
    Enum.GetValues<GainschaLabelPreset>();

partial void OnBrandChanged(PrinterBrand value)
{
    if (value != PrinterBrand.Gainscha)
        GainschaLabelPreset = null;
    OnPropertyChanged(nameof(IsGainschaBrand));
}
```

- [ ] **Step 2: MainWindow.xaml — dropdown condicional**

Na `DataTemplate` da linha de impressora, adicionar `StackPanel` com `Visibility` ligado a `IsGainschaBrand` (converter bool→Visibility ou `DataTrigger`):

```xml
<StackPanel Grid.Row="1" Grid.Column="1" Margin="4,0,0,4"
            Visibility="{Binding IsGainschaBrand, Converter={StaticResource BoolToVisibilityConverter}}">
    <TextBlock Text="{DynamicResource Main_GainschaLabelPresetLabel}" FontSize="11"/>
    <ComboBox ItemsSource="{Binding Source={x:Static vm:PrinterFormRowViewModel.GainschaLabelPresetChoices}}"
              SelectedItem="{Binding GainschaLabelPreset}"/>
</StackPanel>
```

Ajustar grid rows/columns conforme layout (host IP pode mover para row 2).

Strings em `Main.pt-BR.xaml`:

```xml
<sys:String x:Key="Main_GainschaLabelPresetLabel">Tamanho de etiqueta</sys:String>
```

- [ ] **Step 3: Validação MainViewModel**

Antes de construir `definitions`, no loop `foreach (var row in PrinterRows)`:

```csharp
if (row.Brand == PrinterBrand.Gainscha && row.GainschaLabelPreset is null)
{
    AppendLog(UiStrings.Main_Validation_GainschaLabelPresetRequired);
    return;
}
```

No `PrinterQueueDefinition`:

```csharp
GainschaLabelPreset = row.Brand == PrinterBrand.Gainscha ? row.GainschaLabelPreset : null
```

`UiStrings.resx`:

```xml
<data name="Main_Validation_GainschaLabelPresetRequired" xml:space="preserve">
  <value>Selecione o tamanho de etiqueta para impressoras Gainscha.</value>
</data>
```

Regenerar `UiStrings.Designer.cs` (build ou manual).

- [ ] **Step 4: Teste de validação**

```csharp
[Fact]
public void Deploy_GainschaWithoutPreset_DoesNotStartOrchestrator()
{
    // Arrange MainViewModel com row Brand=Gainscha, GainschaLabelPreset=null
    // Act DeployCommand
    // Assert Log contém Main_Validation_GainschaLabelPresetRequired
}
```

- [ ] **Step 5: Build app**

Run: `dotnet build "src/PrinterInstall.App/PrinterInstall.App.csproj" -v q`

Expected: SUCCESS

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.App/ tests/PrinterInstall.App.Tests/
git commit -m "feat(app): Gainscha label preset dropdown and deploy validation"
```

---

### Task 6: Verificação manual e actualização da spec

**Files:**
- Modify: `docs/superpowers/specs/2026-06-25-gainscha-label-paper-preset-design.md` (Status → Implementado)

- [ ] **Step 1: Deploy remoto — Paciente**

PC alvo com driver Gainscha; deploy com preset Paciente; confirmar Preferências → `USER (89,0 mm x 36,0 mm)` único stock.

- [ ] **Step 2: Deploy remoto — Pulseira**

Confirmar `USER (25,0 mm x 270,0 mm)`.

- [ ] **Step 3: Falha simulada**

Temporariamente renomear template `.bin` no stager ou injectar throw no configurator local → fila e porta **não** permanecem.

- [ ] **Step 4: Deploy local**

Mesmo PC do app → preset Dupla → preferência correcta.

- [ ] **Step 5: Regressão**

- Linha Epson na mesma execução → sem dropdown.
- Cancelar deploy a meio → journal/reversão existente intacto.
- `dotnet test` solução completa.

Run: `dotnet test "PrinterInstall.sln" -v n`

- [ ] **Step 6: Commit doc**

```bash
git add docs/superpowers/specs/2026-06-25-gainscha-label-paper-preset-design.md
git commit -m "docs: mark Gainscha label preset spec as implemented"
```

---

## Self-review (plano vs spec)

| Requisito spec | Task |
|----------------|------|
| Dropdown condicional Gainscha | Task 5 |
| 4 presets + dimensões | Task 1 |
| Formato USER (mm x mm) | Task 1 catalog |
| Passo pós-AddPrinter | Task 4 |
| Rollback fila+porta | Task 4 |
| Journal após preferência OK | Task 4 |
| SkippedAlreadyExists sem config | Task 4 teste 3 |
| Local + remoto | Task 3 |
| Spike registry | Task 0 |
| Testes unitários listados | Tasks 1, 2, 4, 5 |
| Verificação manual | Task 6 |

Sem placeholders TBD. Nomes consistentes: `GainschaLabelPreset`, `ConfigureGainschaLabelPresetAsync`, `GainschaLabelPreferenceConfigurator`.

---

## Notas para implementadores

1. **Task 0 é bloqueante** — os `.bin` reflectem formato real do Seagull 2021.1.4_GN; não inventar bytes.
2. Se após import registry a UI não reflectir stocks, documentar no README e avaliar `Restart-Service Spooler` controlado pós-apply.
3. Preset `GainschaLabelPreset?` na definição evita validação duplicada na Core; UI garante non-null para Gainscha antes do request.
4. Mensagens de progresso em português **hardcoded no orquestrador** seguem padrão existente (`"Creating port..."`); opcional migrar para recursos numa segunda onda.
