# Controle de impressoras (renomear + remover remotos) — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para executar tarefa a tarefa. Passos com checkbox (`- [ ]`).

**Goal:** Adicionar renomeação remota de filas de impressão no mesmo assistente, com plano unificado, um botão de execução, exclusão mútua por linha (remover *ou* renomear) e cadeia remota com paridade WinRM/CIM; alinhar textos da UI a **Controle de impressoras**.

**Arquitectura:** Extender `IRemotePrinterOperations` com `RenamePrinterQueueAsync`. Introduzir DTOs `PrinterRenameItem` e `PrinterControlRequest` / `PrinterControlTarget`. Implementar renames no WinRM (PowerShell `Rename-Printer` com o mesmo padrão de *escape* que `RemovePrinterQueueAsync`); CIM com efeito equivalente (Powershell remoto via `IRemoteProcessRunner` *ou* WMI, conforme o que o `CimRemotePrinterOperations` puder reutilizar sem adicionar WinRM). Orquestrador único: por alvo, `Renames` (ordenados) **depois** `QueuesToRemove` (lógica actual de remoção + porta órfã). WPF: `SelectableQueueRow` com `NewName`, exclusão mútua, revisão, recursos e botão no `MainWindow`. **Não fazer** `git commit` / `push` sem pedido explícito do utilizador.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, `System.Text.Json` onde aplicável, xUnit, Moq.

**Comando base de build/test (ajusta o path se a workspace mudar):**

```bash
dotnet build "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\src\PrinterInstall.App\PrinterInstall.App.csproj" -c Release
dotnet test "C:\Users\Admin\Documents\Projects\Code Projects\Printer Install 2\tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Release --no-build
```

---

## Ficheiro — mapa

| Ficheiro | Acção |
|----------|--------|
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | Adicionar `RenamePrinterQueueAsync`. |
| `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` | `Rename-Printer` no runspace remoto. |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | Paridade (ver tarefa). |
| `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` | Encadear. |
| `src/PrinterInstall.Core/Models/PrinterRenameItem.cs` | **Criar** `record` com `CurrentName`, `NewName`. |
| `src/PrinterInstall.Core/Models/PrinterControlTarget.cs` | **Criar** alvo: `Renames` + `QueuesToRemove`. |
| `src/PrinterInstall.Core/Models/PrinterControlRequest.cs` | **Criar** (credencial + alvos). |
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressState.cs` | Adicionar `RenamingQueue` (ou nome equivalente). |
| `src/PrinterInstall.Core/Orchestration/PrinterControlOrchestrator.cs` | **Criar** lógica unificada; ver tarefa sobre o destino de `PrinterRemovalOrchestrator`. |
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalOrchestrator.cs` | **Substituir** por *thin* delegar para o novo, **ou** fundir lógica e manter o nome (preferir *um* orquestrador + actualizar testes). |
| `src/PrinterInstall.App/ViewModels/SelectableQueueRow.cs` | Propriedade `NewName`; lógica de *partial* *OnChanged* com exclusão mútua. |
| `src/PrinterInstall.App/ViewModels/RemovalWizardViewModel.cs` | *Capture* renames+removes; `CanAdvanceQueueStep`, `BuildReviewSummary`, `ExecuteAsync`. |
| `src/PrinterInstall.App/Views/RemovalWizardWindow.xaml` | Coluna “Novo nome”, estilos de célula *IsEnabled* conforme o estado da linha. |
| `src/PrinterInstall.App/Strings/RemovalWizard.pt-BR.xaml` + `Resources/UiStrings.resx` | Novas chaves, títulos, botão principal. |
| `src/PrinterInstall.App/Views/MainWindow.xaml` (e recursos) | Texto do botão **Controle de impressoras…** |
| `src/PrinterInstall.App/App.xaml.cs` | Registar o orquestrador correcto. |
| `tests/PrinterInstall.Core.Tests/Orchestration/PrinterRemovalOrchestratorTests.cs` | Renomear/estender para `PrinterControlOrchestrator` + testes de ordem de *rename*→*remove*. |

---

### Tarefa 1: Contrato remoto e modelos

**Ficheiros:** `IRemotePrinterOperations.cs`, `PrinterRenameItem.cs` (criar), `PrinterControlTarget.cs` (criar), `PrinterControlRequest.cs` (criar)

- [ ] **Passo 1.1** Em `IRemotePrinterOperations`, adicionar:

```csharp
Task RenamePrinterQueueAsync(
    string computerName,
    System.Net.NetworkCredential credential,
    string currentName,
    string newName,
    CancellationToken cancellationToken = default);
```

As implementações existentes *default* (interface **sem** *default throw* se o projecto hoje *não* usa *default* em todos os métodos — **copiar o padrão** de `ListPrinterQueuesAsync`).

- [ ] **Passo 1.2** Criar `PrinterRenameItem.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public sealed record PrinterRenameItem(string CurrentName, string NewName);
```

- [ ] **Passo 1.3** Criar `PrinterControlTarget.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public sealed class PrinterControlTarget
{
    public required string ComputerName { get; init; }
    public IReadOnlyList<PrinterRenameItem> Renames { get; init; } = Array.Empty<PrinterRenameItem>();
    public IReadOnlyList<PrinterRemovalQueueItem> QueuesToRemove { get; init; } = Array.Empty<PrinterRemovalQueueItem>();
}
```

- [ ] **Passo 1.4** Criar `PrinterControlRequest.cs` com as mesmas convenções de credencial que `PrinterRemovalRequest` (`DomainCredential` + `Targets` de `PrinterControlTarget`).

**Verificar:** `dotnet build` no `.sln` ou no `Core`.

---

### Tarefa 2: `WinRmRemotePrinterOperations.RenamePrinterQueueAsync`

**Ficheiro:** `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`

- [ ] **Passo 2.1** Implementar com script inline que: importa o módulo de impressão se necessário, chama `Rename-Printer -Name '...' -NewName '...'`, reutilizando o método *privado* `Escape` já existente para *strings* (como `RemovePrinterQueueAsync`).

**Verificar:** `dotnet build`.

---

### Tarefa 3: `CimRemotePrinterOperations` — paridade

**Ficheiro:** `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`

- [ ] **Passo 3.1** Implementar `RenamePrinterQueueAsync` de forma a produzir o **mesmo efeito** no spooler alvo. Se a classe WMI `Win32_Printer` no alvo expuser um rename suportado e testável, usar `ManagementObject.InvokeMethod`; **senão** invocar `IRemoteProcessRunner.RunAsync` com uma linha de comando `powershell.exe` (ou a mesma *shape* usada noutro `RunAsync` de instalação) a executar `Import-Module PrintManagement; Rename-Printer -Name '...' -NewName '...'`, com *escape* de aspas (reutilizar `EscapePs` *private* se existir). Tratar *timeout* com `TimeSpan` razoável (ex.: 2 min) alinhada a operações semelhantes, **não** ao *InstallTimeout* de 3 min, a menos que se justifique.

**Verificar:** `dotnet build`.

---

### Tarefa 4: `CompositeRemotePrinterOperations`

**Ficheiro:** `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`

- [ ] **Passo 4.1** Seguir o mesmo padrão de *fallback* de `ListPrinterQueuesAsync` / `RemovePrinterQueueAsync`: tentar o primeiro canal, *catch* e tentar o segundo.

**Verificar:** `dotnet build`.

---

### Tarefa 5: Progresso e orquestrador

**Ficheiros:** `PrinterRemovalProgressState.cs`, `PrinterControlOrchestrator.cs` (criar), `PrinterRemovalOrchestrator.cs` (refactor/redirect), testes

- [ ] **Passo 5.1** Adicionar `RenamingQueue` a `PrinterRemovalProgressState` (ou renomear o enum para `PrinterControlProgressState` **se** estiveres disposto a tocar *em* todos os ficheiros que referem o tipo; para **YAGNI**, acrescenta um valor).

- [ ] **Passo 5.2** Criar `PrinterControlOrchestrator` com `RunAsync(PrinterControlRequest, IProgress<PrinterRemovalProgressEvent>, CT)` (ou *record* de evento partilhado) que, **para cada** `target`:

  1. Se `Renames.Count == 0` **e** `QueuesToRemove.Count == 0`, reportar *Done* / *Nothing to do* (mensagem alinhada à actual).
  2. `ContactingRemote` / mensagem a indicar início.
  3. Para cada `PrinterRenameItem` em ordem alfabética de `CurrentName` (`StringComparer.OrdinalIgnoreCase`): `RenamingQueue`, `await _remote.RenamePrinterQueueAsync(...)`; *catch* e `Error` *per item*, `continue` (igual a removes).
  4. Reutilizar a lógica *existente* de remoção (bloco *foreach* *ordered* e porta órfã) para `QueuesToRemove` — podes **extrair** o corpo *foreach* *actual* de `PrinterRemovalOrchestrator` num método *privado* *estático* *partilhado* *ou* **instanciar** o código **uma vez** no novo ficheiro e fazer o antigo ficheiro **delegar** para o novo. **Não** duplicar a regra de porta *sem* *diff* a rever.

- [ ] **Passo 5.3** `PrinterRemovalOrchestrator`: transformar em *wrapper* *deprecated* *interno* **ou** substituir corpo para construir `PrinterControlRequest` com `Renames` *empty* a partir de `PrinterRemovalRequest` e chamar `PrinterControlOrchestrator`, para **não** *quebrar* o *DI* até o passo 7.

- [ ] **Passo 5.4** `tests/.../PrinterRemovalOrchestratorTests.cs` (ou renomear ficheiro): adicionar teste **mock** que verifica **ordem** de chamada: renames *antes* de `RemovePrinterQueueAsync` com dois itens; adicionar teste “*só* *renames* *sem* *removes* *com* *TargetCompleted* *sem* *remover* *porta* *sem* *chamar* *RemoveTcpPrinterPortAsync* *para* *esse* *plano*” (ajusta expectativas ao *mock* *Setup* *Verify*).

**Verificar:** `dotnet test` no projecto *Core*.

---

### Tarefa 6: ViewModel e linha seleccionável

**Ficheiros:** `SelectableQueueRow.cs`, `RemovalWizardViewModel.cs`

- [ ] **Passo 6.1** `SelectableQueueRow`: adicionar `partial` *property* `NewName` (string, *default* `""`). Em *partial* *OnIsSelectedChanged* e *OnNewNameChanged*: se *selected*, limpar *NewName*; se *NewName* *trim* *non-empty* *and* *different* *from* *Name*, *unselect*. Garantir que *PropertyChanged* dispara *para* o *DataGrid* *rebind* (para *texto* a *não* *interferir* com o *check*).

- [ ] **Passo 6.2** `OnQueueRowPropertyChanged`: reagir também a *NewName* e actualizar *NextQueueStepCommand* *CanExecute* (como hoje *IsSelected*). 

- [ ] **Passo 6.3** Ajustar `CanAdvanceQueueStep` para: se `Count > 0`, permitir *advance* *se* *qualquer* *row* *tem* *IsSelected* *||* *HasMeaningfulRename* *(*trim*, *diferente* *do* *nome* do *spooler*)*.

- [ ] **Passo 6.4** `CaptureCurrentSelection`: preencher `Dictionary<string, List<PrinterRemovalQueueItem>>` **e** *novo* *Dictionary* *para* *renames* (ou *estrutura* *única* *por* *alvo* *—* *preferir* *dois* *dicionários* *espelhando* o *padrão* *actual*).

- [ ] **Passo 6.5** `BuildReviewSummary` incluir renames; usar novas cadeias em *UiStrings* / `.resx`.

- [ ] **Passo 6.6** `ExecuteAsync` construir `PrinterControlRequest` (credencial, alvos) e chamar o **orquestrador** de controlo. Se *apenas* o *wrapper* *antigo* *existir*, *injetar* o *orquestrador* *unificado* *no* *ctor* (passo 7).

**Verificar:** compilação + se possível *test* *de* *ViewModel* *só* *se* *já* *existir* *padrão* *no* *repo* (opcional; **não** *bloquear*).

---

### Tarefa 7: DI e `App.xaml.cs`

**Ficheiro:** `App.xaml.cs`

- [ ] **Passo 7.1** `AddSingleton<PrinterControlOrchestrator>()`; *manter* *ou* *remover* o *antigo* *orquestrador* conforme o *passo* *5* *(*wrapper*)*; *garantir* *que* *há* *exactamente* *uma* *cadeia* *coerente*.

- [ ] **Passo 7.2** Actualizar o *construtor* *do* *RemovalWizardViewModel* no *registo* (interface *não* *—* o *tipo* *concreto*).

**Verificar:** `dotnet build` *App*.

---

### Tarefa 8: XAML do assistente

**Ficheiro:** `RemovalWizardWindow.xaml`

- [ ] **Passo 8.1** Adicionar coluna `DataGrid` *Novo* *nome* *(*TextBox* *ou* *TextColumn* *editável* *conforme* o *padrão* *actual*)*; *ligar* *a* *NewName*; *estilo* *IsReadOnly* *ou* *IsEnabled* *via* *multi* *converter* *ou* *propriedade* *só* *leitura* *derivada* *na* *row* (ex.: *IsRenameEditable* = *!IsSelected*). **Recomendado:** *property* *bool* *só* *leitura* *na* *row* *para* *evitar* *conversor* *complexo*.

- [ ] **Passo 8.2** Ajustar *cabeçalho* *da* *janela* *e* *recursos* *Dinâmicos* *para* *“Controle* *de* *impressoras*”*.

**Verificar:** *Smoke* *manual* (abrir *janela*).

---

### Tarefa 9: Cadeias e *MainWindow*

**Ficheiros:** `RemovalWizard.pt-BR.xaml`, `MainWindow.xaml` *resources*, `UiStrings.resx`, `App.xaml` *merges* *se* *preciso*.

- [ ] **Passo 9.1** Renomear chaves *ou* *adicionar* *chaves* *novas* *para* *títulos* *e* *colunas* *sem* *destruir* *a* *localização*; *atualizar* *referências* *XAML*.

- [ ] **Passo 9.2** `Main_RemovePrintersButton` (ou a chave usada) → **Controle de impressoras…**

**Verificar:** *build*.

---

### Tarefa 10: Verificação final

- [ ] `dotnet build` *solução* *Release*
- [ ] `dotnet test` *todos* *os* *test* *projects*
- [ ] *Checklist* *manual* *breve* *(*credenciais* *de* *domínio* *+ *um* *alvo* *de* *teste*): *listar* *→* *só* *rename* *→* *executar*; *listar* *→* *só* *remove* *→* *executar*; *misto* *no* *mesmo* *lote*.

---

## *Self-review* *do* *plano* *vs* *spec* `2026-04-27-printer-control-design.md`

- **Tarefa** *1–4* *→* *remote* *rename* *+* *paridade* *Composite*.  
- **5** *→* *ordem* *rename* *→* *remove* *+* *progress* *new* *state*.  
- **6–9** *→* *UI* *exclusão* *mútua* *e* *cópia* *Controle* *de* *impressoras*.  
- **Não** *coberto* *explicitamente* *neste* *plano* *(*fora* *de* *âmbito* *aceitável* *ou* *follow-up*)*: *confirmação* *de* *pré-definida* *para* *remoção* *se* *o* *código* *actual* *já* *não* *a* *tiver* *—* *se* *existir* *no* *viewmodel* *hoje*, *actualizar* *revisão* *sem* *regressão* *(*ver* *código* *antes* *de* *fechar* *a* *tarefa* *6*).*

*Placeholders* *(*TBD* *)*: *nenhum* *intencional* *no* *plano* *acima*; *CIM* *deixa* *a* *escolha* *WMI* *vs* *process* *como* *decisão* *de* *implementação* *documentada* *no* *comentário* *do* *PR* *ou* *bloco* *// *no* *ficheiro* *após* *escolher* *uma* *via* *estável*.*

---

**Plano* *guardado* *em* *`docs/superpowers/plans/2026-04-27-printer-control.md`*. *Opções* *de* *execução* *(*quando* *quiseres* *implementar*):*

1. **Subagente** *a* *cada* *tarefa* *(*recomendado* *)* *—* *subagent-driven-development*  
2. **Sessão* *única* *com* *checkpoints* *—* *executing-plans*

*Indica* *qual* *preferes* *quando* *for* *abrir* *a* *implementação* *(*não* *é* *obrigatório* *responder* *agora* *para* *este* *documento* *estar* *completo* *).* 

**Nota* *sobre* *commit:* *Não* *fazer* *commit* *até* *sinal* *do* *utilizador* (*política* *do* *projecto* *).* 

**Especificação* *de* *desenho* *associada* *(*sem* *wireframe* *—* *ficheiro* *eliminado* *a* *pedido* *do* *utilizador*):* *`docs/superpowers/specs/2026-04-27-printer-control-design.md`.*
