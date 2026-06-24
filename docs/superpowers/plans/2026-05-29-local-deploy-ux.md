# Local Deploy UX — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar óbvio configurar impressoras na máquina de execução via botão «Adicionar este PC» e hint «(este PC)» na grelha, reutilizando o roteamento local já implementado (drivers + test page).

**Architecture:** Backend inalterado (`RoutingRemotePrinterOperations`). Camada App: `LocalMachineIdentity.GetPrimaryLocalName()`, comando `AddThisComputerCommand` no `MainViewModel`, botão no `MainWindow`, propriedades de display no `TargetRowViewModel`, strings pt-BR e documentação.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, xUnit

**Spec:** `docs/superpowers/specs/2026-05-29-local-deploy-ux-design.md`

---

## Mapa de ficheiros

| Ficheiro | Responsabilidade |
|----------|------------------|
| `src/PrinterInstall.Core/Remote/LocalMachineIdentity.cs` | `GetPrimaryLocalName()` para a UI |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Injeção `LocalMachineIdentity`, `AddThisComputerCommand`, hint na grelha |
| `src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs` | `IsLocalMachine`, `ComputerNameDisplay` |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Botão + coluna Computador |
| `src/PrinterInstall.App/Strings/Main.pt-BR.xaml` | Strings do botão e sufixo |
| `src/PrinterInstall.App/Resources/UiStrings.resx` | `Main_LocalComputerSuffix` (usado em código C#) |
| `tests/PrinterInstall.Core.Tests/Remote/LocalMachineIdentityTests.cs` | Teste `GetPrimaryLocalName` |
| `tests/PrinterInstall.App.Tests/ViewModels/MainViewModelAddThisComputerTests.cs` | Testes do comando |
| `docs/conexao-remota.md` | Fluxo recomendado + botão |

---

### Task 1: `GetPrimaryLocalName` em `LocalMachineIdentity`

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/LocalMachineIdentity.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Remote/LocalMachineIdentityTests.cs`

- [ ] **Step 1: Escrever teste que falha**

Em `LocalMachineIdentityTests.cs`, adicionar:

```csharp
[Fact]
public void GetPrimaryLocalName_ReturnsEnvironmentMachineName()
{
    Assert.Equal(Environment.MachineName, _sut.GetPrimaryLocalName());
}
```

- [ ] **Step 2: Correr teste e confirmar falha**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~GetPrimaryLocalName" -v n`

Expected: FAIL — `'LocalMachineIdentity' does not contain a definition for 'GetPrimaryLocalName'`

- [ ] **Step 3: Implementação mínima**

Em `LocalMachineIdentity.cs`, dentro da classe, adicionar:

```csharp
/// <summary>
/// Hostname curto preferido para inserir na lista de alvos da UI.
/// </summary>
public string GetPrimaryLocalName() => Environment.MachineName;
```

- [ ] **Step 4: Correr teste e confirmar passagem**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~GetPrimaryLocalName" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Remote/LocalMachineIdentity.cs tests/PrinterInstall.Core.Tests/Remote/LocalMachineIdentityTests.cs
git commit -m "feat: expose primary local machine name for deploy UX"
```

---

### Task 2: `AddThisComputerCommand` no `MainViewModel`

**Files:**
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Create: `tests/PrinterInstall.App.Tests/ViewModels/MainViewModelAddThisComputerTests.cs`

- [ ] **Step 1: Escrever testes que falham**

Criar `tests/PrinterInstall.App.Tests/ViewModels/MainViewModelAddThisComputerTests.cs`:

```csharp
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App.Tests.ViewModels;

public class MainViewModelAddThisComputerTests
{
    private static MainViewModel CreateSut(LocalMachineIdentity identity)
    {
        // AddThisComputer não usa orquestrador nem service provider.
        return new MainViewModel(
            new SessionContext(),
            null!,
            null!,
            null!,
            identity);
    }

    [Fact]
    public void AddThisComputer_EmptyList_AppendsMachineName()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal(identity.GetPrimaryLocalName(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_ExistingRemote_AppendsOnNewLine()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "pc-remoto-01";

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal($"pc-remoto-01{Environment.NewLine}{identity.GetPrimaryLocalName()}", sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocal_DoesNotDuplicate()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        var local = identity.GetPrimaryLocalName();
        sut.ComputersText = local.ToUpperInvariant();

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal(local.ToUpperInvariant(), sut.ComputersText);
    }

    [Fact]
    public void AddThisComputer_AlreadyHasLocalhostLiteral_DoesNotAppend()
    {
        var identity = new LocalMachineIdentity();
        var sut = CreateSut(identity);
        sut.ComputersText = "localhost";

        sut.AddThisComputerCommand.Execute(null);

        Assert.Equal("localhost", sut.ComputersText);
    }
}
```

- [ ] **Step 2: Correr testes e confirmar falha**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~MainViewModelAddThisComputer" -v n`

Expected: FAIL — construtor com 5 argumentos inexistente / comando inexistente

- [ ] **Step 3: Implementar no `MainViewModel`**

1. Adicionar `using PrinterInstall.Core.Remote;` e `using PrinterInstall.Core.Validation;`

2. Campo e construtor — adicionar parâmetro `LocalMachineIdentity localMachineIdentity`:

```csharp
private readonly LocalMachineIdentity _localMachineIdentity;

public MainViewModel(
    ISessionContext session,
    PrinterDeploymentOrchestrator orchestrator,
    DeploymentRollbackRunner rollbackRunner,
    IServiceProvider serviceProvider,
    LocalMachineIdentity localMachineIdentity)
{
    _session = session;
    _orchestrator = orchestrator;
    _rollbackRunner = rollbackRunner;
    _serviceProvider = serviceProvider;
    _localMachineIdentity = localMachineIdentity;
    PrinterRows.Add(new PrinterFormRowViewModel());
    Targets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatusEmptyHint));
}
```

3. Comando Relay:

```csharp
[RelayCommand]
private void AddThisComputer()
{
    var existing = ComputerNameListParser.Parse(ComputersText);
    if (existing.Any(_localMachineIdentity.IsLocalMachine))
        return;

    var name = _localMachineIdentity.GetPrimaryLocalName();
    ComputersText = string.IsNullOrWhiteSpace(ComputersText)
        ? name
        : ComputersText.TrimEnd() + Environment.NewLine + name;
}
```

> **Nota DI:** `MainViewModel` já é `AddTransient` em `App.xaml.cs`; `LocalMachineIdentity` é singleton registado — o container resolve automaticamente o 5.º parâmetro. **Não** alterar `App.xaml.cs` manualmente.

- [ ] **Step 4: Correr testes e confirmar passagem**

Run: `dotnet test tests/PrinterInstall.App.Tests/PrinterInstall.App.Tests.csproj --filter "FullyQualifiedName~MainViewModelAddThisComputer" -v n`

Expected: PASS (4 testes)

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/MainViewModel.cs tests/PrinterInstall.App.Tests/ViewModels/MainViewModelAddThisComputerTests.cs
git commit -m "feat: add AddThisComputer command to MainViewModel"
```

---

### Task 3: Hint «(este PC)» na grelha de status

**Files:**
- Modify: `src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs`
- Modify: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.resx`
- Modify: `src/PrinterInstall.App/Resources/UiStrings.Designer.cs` (regenerado pelo build ou editado)
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`

- [ ] **Step 1: Propriedades no `TargetRowViewModel`**

Em `TargetRowViewModel.cs`, adicionar:

```csharp
[ObservableProperty]
private bool _isLocalMachine;

[ObservableProperty]
private string _computerNameDisplay = "";
```

- [ ] **Step 2: Helper privado no `MainViewModel`**

Adicionar método:

```csharp
private void ConfigureTargetRowDisplay(TargetRowViewModel row, string computerName)
{
    var isLocal = _localMachineIdentity.IsLocalMachine(computerName);
    row.IsLocalMachine = isLocal;
    row.ComputerNameDisplay = isLocal
        ? $"{computerName} {UiStrings.Main_LocalComputerSuffix}"
        : computerName;
}
```

Chamar `ConfigureTargetRowDisplay(row, n)` em **todos** os sítios onde `Targets.Add(new TargetRowViewModel { ComputerName = n, ...})` ocorre dentro de `DeployAsync` (bloco inválido e bloco válido).

- [ ] **Step 3: String em `UiStrings.resx`**

Adicionar entrada:

| Name | Value |
|------|-------|
| `Main_LocalComputerSuffix` | `(este PC)` |

Regenerar `UiStrings.Designer.cs` via build ou adicionar propriedade estática equivalente às existentes.

- [ ] **Step 4: Coluna da grelha**

Em `MainWindow.xaml`, linha 82, alterar binding:

```xml
<DataGridTextColumn Header="{DynamicResource Main_ColumnComputer}" Binding="{Binding ComputerNameDisplay}" Width="*"/>
```

- [ ] **Step 5: Verificação manual rápida**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`

Expected: build succeeded, sem erros CS

- [ ] **Step 6: Commit**

```bash
git add src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs src/PrinterInstall.App/ViewModels/MainViewModel.cs src/PrinterInstall.App/Resources/UiStrings.resx src/PrinterInstall.App/Resources/UiStrings.Designer.cs src/PrinterInstall.App/Views/MainWindow.xaml
git commit -m "feat: show local machine hint in deploy status grid"
```

---

### Task 4: Botão «Adicionar este PC» na UI

**Files:**
- Modify: `src/PrinterInstall.App/Views/MainWindow.xaml`
- Modify: `src/PrinterInstall.App/Strings/Main.pt-BR.xaml`

- [ ] **Step 1: String pt-BR**

Em `Main.pt-BR.xaml`, após `Main_ComputersLabel`:

```xml
<sys:String x:Key="Main_AddThisComputer">Adicionar este PC</sys:String>
```

- [ ] **Step 2: Layout do painel de computadores**

Substituir o `TextBlock` isolado do label por um `Grid` horizontal no topo do `DockPanel` (linhas ~59–62):

```xml
<Grid DockPanel.Dock="Top" Margin="0,0,0,8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="{DynamicResource Main_ComputersLabel}" FontWeight="SemiBold" VerticalAlignment="Center"/>
    <ui:Button Grid.Column="1"
               Content="{DynamicResource Main_AddThisComputer}"
               Command="{Binding AddThisComputerCommand}"
               Appearance="Secondary"
               Padding="8,4"/>
</Grid>
```

- [ ] **Step 3: Build**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`

Expected: build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.App/Views/MainWindow.xaml src/PrinterInstall.App/Strings/Main.pt-BR.xaml
git commit -m "feat: add Add This PC button to main deploy screen"
```

---

### Task 5: Documentação

**Files:**
- Modify: `docs/conexao-remota.md`
- Modify: `docs/superpowers/specs/2026-05-29-local-deploy-ux-design.md` (status → Implementado após conclusão)

- [ ] **Step 1: Actualizar `docs/conexao-remota.md`**

Na secção **Máquina local**, substituir o parágrafo:

> A máquina local **não** é adicionada automaticamente à lista; o operador continua a incluí-la manualmente.

Por:

```markdown
### Configurar este PC (fluxo recomendado)

1. Faça login LDAP (obrigatório em todos os cenários).
2. Na tela principal, clique em **Adicionar este PC** — insere o hostname curto desta máquina na lista de alvos (sem duplicar se já estiver presente como hostname, IP ou literal local).
3. Preencha a impressora e, se desejar, marque **Imprimir teste**.
4. Clique em **Implantar**.

O mesmo fluxo serve para operações mistas: adicione este PC e outros hosts do domínio na mesma lista. A grelha de status mostra **(este PC)** nas linhas do alvo local.

Requisitos na máquina de execução: perfil com privilégios de administrador local (WMI, `pnputil`, página de teste). Login LDAP permanece obrigatório mesmo quando todos os alvos são locais.
```

- [ ] **Step 2: Actualizar status da spec**

Em `docs/superpowers/specs/2026-05-29-local-deploy-ux-design.md`, alterar `Status: Para revisão` → `Status: Implementado`.

- [ ] **Step 3: Commit**

```bash
git add docs/conexao-remota.md docs/superpowers/specs/2026-05-29-local-deploy-ux-design.md
git commit -m "docs: document local deploy UX flow and Add This PC button"
```

---

### Task 6: Verificação final

**Files:** (nenhum novo)

- [ ] **Step 1: Correr toda a suíte de testes**

Run: `dotnet test -v n`

Expected: todos os testes passam, zero falhas

- [ ] **Step 2: Checklist de aceitação (manual)**

| # | Critério | Como verificar |
|---|----------|----------------|
| 1 | Botão insere hostname sem duplicar | Clicar duas vezes — lista não muda na 2.ª |
| 2 | Deploy só-local | Adicionar este PC + implantar com driver em falta + test page |
| 3 | Deploy misto | Este PC + remoto na mesma operação |
| 4 | Hint na grelha | Linha local mostra `(este PC)` |
| 5 | LDAP obrigatório | Sem login, deploy não avança (comportamento existente) |

- [ ] **Step 3: Commit final (se houver ajustes)**

Apenas se Steps 1–2 revelarem correções.

---

## Self-review (spec coverage)

| Requisito da spec | Task |
|-------------------|------|
| `GetPrimaryLocalName()` | Task 1 |
| `AddThisComputerCommand` | Task 2 |
| Botão MainWindow | Task 4 |
| `ComputerNameDisplay` / hint | Task 3 |
| Strings pt-BR + UiStrings | Tasks 3–4 |
| Documentação | Task 5 |
| Testes unitários | Tasks 1–2 |
| Backend inalterado | Nenhuma task Core de orquestração |
| LDAP sempre obrigatório | Sem alteração — verificação Task 6 |
| Removal Wizard fora de âmbito | Nenhuma task |
