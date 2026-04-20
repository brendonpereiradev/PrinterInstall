# Especificação de desenho — Página de teste automática após deploy da impressora

**Data:** 2026-04-20  
**Estado:** Aprovado para plano de implementação  
**Relacionado:** `2026-04-16-printer-remote-config-design.md`, orquestrador `PrinterDeploymentOrchestrator`

## 1. Objectivo

Após **configuração bem-sucedida** da fila de impressão num computador alvo (porta TCP criada e `Add-Printer` / `Win32_Printer` concluídos sem erro), o operador pode optar por **enviar uma página de teste** para essa fila, no próprio alvo, usando as **mesmas credenciais e canais remotos** (WinRM com fallback WMI/CIM) já usados no produto.

A decisão é **opt-in** na interface principal: checkbox **desmarcado por defeito**; aplica-se a **todos** os alvos da operação corrente (sem persistência entre sessões da aplicação).

## 2. Comportamento funcional

1. **Modelo:** `PrinterDeploymentRequest.PrintTestPage` (`bool`, predefinição `false`). Quando `false`, o orquestrador **não** chama `PrintTestPageAsync` e reporta `CompletedSuccess` com `"Done"` após `AddPrinterAsync`. Quando `true`, segue os pontos seguintes.
2. **Momento do gatilho:** imediatamente após `AddPrinterAsync` completar com sucesso para o alvo corrente, antes de reportar conclusão final ao utilizador.
3. **Nome da fila:** o nome da fila é `PrinterDeploymentRequest.DisplayName` (o mesmo passado a `AddPrinterAsync`).
4. **Progresso:** reportar um passo intermédio sob `TargetMachineState.Configuring` com mensagem curta em inglês, alinhada ao orquestrador existente (ex.: `"Sending test page..."`).
5. **Sucesso da página de teste:** reportar `TargetMachineState.CompletedSuccess` com mensagem de conclusão (ex.: `"Done"` ou texto equivalente).
6. **Falha da página de teste com configuração OK:** **não** reverter a fila nem marcar o alvo como `Error`. Reportar `CompletedSuccess` com mensagem que deixa explícito que a configuração terminou mas a página de teste falhou, incluindo o motivo (via `Flatten` de excepção já usado no orquestrador). Motivos típicos: impressora offline, fila pausada, erro do spooler.
7. **Cancelamento:** se `CancellationToken` for cancelado durante o envio da página de teste, propagar `OperationCanceledException` como nas outras etapas (não converter em “sucesso com aviso”).
8. **Vários alvos:** com `PrintTestPage == true`, cada máquina na lista recebe a sua própria página de teste na sua fila local após o seu próprio deploy bem-sucedido.

### 2.1 Interface (WPF)

- **Controlo:** `CheckBox` com texto «Imprimir página de teste após configurar» (`Main_PrintTestPageLabel` em `Main.pt-BR.xaml`).
- **Posição:** painel direito do `MainWindow`, após o campo do host da impressora e antes do botão **Implantar**.
- **Ligação:** propriedade `PrintTestPage` no `MainViewModel` (duas vias), predefinição `false`.

## 3. Abordagem técnica (seleccionada)

**Orquestrador:** só invoca o envio da página de teste quando `request.PrintTestPage` é verdadeiro.

**Extensão de `IRemotePrinterOperations`** com um método dedicado, por exemplo:

`Task PrintTestPageAsync(string computerName, NetworkCredential credential, string printerQueueName, CancellationToken cancellationToken = default)`

### 3.1 WinRM (`WinRmRemotePrinterOperations`)

- Executar PowerShell remoto que carrega o módulo de gestão de impressão e chama o cmdlet oficial:
  - `Import-Module PrintManagement -ErrorAction Stop`
  - `Print-TestPage -PrinterName '<nome>'`
- O nome da fila deve ser escapado com a mesma regra de aspas simples que `Escape` já aplica a `Add-Printer` (`'` → `''`).

### 3.2 WMI/CIM (`CimRemotePrinterOperations`)

- Ligar ao `root\cimv2` remoto (padrão existente).
- `SELECT * FROM Win32_Printer WHERE Name='<nome>'` com `EscapeWql` já usado noutras queries.
- Se não existir instância: `InvalidOperationException` com mensagem clara.
- Invocar o método WMI `PrintTestPage` na instância encontrada.

### 3.3 Composição (`CompositeRemotePrinterOperations`)

- Mesmo padrão que `AddPrinterAsync`: tentar `_primary`; em falha (excepto cancelamento), tentar `_fallback`.

## 4. Fora de âmbito

- Novo valor em `TargetMachineState` para “sucesso com aviso” (opcional futuro; o MVP usa mensagem no último evento de sucesso).
- Página de teste personalizada ou ficheiro PDF — apenas o comportamento padrão do Windows para a fila.
- `rundll32 printui.dll` — mantém-se fora, por alinhamento com decisões anteriores do projecto.

## 5. Testes

- **Orquestrador:** mock de `IRemotePrinterOperations` — com `PrintTestPage == true`, verificar que `PrintTestPageAsync` é chamado após `AddPrinterAsync` com o nome de fila correcto; com `PrintTestPage == false`, verificar que `PrintTestPageAsync` **nunca** é chamado e o alvo termina em `CompletedSuccess`; com falha no envio e `PrintTestPage == true`, ainda produz `CompletedSuccess` com mensagem de aviso.
- **Composite:** verificar fallback quando o primário lança excepção.
- Testes de integração contra Windows real permanecem opcionais.

## 6. Riscos e pré-requisitos no alvo

- O cmdlet `Print-TestPage` requer o módulo **PrintManagement** (habitual em clientes Windows desktop; em Server Core pode depender de funcionalidades de impressão instaladas). O caminho WMI é o fallback quando WinRM falha de forma global; se **ambos** falharem após configuração bem-sucedida, aplicar a mesma política de “sucesso com aviso” no orquestrador após esgotar o composite (ou seja, capturar a excepção final do `PrintTestPageAsync`).

---

*Revisão interna:* sem placeholders; comportamento de falha da página de teste e cancelamento estão explícitos; alinhado a um único plano de implementação.
