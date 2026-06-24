# Teste de impressora directo por IP (sem configuração local)

**Data:** 2026-06-24  
**Status:** Aprovado — implementado em 2026-06-24  
**Contexto:** O produto envia páginas de teste **após** criar fila no Windows (`PrintTestPage` via spooler/WMI). O operador pediu uma acção **independente** do deploy que teste conectividade e envie uma página de teste **directamente ao IP da impressora**, sem instalar driver nem criar fila na máquina onde corre o Printer Install.

## Objetivo

Permitir que o operador, a partir da **máquina onde o Printer Install está aberto**, teste uma impressora de rede informando **marca + IP/hostname**, com confirmação em **duas fases** (conectividade TCP, depois envio do job RAW), num **diálogo dedicado** separado do fluxo de deploy.

## Restrições confirmadas

| Item | Decisão |
|------|---------|
| Acção | Independente do deploy (não integrada ao orquestrador) |
| Origem da conexão | Máquina local onde corre a app |
| Porta / protocolo | Fixos: **9100 + RAW** (JetDirect) |
| Driver Windows | **Não utilizado** — payload RAW embutido por marca |
| Marca | **Obrigatória** (Epson, Lexmark, Gainscha) |
| Credenciais de domínio | **Não necessárias** para esta acção |
| Sucesso | Fase 1 (TCP) + Fase 2 (envio de bytes); mensagens distintas por fase |
| Garantia de papel | **Não** — app confirma envio na rede; operador verifica fisicamente |

## Abordagens consideradas

| # | Abordagem | Decisão |
|---|-----------|---------|
| 1 | TCP RAW + payload PCL5/ESC-POS embutido | **Adotada** |
| 2 | TCP RAW + payload por marca (sem driver) | **Adotada** (marca obrigatória escolhe linguagem) |
| 3 | Fila temporária local + `PrintTestPage` + remoção | **Descartada** (configura a máquina; exige driver) |
| 4 | IPP/LPR configurável | **Descartada** (fora de scope v1; operador confirmou 9100 RAW fixo) |

## Arquitectura

```text
MainWindow  →  botão "Testar impressora…"
    └── PrinterNetworkTestWindow (diálogo modal)
            └── PrinterNetworkTestViewModel
                    └── IDirectRawPrinterTestService  (PrinterInstall.Core)
                            ├── Fase 1: TcpClient.ConnectAsync(host, 9100)
                            └── Fase 2: NetworkStream.WriteAsync(payload[brand])
```

**Princípio:** serviço isolado em `PrinterInstall.Core`; **sem** alteração a `IRemotePrinterOperations`, orquestradores ou remoting WMI.

### Componentes novos

| Componente | Responsabilidade |
|------------|------------------|
| `IDirectRawPrinterTestService` | Orquestra fases 1 e 2; devolve `DirectRawPrinterTestResult` |
| `DirectRawPrinterTestService` | Implementação com `TcpClient` injectável para testes |
| `DirectRawPrinterTestPageBuilder` | Gera bytes PCL5 (Epson/Lexmark) ou ESC/POS (Gainscha) |
| `DirectRawPrinterTestResult` | `Success`, `FailedPhase` (`None` / `Connectivity` / `Send`), `Message` |
| `PrinterNetworkTestViewModel` | Validação, comando Testar, estados de UI, cancelamento |
| `PrinterNetworkTestWindow` | Diálogo modal com marca, IP, resultado |

### Payload por marca

| Marca | Linguagem | Notas |
|-------|-----------|-------|
| Epson | PCL5 | Página de teste com texto fixo (IP, data/hora, identificação da app) |
| Lexmark | PCL5 | Mesmo payload PCL5 ou variante mínima se necessário |
| Gainscha | ESC/POS | Ticket de teste adaptado a impressora térmica |

Não se usa o nome do driver Windows (`EPSON Universal Print Driver`, etc.) — apenas a **linguagem de impressão** compatível com o hardware.

## Comportamento funcional

### Entrada na UI

1. Botão **"Testar impressora…"** na `MainWindow`, visível após login.
2. Abre diálogo modal dedicado; **não** depende de alvos ou filas preenchidas no deploy.

### Diálogo

| Campo | Obrigatório | Validação |
|-------|-------------|-----------|
| Marca (`PrinterBrand`) | Sim | Dropdown: Epson, Lexmark, Gainscha |
| IP ou hostname | Sim | Não vazio; mesma validação básica de formato do deploy |

- **Testar:** desabilitado até marca + IP válidos; desabilitado durante execução.
- **Fechar:** fecha o diálogo (cancela teste em curso se aplicável).
- Texto informativo: teste confirma envio na rede; operador deve verificar impressão física.

### Fluxo de execução

1. **Validação local** — marca ou IP inválidos → mensagem inline; sem abrir socket.
2. **Fase 1 — Conectividade** (~5 s timeout)
   - `TcpClient.ConnectAsync(host, 9100)`
   - Sucesso → avança para fase 2
   - Falha → resultado final: `"Sem conectividade em {host}:9100 — {motivo}"`, `FailedPhase = Connectivity`
3. **Fase 2 — Envio** (~10 s timeout)
   - Obtém payload via `DirectRawPrinterTestPageBuilder.ForBrand(brand, host)`
   - Escreve bytes no `NetworkStream`, flush, fecha stream e cliente
   - Sucesso → `"Teste enviado com sucesso. Verifique se a impressora imprimiu a página."`
   - Falha → `"Conectou, mas falhou ao enviar — {motivo}"`, `FailedPhase = Send`
4. **Cancelamento** — `CancellationToken` cancela connect/write; mensagem `"Teste cancelado."`

Sem retry automático na v1; operador clica **Testar** novamente.

### Estados de progresso na UI

- *A testar conectividade…*
- *A enviar página de teste…*
- Resultado final (sucesso ou erro com fase indicada)

## Tratamento de erros

| Situação | Comportamento |
|----------|---------------|
| DNS não resolve | Fase 1, mensagem com detalhe |
| Timeout de conexão | Fase 1 |
| Conexão recusada (porta fechada) | Fase 1 |
| Conectou, write falhou | Fase 2 |
| Cancelado pelo operador | Mensagem de cancelamento; sem excepção não tratada na UI |
| Excepção inesperada | Mensagem amigável com detalhe técnico resumido |

## Internacionalização

- Strings em `UiStrings.resx` + entradas em ficheiro XAML pt-BR (ex.: `Strings/PrinterNetworkTest.pt-BR.xaml`).
- Chaves sugeridas: título do diálogo, labels de marca/IP, botões, mensagens de fase e resultado, aviso de limitação.

## Fora de âmbito (v1)

- Porta ou protocolo configuráveis (LPR, IPP)
- Teste remoto a partir de PCs alvo
- Histórico de testes ou fila de jobs
- Retry automático
- Selecção de modelo ou driver Windows
- Garantia programática de que saiu papel
- Integração com checkbox "Imprimir página de teste" do deploy

## Testes

### Unitários (`PrinterInstall.Core.Tests`)

- `DirectRawPrinterTestPageBuilder_ForBrand_ReturnsNonEmptyPayload`
- `DirectRawPrinterTestPageBuilder_Gainscha_DiffersFromPcl`
- `DirectRawPrinterTestService_WhenConnectFails_ReturnsConnectivityPhase`
- `DirectRawPrinterTestService_WhenConnectSucceedsButWriteFails_ReturnsSendPhase`
- `DirectRawPrinterTestService_WhenBothSucceed_ReturnsSuccess`
- `DirectRawPrinterTestService_WhenCancelled_PropagatesCancellation`

Implementação injecta abstracção de socket/factory para evitar rede real nos testes.

### Checklist manual

1. Epson ou Lexmark na rede → página PCL legível
2. Gainscha → ticket ESC/POS
3. IP inexistente / timeout → erro fase 1
4. IP válido, porta 9100 fechada → erro fase 1
5. Cancelar durante teste → cancelamento limpo

## Ficheiros previstos (implementação)

| Ficheiro | Acção |
|----------|-------|
| `src/PrinterInstall.Core/Network/IDirectRawPrinterTestService.cs` | Criar |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestService.cs` | Criar |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestPageBuilder.cs` | Criar |
| `src/PrinterInstall.Core/Network/DirectRawPrinterTestResult.cs` | Criar |
| `src/PrinterInstall.App/ViewModels/PrinterNetworkTestViewModel.cs` | Criar |
| `src/PrinterInstall.App/Views/PrinterNetworkTestWindow.xaml` (+ code-behind) | Criar |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Botão de entrada |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | Comando abrir diálogo |
| `src/PrinterInstall.App/App.xaml.cs` | Registo DI do serviço |
| `src/PrinterInstall.App/Resources/UiStrings.resx` | Strings |
| `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestServiceTests.cs` | Criar |
| `tests/PrinterInstall.Core.Tests/Network/DirectRawPrinterTestPageBuilderTests.cs` | Criar |

## Critérios de aceitação

1. Operador abre diálogo, escolhe marca, informa IP, clica Testar — sem configurar impressora no Windows.
2. UI mostra progresso das duas fases e resultado distinto para falha de conectividade vs falha de envio.
3. Epson/Lexmark recebem payload PCL5; Gainscha recebe ESC/POS.
4. Acção não invoca remoting, credenciais de domínio nem orquestrador de deploy.
5. Testes unitários cobrem fases de sucesso/falha/cancelamento sem rede real.
