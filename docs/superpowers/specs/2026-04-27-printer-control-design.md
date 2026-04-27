# Especificação de desenho — Controlo remoto de filas (listar, renomear, remover)

**Data:** 2026-04-27  
**Estado:** Aprovada para plano de implementação  
**Especificação relacionada:** `2026-04-17-printer-removal-design.md` (remoção e porta órfã); arquitectura remota e credenciais alinhadas

## 1. Objectivo

Adicionar **renomeação remota** das filas de impressão nas **máquinas-alvo**, reutilizando o mesmo **assistente** e o mesmo **modelo de credenciais** (WinRM com fallback CIM) da aplicação. O fluxo de **remoção** (incluindo **porta TCP órfã** quando aplica) **mantém-se**; a experiência deixa de se apresentar só como “remover” e passa a **“Controle de impressoras”** (pt-BR) — **listagem**, possibilidade de **renomear** e de **remover** num **único plano** e **uma** execução.

## 2. Decisões de produto (resumo)

| Tema | Decisão |
|------|---------|
| Local das operações | **Sempre** nas **máquinas-alvo** (remoto), não só no posto de trabalho do operador. |
| Por fila | **Uma** intenção: **ou** remover **ou** renomear (a UI e a validação **impedem** ambas na mesma linha). |
| Execução | **Um** botão (ex.: “Executar alterações”) aplica, por alvo, **primeiro todas as renomeações**, **depois** todas as remoções (ordem interna estabilizada alfabeticamente por nome *actual* / antigo, por máquina). |
| Nome do produto (UI) | Título da janela do assistente, botão no `MainWindow` e cadeia de recursos: **“Controle de impressoras”** (pt-BR), coerente com listar + renomear + remover. |
| Listagem | Mantém-se a listagem remota **por máquina**; filas **sem** acção (nem remover nem novo nome) são apenas informativas. |
| Pré-definida (remoção) | Comportamento **igual** ao existente: aviso e confirmação adicional no plano de **remoção** quando a fila for predefinida *no momento relevante* (reutilizar a lógica actual). **Renomear** a fila predefinida: em geral o Windows **mantém** a predefinição no mesmo objecto renomeado; **não** exige o mesmo aviso de “remover predefinida” — falhas e mensagens passam **pelo log** (e mensagem clara de erro remoto se o rename falhar). |
| Fora de âmbito adicional | Continuam a não desinstalar **drivers** no alvo; nada de paralelismo extra entre alvos. |

## 3. Abordagens consideradas e selecção

1. **Estender `IRemotePrinterOperations` com `RenamePrinterQueueAsync` + alargar o orquestrador** a um plano de **controlo** (renomear depois remover por alvo), com o **mesmo padrão** `Composite` WinRM → CIM. A UI continua a ser um **único** assistente MVVM. **Seleccionada.**

2. **Orquestrador separado “só remoção” e outro “só rename”** chamados em sequência pela UI — duplica lógica de iteração por alvo, progresso e cancelamento; **rejeitada**.

3. **Apenas WinRM para rename** — **rejeitada** (sustenta-se paridade mínima com o canal CIM, como no resto do produto).

## 4. Requisitos funcionais

### 4.1 Entrada e credenciais

- Reutilizar **credenciais** em `ISessionContext` (não nulas para listar/executar), como hoje.
- Lista de **computadores** com a mesma validação que o passo de introdução do assistente.

### 4.2 Assistente (fluxo) — ajustes em relação à spec de remoção

1. **Alvos:** inalterado (introdução de computadores, ordem de visita).
2. **Por alvo — grelha de filas:** colunas, no mínimo: **nome da fila**, **porta** (informativo), coluna **Remover** (checkbox), coluna **Novo nome** (texto). **Exclusão mútua na mesma linha:** se **Remover** estiver assinalado, **Novo nome** fica inactivo e **vazio**; se o utilizador preencher **Novo nome** (nome novo não vazio e diferente do nome actual), **Remover** desmarca e permanece inactivo nessa intenção. Uma fila com ambos vazios/demarcados = **sem acção**.
3. **Avanço entre alvos:** permitido se existir **pelo menos uma** fila com **intenção** (remoção **ou** renomeação) **ou** se o conjunto for vazio nesse alvo (comportamento alinhado ao de “nada a fazer / avançar” já permitido para listas vazias, conforme implementação presente) — a implementação **deve** permitir avanço quando houver **só renames** sem qualquer checkbox de remover.
4. **Revisão:** texto de resumo **por alvo** que distinga **A renomear** (nome antigo → nome novo) e **A remover** (nome, porta) — a ordem de execução (rename antes de remove) fica explicite no resumo se útil, ou no manual de testes.
5. **Execução num único passo** — aplica renames, depois remoções, com o mesmo padrão de log e de **continuidade** em falhas **por acção** que o orquestrador de remoção já segue, salvo ajuste explícito no plano se for necessário alinhar rename com a mesma política.
6. **Pós-falta de selecção:** se não houver **nenhuma** renomeação **nem** remoção em nenhum alvo, a execução não deve chamar a rede sem necessidade: mensagem clara (equivalente ao “nada seleccionado” actual).

### 4.3 Comportamento remoto — renomear

- **Entrada** por operação: nome **actual** da fila, nome **novo** (validado localmente: não vazio, diferente do actual, *trim*; evitar conflito com outro **nome já listado** nessa máquina no momento da selecção).
- **Se** a fila **já não existir** com o nome antigo, tratar como **idempotente** ou aviso, em linha com a remoção (mensagem clara, não colapsar silenciosamente o resto do plano).
- **WinRM:** `Rename-Printer` (ou `Set-Printer` com parâmetros equivalentes suportados no alvo) com escape de nomes alinhado a `RemovePrinterQueueAsync` / `AddPrinterAsync`.
- **CIM (WMI/DCOM):** implementar **paridade** com o efeito de `Rename-Printer` (por exemplo, via método suportado em `Win32_Printer` no alvo, ou mecanismo remoto de execução já usado noutra operação CIM, documentado no plano com caminho concreto no código). **Não** introduzir dependência de WinRM para o *único* caminho CIM; o **objectivo** é o mesmo *resultado* no spooler.

### 4.4 Comportamento remoto — remoção (inalterado na essência)

- Inalterado relativamente a `2026-04-17-printer-removal-design.md`: após remoção de cada fila, **porta órfã** conforme a política existente, etc.

## 5. Arquitectura e componentes

### 5.1 Core

- **`IRemotePrinterOperations`:** adicionar `Task RenamePrinterQueueAsync(string computerName, NetworkCredential credential, string currentName, string newName, CancellationToken cancellationToken)`.
- **`WinRmRemotePrinterOperations` / `CimRemotePrinterOperations` / `CompositeRemotePrinterOperations`:** implementar e encadear o novo método.
- **Modelo de pedido de execução** (nomes a fixar no plano): estructura **por alvo** com duas listas: **renomeações** (par antigo/novo) e **itens a remover** (reutilizar `PrinterRemovalQueueItem` ou equivalente com nome + porta). Um único `…Request` com credencial e lista de alvos, ou evolução do `PrinterRemovalRequest` com campos adicionais — preferir **nomes e tipos** que leiam *controlo* e não *só* remoção, desde que a migração no código fique tracável.
- **Orquestrador:** alargar o existente (ou `PrinterControl…`) para: por cada alvo, **executar renames** (bloco) **e depois** a sequência de remoções com a lógica actual de fila/ porta. **Não** remover a porta de uma fila **antes** de a renomear *se* o plano tiver a mesma fila nas duas listas (o desenho de produto **já o impõe**); o orquestrador ainda **deve** ser robusto se os dados forem inconsistentes.
- **Progresso:** alargar o enum/ eventos (ex.: estado **Renomear fila** ou `RenamingQueue`) para que o log mostre renames e removals de forma distinta, sem apagar a semântica existente de erro/aviso.

### 5.2 App (WPF)

- Janela e ViewModel do assistente: ajustar `SelectableQueueRow` (ou equivalente) com `NewName`, subscripcionalidade para exclusão mútua e revalidação de `CanExecute` de avanço.
- `RemovalWizardWindow` / `RemovalWizardViewModel` — alvejar **rebatizar** ficheiros e tipos para `PrinterControl…` no plano de implementação, **ou** manter **nomes de tipo** e mudar **apenas** recursos/ título se o *diff* de rotura for grande; a decisão fica no plano com um critério: **consistência de repositório** vs **tamanho do refactor** (YAGNI no *rename* de tipos se só recursos forem trocados).
- Recursos: `Main_RemovePrintersButton` → texto **“Controle de impressoras…”**; cadeia `Removal_*` evolui ou duplica com prefixo **Control_** (definir no plano para não deixar chaves orfãs).
- Acessibilidade: cabeçalhos de coluna legíveis (“Remover” vs “Novo nome”); foco/atalhos podem replicar o padrão existente do `DataGrid`.

## 6. Erros, casos-limite e testes

### 6.1 Validação local

- Novo nome: não vazio, ≠ nome actual (após *trim*), e sem duplicar outro “novo nome” **no mesmo alvo** na mesma submissão; opcional: verificar *collisions* com nomes *já listados* que não entram no plano de rename (evita pedidos óbvios a falhar no servidor).
- A mesma fila *não* pode constar a remover e a renomear: garantido na UI; teste unitário na construção do pedido, se a camada a extrair tiver lógica.

### 6.2 Testes

- **Unitários** do orquestrador: ordem **renames antes de removes**; alvo com só renames, só removes, *mix*; `CancellationToken`.
- **Mocks** de `IRemotePrinterOperations`: falha a meio de renames, continuação de removes; falha em remoção após renames *bem-sucedidos*.
- **Manuais** (humano): um alvo de domínio, rename + remove no mesmo lote, verificar nomes e portas no alvo, e re-leitura do assistente.

## 7. Fora de âmbito

- Alterar spooler / drivers / permissões além de renomear fila, remover fila e política de porta **já* definida.
- Sincronizar nomes de fila em GPO, perfis móveis ou aplicações de terceiros.

## 8. Próximo passo

Criar **plano de implementação** (interfaces exactas, passos, ficheiros, ordem e verificação) em `docs/superpowers/plans/2026-04-27-printer-control.md`, e só depois *implementação* e testes. **Não** efectuar *commit* no repositório sem pedido explícito do *product owner* (política do projecto).
