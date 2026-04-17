# Especificação de desenho — Remoção remota de impressoras (selecção por alvo)

**Data:** 2026-04-17  
**Estado:** Aprovado para plano de implementação  
**Especificação relacionada:** `2026-04-16-printer-remote-config-design.md` (instalação/configuração remota)

## 1. Objectivo

Permitir que um administrador de domínio **liste remotamente** as filas de impressão em cada computador alvo, **seleccione por máquina** quais remover, e **execute** a remoção com o **mesmo** modelo de credenciais e canais remotos (WinRM com fallback CIM/WMI) da aplicação existente. A remoção inclui **eliminar a porta TCP/IP associada apenas quando deixar de estar referenciada** por qualquer fila (política de “porta órfã”). O produto **continua a não desinstalar drivers** nos alvos.

## 2. Decisões de produto (resumo)

| Tema | Decisão |
|------|---------|
| Identificação das filas | Listagem remota + selecção na UI (não só nomes digitados). |
| Vários computadores | Selecção **por máquina** (conjuntos diferentes por alvo). |
| Porta TCP após remover fila | Remover fila; **se** nenhuma outra impressora usar essa porta, remover a porta. |
| Impressora predefinida | **Aviso** e **confirmação extra** antes de executar; permitir continuar. |
| Experiência | **Assistente (wizard)** próprio, aberto a partir do menu ou botão no shell principal. |

## 3. Abordagens consideradas e selecção

1. **Estender `IRemotePrinterOperations` + orquestrador dedicado de remoção** — Novos métodos na abstracção remota; `PrinterRemovalOrchestrator` recebe um plano por máquina; o assistente só recolhe dados e invoca a Core. Paridade WinRM/CIM como no resto do produto.  
   **Seleccionada.**

2. **Serviço monolítico “instalar + remover” na Core** — Menos ficheiros, mas mistura responsabilidades e prejudica testes e manutenção.

3. **Remoção apenas via WinRM** — Mais rápido num canal, mas quebra o requisito de fallback CIM para alvos sem WinRM.

## 4. Requisitos funcionais

### 4.1 Entrada e credenciais

- Reutilizar **credenciais de domínio em memória** (`ISessionContext` ou equivalente); não persistir segredos.
- Lista de **um ou mais computadores** (FQDN ou NetBIOS), com a mesma validação mínima de cliente que o ecrã principal (não vazio, formato simples).

### 4.2 Assistente (fluxo)

1. **Alvos:** introdução da lista de computadores pela ordem de processamento desejada.
2. **Por cada alvo, na ordem:** contacto remoto → **listar filas** com, no mínimo: **nome da fila**, **nome da porta**, **indicador de predefinida** → o utilizador **multi-selecciona** zero ou mais filas → avança para o próximo alvo ou para revisão.
3. **Revisão:** resumo (máquina → filas a remover). Para cada fila que na listagem era **predefinida**, mostrar aviso e exigir **confirmação explícita adicional** antes de permitir executar.
4. **Execução:** processamento **sequencial** por máquina (alinhado ao deploy). Progresso e **log** textual com timestamp, sem registar segredos.

### 4.3 Comportamento de remoção (por fila)

- Para cada fila seleccionada num alvo, **remover a fila** primeiro.
- **Depois**, para a **porta** associada a essa fila: se **nenhuma** impressora remanescente no alvo usar esse nome de porta, **remover a porta TCP/IP** correspondente. Se outra fila ainda referenciar a mesma porta, **não** remover a porta.
- Se a fila **já não existir** no momento da remoção, tratar como operação **idempotente** (sucesso ou mensagem clara “já removida”), não como falha genérica.

### 4.4 Pré-definida durante a execução

- Opcionalmente **revalidar** imediatamente antes de remover cada fila marcada como predefinida na revisão, para reduzir inconsistências se o utilizador demorar entre passos.

## 5. Arquitectura e componentes

### 5.1 Core

- **`IRemotePrinterOperations`:** além dos métodos existentes, incluir operações para **listar filas com metadados**, **remover fila**, e **remover porta TCP** quando aplicável (ou métodos compostos bem definidos que preservem a política de porta órfã).
- **`WinRmRemotePrinterOperations` / `CimRemotePrinterOperations`:** implementações espelhadas (PowerShell vs WMI/CIM), com escape de nomes coerente com o código actual.
- **`CompositeRemotePrinterOperations`:** WinRM primeiro, CIM em fallback, como hoje.
- **`PrinterRemovalOrchestrator`:** entrada = plano por máquina (credencial + lista de alvos + por alvo: conjunto de nomes de fila). Ordem de remoção dentro do alvo: **definir uma regra fixa** (por exemplo ordem alfabética do nome da fila) e documentá-la no plano de implementação.
- **DTOs:** `RemotePrinterQueueInfo` (ou nome equivalente), `PrinterRemovalPlan`, `PrinterRemovalTarget`; logs sem credenciais.

### 5.2 App (WPF)

- **Assistente** em janela ou `UserControl` dedicado, com os passos da secção 4.2.
- **Entrada no produto:** item de menu ou botão no shell principal que abre o assistente.
- Reutilizar padrões MVVM e injecção já usados na solução.

## 6. Fluxo de dados e operações remotas

### 6.1 Canais

- **Listagem WinRM:** `Get-Printer` (ou equivalente) expondo nome, porta e predefinida.
- **Listagem CIM:** `Win32_Printer` mapeado para o mesmo DTO.
- **Remoção WinRM:** `Remove-Printer` com nome da fila.
- **Remoção CIM:** eliminar a fila via API suportada (`Win32_Printer`), mantendo semântica idempotente onde fizer sentido.
- **Porta órfã WinRM:** após remoção da fila, verificar se alguma fila ainda usa o nome da porta; caso contrário, `Remove-PrinterPort`.
- **Porta órfã CIM:** análogo com `Win32_TCPIPPrinterPort` (ou classe já usada no projecto para portas TCP), com a mesma regra de “nenhuma fila referencia”.

### 6.2 Progresso e cancelamento

- Reportar progresso através do mesmo tipo de padrão que `PrinterDeploymentOrchestrator` (por exemplo `IProgress` com eventos por máquina e, se útil, por fila).
- Propagar `CancellationToken` a todas as chamadas remotas; ao cancelar, concluir a operação corrente e registar estado parcial no log.

## 7. Erros, casos-limite e testes

### 7.1 Erros

- **Falha ao listar** num alvo: estado de erro para esse alvo; não impedir que o utilizador continue a rever outros alvos já preenchidos; permitir repetir ou retirar o alvo do plano.
- **Falha ao remover uma fila** no meio de um conjunto: registar erro **por fila**; **continuar** com as restantes filas do mesmo alvo e depois com os outros alvos; o log deve identificar explicitamente as filas falhadas.
- **Falha ao remover porta órfã:** registar como **aviso** (a fila já foi removida); não falhar a operação global por isso. Opcional: uma repetição após breve atraso se for necessário por consistência do spooler.

### 7.2 Casos-limite

- **Porta partilhada:** nunca remover a porta se outra fila ainda a referenciar.
- **Lista desactualizada entre listagem e execução:** aceitável; erros aparecem no log. A revalidação opcional da predefinida mitiga o caso mais sensível.
- **Nenhuma fila seleccionada** para um alvo: definir um comportamento único (por exemplo: não efectuar chamadas remotas de remoção nesse alvo, ou omitir o alvo do plano de execução) e manter-se consistente na UI.

### 7.3 Testes

- **Unitários:** lógica de “porta órfã” com dados em memória; construção do plano a partir de selecções do assistente.
- **Testes com mocks** de `IRemotePrinterOperations`: lista vazia, predefinida presente, falha intermédia, porta partilhada, sucesso completo.
- **Manuais:** pelo menos um alvo em domínio — fluxo completo, confirmação de predefinida, verificação no Windows de filas e portas após a política de porta órfã.

## 8. Fora de âmbito

- Desinstalação ou reparação de **drivers** nos alvos.
- Remoção em **paralelo** de vários alvos (mantém-se sequencial como o deploy).
- Descoberta de computadores por OU/LDAP (continua lista manual).

## 9. Próximo passo

Criar **plano de implementação** detalhado (interfaces exactas, nomes de comandos PowerShell/CIM, vistas do assistente, ordem de entrega e testes) com base nesta especificação, seguindo o fluxo *writing-plans* quando for altura de implementar.
