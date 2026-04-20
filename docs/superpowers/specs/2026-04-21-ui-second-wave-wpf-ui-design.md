# Especificação de desenho — Segunda leva de UI com WPF-UI

**Data:** 2026-04-21  
**Estado:** Rascunho para revisão do usuário (antes do plano de implementação)  
**Contexto:** Aplicação WPF `PrinterInstall.App` (.NET 8), localização pt-BR já implementada (`2026-04-20-ui-ux-pt-BR-design.md`).  
**Relação com specs anteriores:** Esta spec **não** altera idioma, limites da Core nem política de log; **só** define a **camada visual** e a **integração da biblioteca WPF-UI**. O alvo continua ser uma ferramenta **densa e legível** para administrador, **não** um produto consumer “chamativo”.

## 1. Objetivo

Entregar uma **segunda leva de melhorias visuais claramente perceptíveis** nas **três janelas** (login, principal, assistente de remoção), com **linguagem visual coerente**, usando a biblioteca **WPF-UI (Wpf.Ui)** em **tema claro**, **acento azul discreto**, e reforço de **hierarquia** (cabeçalho contextual, botões primário/secundário, agrupamentos mais limpos, **estado vazio** na grade principal).

## 2. Decisões de produto (resumo)

| Tema | Decisão |
|------|---------|
| Âmbito | **Três janelas** na mesma entrega: `LoginWindow`, `MainWindow`, `RemovalWizardWindow`. |
| Foco visual | **Mistura equilibrada:** cabeçalho + acento, tipografia/espaçamento, polimento de componentes (incl. estado vazio). |
| Biblioteca | **WPF-UI (`Wpf.Ui`)**, versão **fixada** no `.csproj` (reprodutibilidade). |
| Tema | **Claro**; evitar tema escuro por defeito como identidade do produto. |
| Cor de acento | **Azul discreto** (baixa saturação); valor exato definido na implementação com contraste legível sobre fundo claro (ex. faixa/cabeçalho e botão primário). |
| Estilo geral | **Admin neutro:** fundos claros, bordas subtis, **sem** animações ou decoração desnecessária; densidade preferível a “cards” altos estilo consumer. |
| Localização | **Preservar** chaves `DynamicResource` / `UiStrings`; novos textos (estado vazio) em **pt-BR** nos mesmos mecanismos já usados. |
| Core | **Sem** referência a WPF-UI ou a recursos de UI. |
| Layout estrutural | Manter o **layout A** da janela principal (colunas + log em baixo) salvo **ajustes** impostos por controlos ou margens do tema. |

## 3. Abordagens consideradas e seleção

1. **WPF “puro”** — Só `ResourceDictionary` e estilos manuais. **Prós:** zero dependências. **Contras:** mais trabalho para o mesmo impacto visual.  
2. **Biblioteca de UI pronta (WPF-UI)** — Temas e controlos modernos, tema claro configurável. **Prós:** mudança visível rápida e coerente. **Contras:** dependência externa; exige **afinação** para não parecer app consumer.  
3. **Outra biblioteca (ex. Material)** — **Descartada** para este produto (desalinhada com ferramenta admin Windows).

**Selecionada:** **2 (WPF-UI)**, com configuração explícita **claro + azul discreto + densidade admin**.

## 4. Arquitetura e integração técnica

### 4.1 Pacote

- `PackageReference` apenas em **`PrinterInstall.App`**.  
- Versão **pinada** (sem intervalos largos) e nota no plano de implementação para upgrades controlados.

### 4.2 Recursos globais (`App.xaml` / arranque)

- Fundir dicionários do **WPF-UI** (tema **Light**).  
- Ordem sugerida de `MergedDictionaries`: **tema WPF-UI** → **overrides** (acento azul discreto, se necessário arquivo dedicado) → **`AdminToolTheme.xaml`** apenas para o que **não** conflitar → **dicionários de strings pt-BR** (`Strings/*.xaml`), para textos continuarem aplicáveis.  
- Configurar **cor de acento** pela API suportada pela versão do WPF-UI (documentar no plano se for **XAML**, **code-behind** no `App_OnStartup`, ou **ThemeWatcher** / gestor de tema equivalente).  
- **Rever** `Themes/AdminToolTheme.xaml`: eliminar estilos que **dupliquem** ou **sobreponham** mal os do WPF-UI (especialmente `Button`).

### 4.3 Janelas e controlos

- Escolher **um** padrão para o shell da janela: **`FluentWindow`** (ou equivalente na versão do pacote) **ou** `Window` com recursos fundidos — critério: **menor atrito** com `Owner`, `ShowDialog`, e código existente nas três vistas. A decisão final fica **registada no plano de implementação** e aplica-se às três janelas.  
- Usar variantes do WPF-UI onde agregarem valor: **botão primário** (Implantar, Entrar, Executar remoção), **secundário/outline** (ex. Remover impressoras…), **Card** ou agrupamentos equivalentes para secções (computadores, parâmetros, status).  
- **DataGrid:** estilização alinhada ao tema (cabeçalhos e linhas legíveis; zebra **opcional** e muito sutil).  
- **Login:** campos e botão alinhados ao tema; erro de validação local em pt-BR; mensagens LDAP do Core podem permanecer em inglês (política já existente).

### 4.4 Estado vazio (`MainWindow`)

- Quando não existirem linhas em `Targets`, mostrar **mensagem orientativa** em pt-BR (novo recurso em `Main.pt-BR.xaml` ou `UiStrings`, conforme o padrão do projeto), **centrada** na área da grade ou sobreposta de forma clara, em vez de grelha completamente vazia.  
- Implementação via propriedade no **ViewModel** (ex. `ShowStatusEmptyHint`) ou conversor, **sem** alterar a lógica do orquestrador.

### 4.5 Assistente de remoção

- Aplicar o mesmo cabeçalho e hierarquia de botões.  
- Revalidar o padrão atual de **`TabItem` com `Visibility=Collapsed`**: após o tema, confirmar que o fluxo por passos **não** fica visível nem quebrado; ajustar estilo pontualmente se necessário.

## 5. Riscos e mitigação

| Risco | Mitigação |
|-------|-----------|
| Conflito de estilos entre tema antigo e WPF-UI | Ordem de dicionários + remover estilos redundantes; estilos explícitos só onde preciso. |
| Aparência “demasiado Fluent/consumer” | Tema **claro**, acento **azul discreto**, evitar densidade baixa excessiva; priorizar controle admin. |
| Mudança de API entre versões do Wpf.Ui | Versão fixada; upgrade documentado. |
| Regressão em diálogo / Owner | Testar `RemovalWizardWindow` com `Owner` após mudança de tipo de janela. |

## 6. Verificação

- `dotnet build` e `dotnet test` na solução **sem regressões**.  
- **Smoke manual:** login; principal (validação, lista vazia com dica, pelo menos uma linha na grade após acção); assistente (passos, grelha, revisão).  
- **Critério de sucesso:** melhoria visual **óbvia** e **coerente** nas três janelas; **pt-BR** mantido; **Core** sem dependência de UI.

## 7. Fora de âmbito

- Novas funcionalidades de negócio (só UI).  
- Internacionalização adicional.  
- Redesenho completo do fluxo do assistente (passos e ViewModels mantêm-se).  

## 8. Próximos passos

1. Revisão deste documento pelo usuário.  
2. Skill **writing-plans** para plano de implementação (tarefas, versão exata do pacote, ordem de migração das três janelas).  
3. **Não** iniciar implementação até o plano estar acordado.

## 9. Referências no repositório

- Spec anterior: `docs/superpowers/specs/2026-04-20-ui-ux-pt-BR-design.md`  
- Views: `src/PrinterInstall.App/Views/*.xaml`  
- Tema atual: `src/PrinterInstall.App/Themes/AdminToolTheme.xaml`  
