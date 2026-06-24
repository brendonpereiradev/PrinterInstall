# Especificação de desenho — Hosts multilinha no assistente de controle de impressoras

**Data:** 2026-05-29  
**Estado:** Aprovado  
**Relacionado:** `2026-04-17-printer-removal-design.md`, tela principal (`MainWindow`)

## 1. Objetivo

Permitir inserir vários computadores alvo no passo inicial do assistente de remoção/renomeação usando **Enter para nova linha**, alinhado à tela principal de implantação. A ação **Avançar** (botão) é a única forma de sair do passo 0.

## 2. Decisão de produto

| Tecla / ação | Comportamento |
|--------------|---------------|
| **Enter** (no campo de hosts) | Nova linha |
| **Avançar** (botão) | Valida lista, inicia passo de listagem por máquina |
| Colar texto multilinha | Suportado (parser existente) |

Abordagens descartadas neste escopo: lista com chips, DataGrid de hosts, validação linha a linha em tempo real.

## 3. Alterações de UI

### 3.1 TextBox de hosts

- `AcceptsReturn="True"` (paridade com `MainWindow`).
- Remover `PreviewKeyDown` que interceptava Enter para disparar `StartCommand`.
- Manter `TextWrapping="Wrap"` e scroll vertical.

### 3.2 Texto de ajuda

- Manter `Removal_Step0Intro` (um host por linha + propósito do assistente).
- Adicionar `Removal_Step0KeyboardHint`: *"Pressione Enter para adicionar outro host na lista."*
- Exibir a dica abaixo do intro, com estilo secundário (`TextFillColorSecondaryBrush`, `FontSize="12"`).

### 3.3 Botão Avançar

- Manter `IsDefault="True"` para atalho quando o foco **não** está no TextBox.
- Enter dentro do TextBox não deve acionar o botão padrão (comportamento WPF com `AcceptsReturn="True"`).

## 4. Lógica (sem mudança)

- `ComputersText` → `ComputerNameListParser.Parse` → fluxo existente do `RemovalWizardViewModel`.
- Sem alterações em Core ou testes automatizados.

## 5. Arquivos

| Arquivo | Mudança |
|---------|---------|
| `RemovalWizardWindow.xaml` | Multilinha, layout intro + dica, remover `PreviewKeyDown` |
| `RemovalWizardWindow.xaml.cs` | Remover handler `Step0Computers_OnPreviewKeyDown` |
| `RemovalWizard.pt-BR.xaml` | Nova string `Removal_Step0KeyboardHint` |

## 6. Verificação manual

1. Digitar host A, Enter, host B → duas linhas visíveis.
2. Colar lista com várias linhas → parser reconhece todos ao clicar Avançar.
3. Enter no campo não avança o assistente; Avançar sim.
