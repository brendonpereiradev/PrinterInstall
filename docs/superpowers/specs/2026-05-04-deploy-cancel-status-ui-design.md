# Especificação de desenho — UI de estado no cancelamento e reversão do deploy

**Data:** 2026-05-04  
**Estado:** Aprovada pelo proprietário do produto (confirmação em chat)  
**Especificação relacionada:** `2026-04-29-deploy-cancel-rollback-design.md` (journal, `CancellationToken`, `DeploymentRollbackRunner`); `2026-04-27-printer-control-design.md` (orquestrador de controlo e eventos de progresso)

## 1. Objectivo

Quando o utilizador **cancela** um deploy em curso e opcionalmente corre a **reversão** remota, a grelha **Status** deve reflectir de forma **clara e incremental** o que está a acontecer — **linha-a-linha**, alinhada aos eventos de progresso do rollback — em vez de ficar presa no último estado de deploy (ex.: «Configurando impressora»).

## 2. Requisitos funcionais

### 2.1 Estados visuais novos (`TargetMachineState`)

Acrescentar valores ao enum (nomes exactos fixados no plano de implementação, exemplos abaixo):

| Estado (exemplo) | Significado |
|------------------|-------------|
| `DeployCancelled` | Utilizador cancelou; deploy interrompido nessa linha (antes ou sem passos de rollback visíveis). |
| `RollbackRemovingQueue` | Reversão em curso: **remoção da fila** associada a esta linha. |
| `RollbackRemovingPort` | Reversão em curso: **remoção de porta TCP órfã** relevante para esta linha. |
| `RolledBack` | Reversão **concluída com sucesso** para o efeito representado nesta linha. |

**Falhas durante o rollback:** reutilizar **`Error`** (e mensagem do evento) na linha afectada, salvo decisão no plano de usar um estado dedicado de aviso; **YAGNI:** `Error` para falhas, mensagem explícita.

**`CompletedSuccess` revertido:** a linha deve transitar por estados de rollback (`RollbackRemovingQueue` → `RolledBack` ou equivalente), não permanecer verde «Concluído com sucesso».

### 2.2 Enriquecimento de `PrinterRemovalProgressEvent`

Para *match* fiável linha-a-linha **sem** analisar texto livre das mensagens:

- Estender `PrinterRemovalProgressEvent` com propriedades opcionais, por exemplo **`PrinterQueueName`** e **`PortName`** (nullable), preenchidas em:
  - `PrinterControlOrchestrator` nos relatórios de remoção de fila / porta órfã / erros quando aplicável;
  - `DeploymentRollbackRunner` nos relatórios de port-only (contagem / remoção).

Manter construtor ou inicializadores compatíveis com todas as chamadas existentes (valores por defeito `null`).

### 2.3 Fluxo na `MainViewModel`

1. Ao captar **`OperationCanceledException`** após `RunAsync` do deploy: para cada linha em `Targets` que ainda esteja em estado **intermédio** de deploy (`ContactingRemote`, `ValidatingDriver`, `InstallingDriver`, `DriverInstalledReconfirming`, `Configuring`), definir **`DeployCancelled`** e mensagem curta (recurso pt-BR).
2. Se existir reversão (`journal.HasRollbackWork`): no callback de `IProgress<PrinterRemovalProgressEvent>` do rollback (no *dispatcher*), actualizar **a(s) linha(s)** correspondente(s):
   - `RemovingQueue` + `ComputerName` + `PrinterQueueName` → `RollbackRemovingQueue`;
   - `RemovingOrphanPort` + `ComputerName` + `PortName` (ou regra de *match* com a definição da linha) → `RollbackRemovingPort`;
   - Conclusão local bem-sucedida para essa fila/porta → `RolledBack`;
   - Erros → `Error` + mensagem.
3. Sincronizar com eventos já emitidos pelo `PrinterControlOrchestrator` (`ContactingRemote`, `TargetCompleted`, `RenamingQueue` — não aplicável ao rollback puro de deploy; `Warning` — mapear para mensagem ou estado conforme plano).

### 2.4 Localização (pt-BR)

- `TargetMachineStateDisplay`: rótulos para todos os novos estados.  
- Recursos para mensagens fixas usadas na VM (cancelamento, reversão concluída na linha, etc.) em `UiStrings` ou equivalente, coerente com o projecto.

### 2.5 Conversores visuais

- `TargetMachineStateToBrushConverter` e `TargetMachineStateToIconConverter`: *cases* para os novos estados.  
- **Rollback em curso:** estilo «actividade» distinto do deploy normal se possível (cor/ícone de *undo* ou spinner semântico).  
- **`RolledBack`:** estilo **neutro** ou verde-acinzentado, **distinguível** de `CompletedSuccess` (deploy não revertido).

### 2.6 Resumo após operação

Ajustar `BuildSummaryText` / contadores para incluir linhas **`RolledBack`** e/ou **`DeployCancelled`**, evitando resumo que pareça apenas sucesso bruto quando houve cancelamento e reversão.

## 3. Arquitectura

| Componente | Alteração |
|------------|-----------|
| `PrinterInstall.Core` | Extensão de `PrinterRemovalProgressEvent`; actualização dos `Report` em `PrinterControlOrchestrator` e `DeploymentRollbackRunner`. |
| `TargetMachineState` | Novos membros do enum. |
| `MainViewModel` | Actualização de `Targets` no cancel e no `rbProgress`. |
| `TargetMachineStateDisplay`, conversores WPF | Novos *cases*. |
| Testes | Preferir método puro ou serviço leve testável para mapeamento evento → estado de linha; testes Core para construção de eventos com metadados. |

## 4. Casos-limite

- **Journal só com porta:** a linha da impressora correspondente (mesmo PC e definição cuja porta foi criada) deve receber `RollbackRemovingPort` / `RolledBack` conforme o evento; o *match* usa `PortName` no evento e o nome de porta derivado da definição na linha (`BuildPortName` coerente com o deploy).  
- **Várias linhas no mesmo PC:** actualizar só a linha cujo `PrinterQueueName` / porta coincide.  
- **Evento sem metadados suficientes (legado):** plano deve prever *fallback* mínimo (ex.: actualizar todas as linhas do PC em rollback genérico) **só** se algum caminho não preencher os novos campos — idealmente todos os caminhos preenchem.

## 5. Fora de âmbito

- Alterar o assistente **Controle de impressoras**.  
- Persistir estado da sessão em disco.

## 6. Próximo passo

Criar plano de implementação em `docs/superpowers/plans/` com ordem de entrega (Core → enum + UI + VM + testes + verificação manual). **Commits** apenas quando o proprietário do produto solicitar.
