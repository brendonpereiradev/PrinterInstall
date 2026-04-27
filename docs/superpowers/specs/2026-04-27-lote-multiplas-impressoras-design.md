# Especificação de desenho — Lote: várias impressoras no mesmo plano (vários PCs)

**Data:** 2026-04-27  
**Estado:** Para revisão  
**Especificação relacionada:** `2026-04-16-printer-remote-config-design.md` (configuração remota base); `2026-04-17-printer-driver-install-design.md` (instalação de driver quando em falta)

## 1. Objectivo

Permitir que o operador defina **mais de uma impressora** (cada qual com a sua marca, nome de exibição, anfitrião TCP, porta e protocolo) e aplique o **mesmo conjunto** de definições a **um ou mais computadores** numa única execução, em linha com o fluxo actual de **credenciais de domínio** e canais **WinRM → CIM**.

Hoje, um `PrinterDeploymentRequest` representa **uma** impressora × **N** alvos. Esta spec alarga o modelo a **K** definições de impressora × **N** alvos, mantendo as regras de negócio existentes (validação/instalação de driver, criação de porta TCP, criação de fila, página de teste opcional).

## 2. Decisões de produto (resumo do brainstorming)

| # | Tema | Decisão |
|---|------|---------|
| 1 | Propagação do lote | **A** — a **mesma** lista de definições de impressora aplica-se a **todos** os PCs alvo. |
| 2 | Falha parcial | **1** — **continuar sempre** (melhor esforço); no fim, resumo do que teve sucesso, ignorado e falhou. |
| 3 | Fila já existente (mesmo nome de exibição) | **S** — **não** contar como falha; tratar como **ignorado** com registo claro (ex.: “já existia”) e continuar. |
| 4 | Ordem de execução | **Por computador, depois por impressora:** para cada PC (ordem da lista de alvos), percorre **todas** as definições de impressora (ordem da lista de definições). |
| 5 | Página de teste | Nível **do pedido** (global), como hoje — **não** por fila na primeira entrega, para manter a UI e o modelo simples. |
| 6 | Importação ficheiro (CSV/JSON) | **Fora de âmbito** desta spec; extensão futura sobre o mesmo modelo de dados. |
| 7 | Paralelismo entre alvos | **Fora de âmbito** — mantém-se **sequência por PC** tal como a spec base. |

## 3. Comportamento funcional

### 3.1 Início de cada alvo (PC)

1. Estabelecer o canal remoto (como hoje) e obter a lista de drivers **uma vez** por PC.
2. Determinar o conjunto de **marcas** exigido pelo lote. Para cada marca necessária cujo driver ainda não esteja satisfeito, aplicar a lógica existente de resolução (incl. instalação a partir de pacote local, quando aplicável) **antes** de criar portas/filas para qualquer definição desse PC.
3. Isto evita falhas a meio do aninhamento por “driver em falta” que poderia resolvido proactivamente após a primeira listagem.

### 3.2 Por cada definição de impressora nesse alvo

1. **Se já existir** uma fila de impressão com o **mesmo nome de exibição** (comparação alinhada ao identificador usado hoje no `Add-Printer` / `DeviceID`): **não** chamar criação de porta nem de fila; emitir evento de progresso de **ignorado** com mensagem explícita; avançar para a próxima definição.
2. Caso contrário: validar o driver esperado **para a marca desta** definição; criar porto TCP (regra de nome de porta inalterada em relação ao anfitrião/porta); adicionar impressora; se o pedido tiver `PrintTestPage` global, enviar página de teste após o sucesso desta criação (como hoje, incluindo tratamento de falha de teste sem invalidar a criação).
3. Qualquer excepção noutro passo: registar **erro** com identificação de PC e nome de fila; **continuar** com a definição seguinte e, depois, com o PC seguinte.

### 3.3 Concorrência e portas

- Várias definições podem apontar para o **mesmo** (anfitrião, porta) com **nomes de fila** diferentes; o Windows pode partilhar a **mesma** porta lógica. Este comportamento é **aceite**; documentar na ajuda/operador que, se a intenção fosse portas lógicas distintas, não devem duplicar o par (anfitrião, porta) com intenções conflituosas no mesmo lote.
- A função de construção de nome de porta existente (ex. sufixo quando porta ≠ 9100) **mantém-se** por definição.

## 4. Arquitectura e componentes

### 4.1 Modelo de domínio

- Introduzir um tipo (nome indicativo) para **definição de impressora** no lote, contendo os campos hoje no `PrinterDeploymentRequest` a nível de **uma** impressora: `Brand`, `DisplayName`, `PrinterHostAddress`, `PortNumber`, `Protocol` (e referência a modelo de catálogo, se a UI actual o exigir).
- O pedido alargado (nome indicativo: `MultiPrinterDeploymentRequest` ou evolução de `PrinterDeploymentRequest`) contém: lista de alvos, lista de definições de impressora, credencial, `PrintTestPage`, e quaisquer campos de pedido compartilhados que hoje existam.
- A implementação pode deprecar o uso de “um único bloco de impressora” a favor de uma lista de tamanho ≥ 1, ou manter **compatibilidade** migrando o formulário “uma impressora” para uma única linha — decisão de implementação, desde que a semântica seja a mesma.

### 4.2 Orquestrador

- O `PrinterDeploymentOrchestrator` (ou módulo dedicado) implementa o duplo aninhamento **(PC, impressora)**, reutilizando as operações remotas e a lógica de driver **sem** duplicar N execuções completamente independentes que releiam drivers por cada impressora.
- A responsabilidade de “ignorar se já existe” fica no orquestrador, apoiada num prédicado no remoto (ver abaixo), **não** em adivinhar a partir de excepções de `Add-Printer` por ser menos fiável e mais difícil de documentar.

### 4.3 `IRemotePrinterOperations` (ou extensão coerente)

- Novo membro, por exemplo: `Task<bool> PrinterQueueExistsAsync(string computerName, NetworkCredential credential, string printerDisplayName, CancellationToken cancellationToken)`.
- Implementação **WinRM** e **CIM** (e `Composite` com a mesma ordem de tentativa do produto) com paridade; pode reutilizar a existência lógica já usada no canal CIM em `AddPrinter` (*PrinterExists*), agora **exposto** e simétrico no WinRM (*Get-Printer* / equivalente idempotente e só leitura).
- *Default* com `NotSupportedException` ou padrão do projecto para novos membros, **se** ainda houver *defaults*; preferível implementar em ambos os canais de produção na mesma entrega.

### 4.4 Eventos de progresso

- Alargar `DeploymentProgressEvent` (ou equivalente) com um campo opcional identificando a **fila** / nome de exibição quando o evento se refere a uma definição concreta; `null` ou vazio = evento ao nível do **PC** (fases gerais: contacto, listagem de drivers, instalação de driver, etc.).
- Incluir distinção explícita de estado **ignorado** (já existia) se o `TargetMachineState` actual não tiver *slot*; caso contrário, documentar o mapeamento (ex. `CompletedSuccess` com mensagem prefixada) — a implementação deve ser **fácil** de filtrar na grelha e no resumo.

### 4.5 UI (WPF / MVVM)

- **Alvos:** inalterado em conceito.
- **Bloco de definições:** lista **editável** (adicionar/remover **linhas**), cada qual com os mesmos controlos que a impressora única actual.
- **Grelha de progresso / log:** colunas que permitam ver **PC** e **nome da fila** (ou fase geral do PC). Estados: a correr, sucesso, ignorado, erro.
- **Fim do lote:** resumo com totais e lista breve de falhas; ação de **copiar** o resumo (texto) para a área de transferência.

## 5. Requisitos não funcionais e continuidade

- **Segurança e credenciais:** iguais à spec base; credenciais só em memória, sem alteração.
- **Cancelamento:** `CancellationToken` respeitado **entre** entradas (definição ou PC). Durante uma única operação remota, o cancelamento segue a capacidade existente do canal/timeout.
- **Telemetria/auditoria:** reutilizar o padrão de log textual com *timestamp* já adoptado; incluir no resumo final os totais acordados.

## 6. Testes (aceitação técnica)

- Testes unitários do orquestrador com *doubles* de `IRemotePrinterOperations`:
  - **Ordem** PC → impressoras;
  - *Skip* quando `PrinterQueueExistsAsync` retorna `true` — sem chamadas a criar porto/fila para essa entrada;
  - **Continuação** após excepção noutra entrada;
  - **Uma** chamada a listar drivers no início do ciclo de cada PC (não N redundantes por impressora, salvo revalidação explícita após instalação de driver, que já exista na lógica actual).
- Testes de contrato leves nos adaptadores remoto, se o projecto o fizer noutros fluxos, para o novo *exists*.

## 7. Riscos e mitigação

| Risco | Mitigação |
|-------|------------|
| *Exists* vs nomes ligeiramente diferentes no Windows | Usar a mesma chave de identificação que a criação (nome passado a `Add-Printer`); testar WinRM e CIM com 1–2 alvos reais. |
| Lote grande × muitos PCs | Mantém-se sequencial; o operador percebe tempos longos; *fora de âmbito* paralelizar nesta spec. |
| Diferentes marcas no mesmo lote | Coberto por verificação de driver por **marca** e instalação por marca no início do alvo. |

## 8. Aprovação e próximos passos

- Após aprovação desta spec, escrever o **plano de implementação** (tasking) com a skill *writing-plans* e executar tarefa a tarefa.
- **Não** alterar a spec de controlo de filas (`2026-04-27-printer-control-design.md`); o presente lote destina-se ao **fluxo de deploy** existente, não ao assistente de renomear/remover.

---

**Auto-revisão (inline):** âmbito delimitado (sem ficheiro de import, sem paralelismo de alvos); requisitos alinhados às secções 1–4 aprovadas no brainstorm; nenhum `TBD` propositado.
