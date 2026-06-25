# Preferências de tamanho de etiqueta Gainscha no deploy remoto

**Data:** 2026-06-25  
**Status:** Para revisão do proprietário do produto  
**Relacionado:** `2026-04-16-printer-remote-config-design.md`, `2026-04-29-deploy-cancel-rollback-design.md`, `2026-04-17-printer-driver-install-design.md`

## Objetivo

Automatizar a configuração das **preferências de tamanho de papel de etiqueta** ao instalar uma **nova** fila Gainscha (local ou remota). O operador escolhe o preset do setor na UI; o app aplica remotamente a aba **Configuração de página** das preferências da impressora, deixando **apenas** um formulário `USER` com as dimensões corretas.

## Contexto

Hoje o `PrinterDeploymentOrchestrator` conclui o deploy Gainscha após:

1. Validar/instalar driver (`Gainscha GA-2408T`)
2. Criar porta TCP/IP (9100/RAW)
3. Adicionar fila (`AddPrinterAsync`)
4. (Opcional) Enviar página de teste

Não há configuração de preferências do driver Seagull/TSC. Manualmente, o operador abre **Propriedades da impressora → Preferências → Configuração de página → Papel de etiquetas → Nome**, escolhe ou cria um tamanho `USER (largura mm x altura mm)` e remove outros perfis. Essa etapa passa a ser automatizada **somente para filas Gainscha novas** nesta execução.

## Decisões registradas

| Decisão | Escolha |
|---------|---------|
| Onde configurar | Preferências → aba **Configuração de página** → **Papel de etiquetas** |
| Quantos presets por deploy | **Um** — conforme setor |
| Nome visível no driver | Sempre `USER (largura mm x altura mm)` — rótulos Pulseira/Matrix/Paciente/Dupla são só na UI do PrinterInstall |
| Presets e dimensões | Pulseira 25×270, Matrix 50×30, Paciente 89×36, Dupla 45×13 (mm) |
| UI | Dropdown **condicional** — visível apenas quando marca = **Gainscha** |
| Validação UI | Seleção **obrigatória** antes do Deploy para linhas Gainscha |
| Fila já existente | `SkippedAlreadyExists` — **sem** reconfigurar preferências |
| Falha na preferência | **Rollback** automático de fila + porta criadas nesta execução |
| Journal de rollback | `RecordQueueCreated` **somente após** preferência aplicada com sucesso |
| Epson/Lexmark | Sem alteração |
| Abordagem técnica | Helper C# Win32 Spooler API (`DocumentProperties` + `SetPrinter`); fallback registry Seagull se spike exigir |

## Presets (catálogo fixo)

| Rótulo UI | String no driver | Largura (mm) | Altura (mm) |
|-----------|------------------|--------------|-------------|
| Pulseira | `USER (25,0 mm x 270,0 mm)` | 25 | 270 |
| Matrix | `USER (50,0 mm x 30,0 mm)` | 50 | 30 |
| Paciente | `USER (89,0 mm x 36,0 mm)` | 89 | 36 |
| Dupla | `USER (45,0 mm x 13,0 mm)` | 45 | 13 |

Formato numérico com **vírgula** decimal, alinhado ao Windows pt-BR e ao driver Seagull observado em produção.

## Abordagens consideradas

### A) Helper nativo C# com Win32 Spooler API *(selecionada)*

Executar no alvo (local in-process ou remoto via `ElevatedRemoteProcessRunner`) um helper que usa `DocumentProperties` + `SetPrinter` para ler/gravar DEVMODE da fila, incluindo dados privados do driver Seagull.

- **Prós:** padrão Windows; encaixa no pipeline SMB + schtasks SYSTEM existente; testável com abstração injectável.
- **Contras:** stocks Seagull podem exigir dados privados além do DEVMODE público — requer spike em máquina real.

### B) Manipulação direta do Registry (`PrinterDriverData`)

Escrever/remover chaves em `HKLM\...\Print\Printers\<nome>\PrinterDriverData`.

- **Prós:** controle fino sobre lista de stocks.
- **Contras:** formato opaco; frágil entre versões do driver (`2021.1.4_GN`).

### C) Automação de UI

Descartada — inadequada para deploy remoto headless.

**Fallback documentado:** se o spike provar que a abordagem A não remove/cria stocks USER, usar templates de registry capturados de configuração manual correta, versionados no repositório, aplicados pelo mesmo helper remoto.

## Arquitetura

### UI (App)

- `PrinterFormRowViewModel`: propriedade `GainschaLabelPreset?` + visibilidade condicionada a `Brand == Gainscha`.
- `MainWindow.xaml`: dropdown **"Tamanho de etiqueta"** (Pulseira, Matrix, Paciente, Dupla) visível só para Gainscha.
- `MainViewModel`: validação antes do Deploy — linha Gainscha sem preset → bloquear com mensagem pt-BR.
- Strings em `UiStrings.resx` / `Main.pt-BR.xaml`.

### Core

| Componente | Responsabilidade |
|------------|------------------|
| `GainschaLabelPreset` | Enum dos quatro presets |
| `GainschaLabelPresetCatalog` | Mapeamento preset → dimensões + string `USER (...)` |
| `IGainschaLabelPreferenceConfigurator` | Contrato: aplicar preset numa fila pelo nome |
| `GainschaLabelPreferenceConfigurator` | Implementação Win32 (P/Invoke spooler) |
| `IRemotePrinterOperations.ConfigureGainschaLabelPresetAsync` | Novo método no contrato remoto |
| `LocalPrinterOperations` / `CimRemotePrinterOperations` | Delegar ao configurator (local ou helper remoto elevado) |
| `PrinterQueueDefinition` | Campo opcional `GainschaLabelPreset?` |
| `PrinterDeploymentOrchestrator` | Inserir passo pós-`AddPrinter`; adiar journal; rollback em falha |

### Fluxo no orquestrador (Gainscha, fila nova)

```text
CreateTcpPrinterPortAsync
  → RecordPortCreated
  → AddPrinterAsync
  → delay spooler (~2 s)
  → ConfigureGainschaLabelPresetAsync
       ├─ sucesso → RecordQueueCreated → (opcional) PrintTestPageAsync → CompletedSuccess
       └─ falha   → remover fila + porta (rollback) → Error
```

**Passo de configuração no alvo:**

1. Remover **todos** os perfis de papel de etiqueta existentes na fila.
2. Criar **um** formulário USER com largura × altura do preset.
3. Definir esse USER como seleção ativa em Configuração de página (preferências padrão da fila).
4. Validar que restou exatamente um stock USER com dimensões corretas.

### Rollback em falha

Reutilizar `DeploymentRollbackRunner` / `PrinterControlOrchestrator` (política de porta órfã existente):

1. **Não** chamar `RecordQueueCreated`.
2. Remover fila recém-criada e porta se órfã.
3. Estado terminal **`Error`** com mensagem explícita.
4. **Não** enviar página de teste.

Mensagens de progresso sugeridas:

- `"Configurando tamanho de etiqueta..."`
- `"Preferência de etiqueta aplicada."`
- `"Falha na preferência de etiqueta — revertendo fila e porta..."`
- `"Revertido — preferência de etiqueta não aplicada."`

### Execução remota

- **Local:** configurator in-process (mesmo padrão de `LocalPrinterOperations`).
- **Remoto:** helper staged em `ADMIN$\Temp\PrinterInstall\<guid>\` e executado via `ElevatedRemoteProcessRunner` quando `RequiresElevatedExecution`; caso contrário, processo remoto via WMI como operações existentes.

## Fora de âmbito

- Reconfigurar filas Gainscha já existentes antes do deploy.
- Aplicar preferências a Epson ou Lexmark.
- Instalar ou remover drivers durante rollback de preferência.
- Múltiplos presets simultâneos numa mesma fila.
- Persistir último preset escolhido entre sessões da app.

## Testes

### Unitários (Core + App)

| Teste | Valida |
|-------|--------|
| `GainschaLabelPresetCatalog_*` | Mapeamento preset → dimensões e string USER |
| `PrinterDeploymentOrchestrator_Gainscha_RecordsQueueOnlyAfterLabelConfig` | Journal só após preferência OK |
| `PrinterDeploymentOrchestrator_Gainscha_LabelConfigFailure_TriggersRollback` | Rollback em falha |
| `PrinterDeploymentOrchestrator_Gainscha_SkippedAlreadyExists_NoLabelConfig` | Fila existente ignorada |
| `MainViewModel_Validation_GainschaRequiresLabelPreset` | Deploy bloqueado sem preset |
| `GainschaLabelPreferenceConfigurator_*` | Helper Win32 com spooler mockado |

### Spike obrigatório (pré-merge)

Em máquina com driver `Gainscha GA-2408T`:

1. Capturar referência manual de um preset (ex.: Paciente).
2. Confirmar helper reproduz só `USER (89,0 mm x 36,0 mm)` em Preferências.
3. Documentar no plano se fallback registry foi necessário.

### Verificação manual (aceitação)

1. Deploy remoto — preset Paciente → preferência correta no Windows.
2. Deploy remoto — preset Pulseira → dimensões 25×270.
3. Falha simulada → fila e porta removidas.
4. Deploy local — mesmo comportamento.
5. Linha Epson na mesma execução — sem dropdown nem passo extra.
6. Cancelamento/reversão existente — sem regressão.

## Critérios de sucesso

- Operador seleciona Gainscha + preset → nova fila com preferência correta.
- Falha na preferência → rollback automático e log claro.
- Validação UI impede deploy Gainscha sem preset.
- Sem regressão em marcas não-Gainscha nem no journal de cancelamento.

## Próximo passo

Após aprovação desta spec, criar plano de implementação em `docs/superpowers/plans/` com o skill *writing-plans*.
