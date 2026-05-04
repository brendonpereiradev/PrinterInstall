# UI de status no cancelamento/reversão do deploy — Plano de implementação

> **Para agentes:** sub-skill recomendado: `superpowers:subagent-driven-development` ou `superpowers:executing-plans`. Checkboxes `- [ ]` para acompanhamento.

**Goal:** Actualizar a grelha de **Status** durante e após **Cancelar deploy** com estados visuais distintos e progresso **linha-a-linha** no rollback, conforme `docs/superpowers/specs/2026-05-04-deploy-cancel-status-ui-design.md`.

**Architecture:** Estender `PrinterRemovalProgressEvent` com `PrinterQueueName` e `PortName` opcionais; preencher em `PrinterControlOrchestrator` e `DeploymentRollbackRunner`; novos valores em `TargetMachineState`; `MainViewModel` actualiza `Targets` no `catch (OperationCanceledException)` e no `Progress<PrinterRemovalProgressEvent>` do rollback; conversores e `TargetMachineStateDisplay` alinhados.

**Tech stack:** .NET 8, WPF, xUnit, Moq.

**Commits:** Apenas quando o proprietário do produto solicitar.

---

## Ficheiros principais

| Ficheiro | Acção |
|----------|--------|
| `src/PrinterInstall.Core/Orchestration/PrinterRemovalProgressEvent.cs` | Adicionar `PrinterQueueName`, `PortName` opcionais (record ou propriedades com defaults). |
| `src/PrinterInstall.Core/Orchestration/PrinterControlOrchestrator.cs` | Passar nome de fila/porta em cada `Report` relevante. |
| `src/PrinterInstall.Core/Orchestration/DeploymentRollbackRunner.cs` | Passar `PortName` nos eventos de port-only. |
| `src/PrinterInstall.Core/Models/TargetMachineState.cs` | `DeployCancelled`, `RollbackRemovingQueue`, `RollbackRemovingPort`, `RolledBack`. |
| `src/PrinterInstall.App/Localization/TargetMachineStateDisplay.cs` | Rótulos pt-BR. |
| `src/PrinterInstall.App/Converters/TargetMachineStateToBrushConverter.cs` | Novos *cases*. |
| `src/PrinterInstall.App/Converters/TargetMachineStateToIconConverter.cs` | Novos *cases*. |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Actualização de linhas no cancel e no rollback; ajuste `BuildSummaryText`. |
| `src/PrinterInstall.App/Resources/UiStrings.resx` + `UiStrings.Designer.cs` | Mensagens fixas adicionais se necessário. |
| `tests/PrinterInstall.Core.Tests/...` | Testes de eventos/orquestrador se útil; testes de mapeamento se extraído. |

---

## Task 1: `PrinterRemovalProgressEvent` enriquecido + Core

- [ ] Alterar o record para incluir `string? PrinterQueueName = null`, `string? PortName = null` (sintaxe compatível com todos os `new PrinterRemovalProgressEvent(...)` existentes — usar parâmetros opcionais ou `with`/construtor secundário conforme o estilo do repo).
- [ ] `PrinterControlOrchestrator`: em `RemovingQueue`, incluir `PrinterQueueName = item.PrinterName`, `PortName = item.PortName`; em `RemovingOrphanPort`, `PortName = item.PortName`; em `RenamingQueue`, nomes antigo/novo (opcional para esta feature — pode deixar só fila antiga em `PrinterQueueName` ou omitir se wizard não usar este plano); erros: quando aplicável, preencher o nome da fila.
- [ ] `DeploymentRollbackRunner`: nos `Report` de orphan port, definir `PortName = portName`.
- [ ] Compilar; `dotnet test` no projecto Core (corrigir qualquer chamada que quebre).

## Task 2: Enum + localização + conversores

- [ ] Adicionar os quatro novos valores a `TargetMachineState` (ordem append ao enum para evitar re-numerar se serializado — projecto local).
- [ ] `TargetMachineStateDisplay`: textos claros (ex.: «Cancelado pelo utilizador», «A remover fila…», «A remover porta…», «Revertido»).
- [ ] `TargetMachineStateToBrushConverter` / `ToIconConverter`: cores e glyphs; `RolledBack` visualmente diferente de `CompletedSuccess`.

## Task 3: `MainViewModel` — lógica de actualização

- [ ] No `catch (OperationCanceledException)`, após logging: iterar `Targets` e definir `DeployCancelled` + mensagem para linhas em estados intermédios listados na spec.
- [ ] No handler `rbProgress`, no *dispatcher*:
  - Implementar método auxiliary `ApplyRollbackProgress(PrinterRemovalProgressEvent e)` que:
    - Encontra a(s) linha(s) por `ComputerName` + `PrinterQueueName` quando não nulo;
    - Para eventos só com `PortName`, encontrar linha(s) no mesmo computador cuja porta esperada coincida (reutilizar regra `BuildPortName` — extrair método estático partilhado em Core, ex. `PrinterPortNaming`, ou duplicar mínimo coerente com `PrinterDeploymentOrchestrator`).
    - Mapear `PrinterRemovalProgressState` para `TargetMachineState` + `Message`.
  - Tratar `TargetCompleted` / `ContactingRemote` do control (ex.: «A iniciar reversão…» nas linhas do journal para esse PC — opcional, documentar no código).
- [ ] Ao **fim** com sucesso de remoção de fila (antes de orphan port na mesma linha): transição para `RolledBack` **quando** a política for «fila removida» OU só após porta órfã dependendo do produto — **decisão:** `RolledBack` quando o **item desta linha** está completamente revertido (fila **e** porta se aplicável). Simplificação aceitável: `RolledBack` após `RemovingQueue` bem-sucedido **e** depois actualizar de novo se houver passo de porta na mesma linha (ou um único `RolledBack` após último passo da orquestração para essa fila). Alinhar com comportamento actual do orquestrador (remove fila, depois orphan).
- [ ] Ajustar `BuildSummaryText` para contar `RolledBack`, `DeployCancelled` e reflectir no texto/linhas de falha.

## Task 4: Verificação

- [ ] `dotnet build` solução; `dotnet test` completo.
- [ ] Manual: dois PCs ou uma lista com duas filas; cancelar a meio; confirmar badges durante remoção e estado final `RolledBack` na linha revertida e `DeployCancelled` / progresso nas outras.

---

## Auto-revisão vs spec

| Requisito | Tarefa |
|-----------|--------|
| Estados novos na grelha | Task 2 |
| Evento com metadados | Task 1 |
| VM cancel + rollback | Task 3 |
| Conversores | Task 2 |
| Resumo | Task 3 |
| Linha-a-linha | Task 1 + 3 |

**Plano guardado em:** `docs/superpowers/plans/2026-05-04-deploy-cancel-status-ui.md`.

**Revisão da spec:** `docs/superpowers/specs/2026-05-04-deploy-cancel-status-ui-design.md` — lê o ficheiro e diz se quiseres alterações **antes** de implementar; **commits** só quando pedires.
