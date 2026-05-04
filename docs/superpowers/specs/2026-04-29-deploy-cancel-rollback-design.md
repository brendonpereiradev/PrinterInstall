# Especificação de desenho — Cancelar deploy e reverter alterações nos alvos

**Data:** 2026-04-29  
**Estado:** Para revisão do proprietário do produto  
**Especificação relacionada:** `2026-04-16-printer-remote-config-design.md` (configuração remota); `2026-04-17-printer-removal-design.md` e `2026-04-27-printer-control-design.md` (remoção de fila e política de porta órfã)

## 1. Objectivo

Durante uma execução de **deploy** de impressoras remotas, permitir ao operador:

1. **Parar** a operação em curso de forma cooperativa (`CancellationToken`).
2. **Reverter nos computadores alvo** apenas o que esta **mesma execução** criou com sucesso — sem remover filas “por nome do formulário” se não tiverem sido criadas por este percurso (**critério A**, acordado em brainstorming).

O produto **não** desinstala drivers nos alvos durante a reversão (alinhado ao âmbito das specs de remoção/controlo).

## 2. Decisões de produto (resumo)

| Tema | Decisão |
|------|---------|
| Rótulo do controlo na UI (pt-BR) | Botão **«Cancelar deploy»** — visível (ou activável) **durante** um deploy em execução; não implica outro nome na mesma acção. |
| O que reverte | Apenas entradas num **journal** alimentado **na Core** após sucesso remoto confirmado: ver secção 4. |
| Fila já existente antes do deploy | Ramo **SkippedAlreadyExists** — **não** cria porta nem fila; **não** entra no journal; reversão **não** a remove. |
| Drivers instalados a meio do deploy | **Fora** da reversão automática (mantêm-se instalados). |
| Política de porta após remover fila | **Igual** à do `PrinterControlOrchestrator`: remover porta TCP só se **nenhuma** fila no alvo a referenciar (porta órfã). |
| Compensação de “só porta” | Se **CreateTcpPrinterPort** tiver sucesso e **AddPrinter** **não** for concluído com sucesso nesta execução (falha, cancelamento antes do registo da fila), o journal trata a **porta** para limpeza: `CountPrintersUsingPort` → se zero, `RemoveTcpPrinterPort` (mesma semântica que porta órfã). |

## 3. Abordagem técnica seleccionada

**Journal na Core + reutilização do orquestrador de controlo para filas**

- Alternativa rejeitada: journal montado só na UI a partir de eventos de progresso — duplica regras e é frágil.
- Alternativa rejeitada: mini-transacções formais por máquina sem ganho claro face ao journal único.

## 4. Journal de rollback (`DeploymentRollbackJournal`)

Estrutura **append-only** por corrida de deploy (uma instância por `RunAsync` ou equivalente).

**Registo de fila (para reversão via controlo):** após `AddPrinterAsync` concluir **sem erro**, acrescentar par **(computador, nome da fila, nome da porta)** — o nome da porta deve ser o usado na criação (coerente com `BuildPortName` / protocolo actual).

**Registo de porta sem fila:** após `CreateTcpPrinterPortAsync` concluir **sem erro**, se a fila correspondente **não** chegar a ser registada no journal (por falha, cancelamento, ou excepção antes do `AddPrinter` bem-sucedido), manter entrada **só porta** **(computador, nome da porta)** para o passo de compensação directa (contagem + remoção se órfã).

**Ordenação na reversão:** agrupar por computador; para filas, reutilizar `PrinterControlOrchestrator` com `PrinterControlRequest` contendo **apenas** `QueuesToRemove` (sem renomeações), construído a partir das entradas “fila+porta”; ordenação interna de remoções alinhada ao orquestrador actual (ex.: alfabética por nome de fila). **Depois** (ou em paralelo bem definido por alvo), processar entradas **só porta** que nunca tiveram fila registada nesta execução, sem passar pela remoção de fila inexistente.

**Porta partilhada** por várias definições no mesmo deploy: múltiplas filas no journal com o mesmo `PortName`; ao remover filas, a política de porta órfã só remove a porta quando a última fila que a usava (no conjunto remanescente no servidor) deixar de referenciá-la — comportamento já suportado pela contagem de uso.

## 5. Cancelamento cooperativo no orquestrador de deploy

- `PrinterDeploymentOrchestrator.RunAsync` deve aceitar `CancellationToken` e propagá-lo às chamadas remotas existentes **onde aplicável**.
- Inserir verificações **entre** passos críticos onde hoje não existam — nomeadamente **entre** `CreateTcpPrinterPortAsync` e `AddPrinterAsync`, e antes/depois de passos longos (ex.: página de teste), para que **Cancelar deploy** tenha efeito prático sem esperar só o fim de uma operação.
- `OperationCanceledException`: propagar até ao chamador (VM); o chamador trata o fluxo “deploy interrompido” e, se o journal não for vazio, inicia a fase de reversão (credenciais em memória, como hoje).

## 6. Camada App (WPF)

- `MainViewModel` (ou equivalente): `CancellationTokenSource` **por** deploy; desactivar comando **Deploy** enquanto corre; expor comando **Cancelar deploy** ou botão ligado a `cts.Cancel()`.
- **Recursos (pt-BR):** cadeia dedicada para o rótulo **«Cancelar deploy»** e mensagens de log para “cancelamento pedido”, “a reverter alterações…”, conclusão da reversão e falhas parciais na reversão.
- Após cancelamento: não mostrar o resumo de sucesso habitual **como se** o deploy tivesse terminado normalmente; o log e, se existir, um resumo deve reflectir **cancelado** + resultado da reversão.

## 7. Caso-limite: pedido de cancelamento durante `AddPrinter`

O cancelamento é **cooperativo**: se o servidor concluir a criação da fila **depois** do pedido de cancel no cliente, pode existir uma fila no alvo **sem** entrada no journal. O produto **não** obriga a uma sonda remota pós-cancel na primeira entrega (**YAGNI**); o log pode indicar que alterações residuais podem ser corrigidas com **Controle de impressoras** se o operador detectar inconsistência.

## 8. Erros durante a reversão

Seguir o espírito do controlo/remoção: **melhor esforço** por fila e por porta; erros por item no log; continuar quando possível. Não bloquear toda a reversão por falha num único alvo, salvo decisão explícita futura.

## 9. Testes (orientação)

- **Unitários:** construção do journal e do `PrinterControlRequest` a partir de sequências simuladas (porta OK → fila OK; porta OK → cancelamento antes de `AddPrinter`; duas filas, mesma porta).
- **Mocks** de `IRemotePrinterOperations`: garantir que a reversão **só** invoca remoções para entradas do journal; casos de falha intermédia na reversão.
- **Manuais:** um alvo de domínio — deploy parcial, **Cancelar deploy**, verificar no Windows filas/portas coerentes com o critério A.

## 10. Fora de âmbito

- Persistência do journal em disco para recuperação após crash da app.
- Reversão de alterações feitas pelo **assistente Controle de impressoras** (fluxo separado).
- Paralelismo adicional entre alvos na reversão (mantém-se sequencial como o deploy).

## 11. Próximo passo

Criar **plano de implementação** em `docs/superpowers/plans/` (ficheiros a tocar, interfaces, ordem de entrega, verificação) com o skill *writing-plans*, **após** o proprietário do produto rever e aprovar este documento.

**Nota de repositório:** alterações ao git (commit/push) apenas quando o proprietário do produto o solicitar explicitamente.
