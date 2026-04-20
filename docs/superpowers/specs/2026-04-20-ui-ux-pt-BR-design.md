# Especificação de desenho — UI/UX e interface em português do Brasil

**Data:** 2026-04-20  
**Estado:** Rascunho para revisão do usuário (antes do plano de implementação)  
**Contexto:** Aplicação WPF `PrinterInstall.App` e biblioteca `PrinterInstall.Core` (.NET 8).

## 1. Objetivo

Definir como o produto passa a oferecer **interface e textos voltados ao usuário em português do Brasil (pt-BR)**, com **melhorias de UI/UX** centradas em **refino visual** e **organização da informação**, mantendo um aspecto **neutro de ferramenta de administração** (densidade e clareza). O **log técnico** pode permanecer **misto**: pt-BR onde o time controla a mensagem; inglês ou texto bruto onde reflete saída remota ou detalhe de baixo nível.

## 2. Decisões de produto (resumo)

| Tema | Decisão |
|------|---------|
| Idioma da UI | **Fixo em pt-BR** (sem troca de idioma na aplicação nem seguir cultura do SO para a UI). |
| Localização técnica | **Híbrida:** dicionários de recursos em **XAML** para textos declarados em views; **`.resx`** (ou serviço fino equivalente no App) para strings usadas em **ViewModels** e mensagens amigáveis. |
| `PrinterInstall.Core` | **Sem** arquivos de idioma; parsing e contratos numéricos permanecem **invariantes** (`CultureInfo.InvariantCulture` onde já aplicável). |
| Grade “Estado / Mensagem” | **Misto:** resumos e estados orientados ao usuário em pt-BR; detalhe técnico pode permanecer em EN ou bruto. |
| Painel de log | **Transcript técnico**; sem obrigação de traduzir cada linha. Opcional: marcos curtos em pt-BR (“Iniciando…”, “Concluído”) se a implementação quiser orientação sem ocultar o bruto. |
| Foco UX | **A + B:** melhorar **aparência** (alinhamento, tipografia, espaçamento, hierarquia) e **estrutura da informação** (o que é primário vs. secundário). |
| Estilo visual | **Neutro / admin:** prioridade a legibilidade e densidade; **não** adotar como meta o visual “Fluent / Windows 11 consumer”. |
| Tela principal (layout) | **Opção A:** coluna esquerda com lista de máquinas + grade de status; coluna direita com parâmetros e ações; **log largo** na zona inferior — evolução do layout atual com ganhos de refinamento, não mudança estrutural radical. |
| Assistente de remoção | **Fora do escopo de layout neste documento:** aplicar os **mesmos princípios** de strings pt-BR e estilo admin; **wireframes específicos** podem ser tratados numa iteração ou addendum após validação da shell principal. |

## 3. Abordagens consideradas e seleção

### 3.1 Estratégia de localização

1. **Dicionários XAML apenas** — Excelente para `MainWindow` e XAML; ViewModels e o Core continuariam com literais ou um padrão ad-hoc para mensagens.  
2. **`.resx` / satélites apenas** — Padrão .NET forte em C#; XAML mais verboso sem extensões ou code-behind.  
3. **Híbrido (XAML + `.resx` / serviço no App)** — Separação clara: views consomem recursos XAML; código no App centraliza mensagens amigáveis. Core permanece livre de UI culture.  
   **Selecionada: 3 (híbrido).**

### 3.2 Organização da tela principal (brainstorm visual)

1. **A** — Coluna de ação à direita + log largo em baixo (próximo do atual).  
2. **B** — Barra superior de ações + log compacto.  
3. **C** — Abas “Implantação” \| “Log”.  
   **Selecionada: A.**

## 4. Arquitetura e limites

### 4.1 `PrinterInstall.App`

- **Views (XAML):** `DynamicResource` / `StaticResource` para textos de interface; arquivos de dicionário agrupados por área (ex.: `Strings/Main.pt-BR.xaml`) fundidos em `App.xaml`. Chaves **estáveis** e prefixadas (`Main_WindowTitle`, `Main_DeployButton`, …).
- **ViewModels:** obtêm strings amigáveis via propriedades geradas a partir de `.resx` ou via interface `IUiStrings` implementada com esse recurso — evitar literais espalhados.
- **Mapeamento Core → UI:** ViewModels traduzem **códigos ou resultados estruturados** do Core para texto de colunas; não exigir que o Core devolva frases finais em pt-BR.

### 4.2 `PrinterInstall.Core`

- Sem referência a recursos de UI.
- Continuação de invariant culture em conversões numéricas e protocolos remotos.
- Mensagens técnicas expostas para diagnóstico podem permanecer em inglês ou como recebidas do alvo.

## 5. UX e hierarquia (tela principal — opção A)

- **Primário:** entrada de alvos, parâmetros de implantação e ação “Implantar”.
- **Secundário:** grade de progresso por máquina visível sem competir com o log em altura desnecessária; `GroupBox` ou separadores só quando agrupam um conceito útil.
- **Log:** zona inferior ampla; tipografia monoespaçada mantém-se para leitura de saída técnica.
- **Refinos:** alinhamento consistente de labels e campos, espaçamento vertical uniforme, um único destaque cromático para botão primário (opcional, discreto).

## 6. Política de mensagens (grade vs. log)

| Origem | Política sugerida |
|--------|-------------------|
| Estado agregado (ex.: “Em andamento”, “Concluído”, “Falhou”) | pt-BR no App. |
| Mensagem de erro conhecida / catalogada | pt-BR mapeada no ViewModel a partir de código ou tipo de falha. |
| Saída PowerShell/WMI ou exceção textual | Exibir como recebida (EN/bruto) no log; na grade, opcionalmente truncar ou prefixar com resumo pt-BR. |
| Cabeçalhos de `DataGrid` e rótulos | pt-BR via dicionário XAML. |

## 7. Testes

- Testes de Core: asserções sobre **códigos ou substrings invariantes** nas mensagens técnicas, não sobre texto final de UI pt-BR.
- Onde fizer sentido no App: testes unitários leves do mapeamento “resultado estruturado → string pt-BR” para poucos casos representativos.
- Evitar dependência frágil de frases completas geradas por recursos em testes de integração de baixo nível.

## 8. Fora de âmbito (explícito)

- Suporte multi-idioma ou detecção automática de cultura do Windows para a UI.
- Redesenho completo do assistente de remoção (apenas alinhamento de princípios até addendum).
- Tradução obrigatória de toda a saída remota no painel de log.

## 9. Próximos passos

1. Revisão deste documento pelo usuário.  
2. Invocar o skill **writing-plans** para produzir o plano de implementação (tarefas por projeto, arquivos afetados, ordem sugerida).  
3. **Não** iniciar implementação até o plano estar acordado.

## 10. Referências no repositório

- Views atuais: `src/PrinterInstall.App/Views/MainWindow.xaml`, `RemovalWizardWindow.xaml`, `LoginWindow.xaml`.  
- Especificações relacionadas: `2026-04-17-printer-removal-design.md` (fluxo do assistente), `2026-04-16-printer-remote-config-design.md` (contexto de implantação).
