# UX de deploy na máquina de execução

**Data:** 2026-05-29  
**Status:** Implementado  
**Relacionado:** `2026-05-29-local-machine-routing-design.md` (backend local já implementado)

## Objetivo

Tornar óbvio e simples configurar impressoras na **máquina onde o Printer Install está a correr** — incluindo instalação automática de drivers faltantes e envio opcional de páginas de teste — sem duplicar orquestração nem criar um fluxo técnico separado.

**«Local»** neste documento significa sempre o PC de execução da aplicação (tipicamente membro do domínio), **não** PCs fora do domínio ou em workgroup.

## Contexto

O roteamento técnico (`RoutingRemotePrinterOperations` → `LocalPrinterOperations`) já cobre WMI local, instalação de driver via `%TEMP%` + `install.ps1`, e `PrintTestPageAsync` com fallbacks. O operador ainda precisa **incluir manualmente** o hostname ou IP deste PC na lista de alvos — o que gera fricção e falta de descoberta.

## Decisões registadas

| Decisão | Escolha |
|---------|---------|
| Abordagem | Fluxo único de deploy + atalho UX (não pipeline separado) |
| Cenários de uso | Só este PC **e** misto (este PC + remotos do domínio) |
| Login LDAP | **Sempre obrigatório**, inclusive quando todos os alvos são locais |
| Auto-inclusão silenciosa | Não — alvo local entra via atalho ou digitação manual |
| Novos estados de progresso | Não (`Local`, `ContactingLocal`, etc.) |
| Privilégios | Operador com perfil administrador na máquina de execução (requisito existente para WMI/`pnputil`) |

## Fora de âmbito

- Pular ou relaxar login LDAP para deploy local
- Wizard ou ecrã dedicado só para «este PC»
- Alterações em `PrinterDeploymentOrchestrator`, `IRemotePrinterOperations` ou contratos de progresso
- Botão equivalente no Removal Wizard (pode ser follow-up)
- Testes de integração WMI real contra hardware

## Arquitectura

```text
MainWindow (fluxo único, inalterado na orquestração)
    ├── Campo multilinha de computadores
    │       └── Botão «Adicionar este PC»  ← NOVO
    ├── Definições de impressora (inalteradas)
    ├── ☑ Imprimir página de teste (inalterado)
    └── Deploy → PrinterDeploymentOrchestrator
            └── IRemotePrinterOperations
                    └── RoutingRemotePrinterOperations (existente)
                            ├── máquina de execução → LocalPrinterOperations
                            └── outro PC do domínio  → CimRemotePrinterOperations
```

Backend **sem alterações funcionais**. Trabalho limitado à camada App (ViewModel, XAML, strings, testes unitários leves).

## Componentes

### `LocalMachineIdentity` — extensão mínima

Adicionar método público para a UI obter o identificador preferido deste PC:

```csharp
public string GetPrimaryLocalName() => Environment.MachineName;
```

`IsLocalMachine(string)` permanece inalterado para roteamento e hint na grelha.

### `MainViewModel` — `AddThisComputerCommand`

Comportamento:

1. Obter `name = _localMachineIdentity.GetPrimaryLocalName()`
2. Parsear `ComputersText` com `ComputerNameListParser.Parse`
3. Se `name` já estiver na lista (comparação case-insensitive, alinhada a `IsLocalMachine` ou equivalente simples por nome exacto do hostname curto), **não alterar** o texto
4. Caso contrário, **append**:
   - Se `ComputersText` estiver vazio → definir `name`
   - Senão → adicionar nova linha + `name`

Injeccionar `LocalMachineIdentity` no construtor de `MainViewModel` (registo DI já existe em `App.xaml.cs`).

### `MainWindow.xaml` — botão

Colocar botão **«Adicionar este PC»** no `DockPanel` do campo de computadores, abaixo ou ao lado do label `Main_ComputersLabel` (layout horizontal: label à esquerda, botão à direita no topo do painel).

### `TargetRowViewModel` — hint opcional

Propriedade calculada ou atribuída no `DeployAsync` antes do progresso:

- `IsLocalMachine` (`bool`)
- `ComputerNameDisplay` (`string`): quando `IsLocalMachine`, sufixo discreto via recurso (ex.: `"HOSTNAME (este PC)"`); caso contrário, `ComputerName` puro

A coluna **Computador** da grelha passa a bindar `ComputerNameDisplay` em vez de `ComputerName` (ou usa converter; preferir propriedade no ViewModel).

### Strings (`Main.pt-BR.xaml`)

| Chave | Texto sugerido |
|-------|----------------|
| `Main_AddThisComputer` | Adicionar este PC |
| `Main_LocalComputerSuffix` | (este PC) |

## Fluxos do operador

### Só este PC

1. Login LDAP
2. **Adicionar este PC** (ou digitar hostname/IP manualmente)
3. Preencher impressora(s)
4. Opcionalmente marcar página de teste
5. **Implantar**

### Misto (este PC + remotos)

Mesma tela: lista contém hostname local (via atalho ou manual) e outros hosts do domínio. Uma credencial LDAP, uma operação; cada alvo roteia automaticamente.

## Comportamento herado do backend (sem mudanças)

| Etapa | Caminho local |
|-------|---------------|
| Driver faltante | `TryInstallMissingDriverAsync` → `InstallPrinterDriverAsync` local |
| Criar porta / fila | WMI `root\cimv2` local |
| Página de teste | `PrintTestPageAsync` com WMI + fallbacks `printui` / `Print-TestPage` |
| Falha de test page com fila OK | `CompletedSuccess` com aviso (não reverte fila) |
| Driver sem pacote embutido | Erro / `AbortedDriverMissing` como nos remotos |

## Erros e mensagens

- Falhas WMI/`pnputil` locais propagam para a grelha como nos remotos
- Código WMI 5 em test page: mensagem existente sobre executar como administrador (operador confirmou perfil admin; nota de UAC elevado permanece na documentação)
- Atalho **não** valida conectividade — validação ocorre no deploy como hoje

## Testes

| Ficheiro | Casos |
|----------|-------|
| `tests/PrinterInstall.App.Tests/ViewModels/MainViewModelAddThisComputerTests.cs` (novo) | Append hostname; lista vazia; não duplica case-insensitive; preserva entradas existentes |
| `LocalMachineIdentityTests` | Adicionar teste para `GetPrimaryLocalName()` retorna `Environment.MachineName` |
| Suíte existente | `dotnet test` sem regressões |

Testes do ViewModel podem usar `LocalMachineIdentity` real (determinístico para hostname) ou wrapper injectável se necessário para isolar append logic.

## Documentação

Actualizar `docs/conexao-remota.md`:

- Secção **Fluxo recomendado — configurar este PC**
- Descrição do botão «Adicionar este PC»
- Reiterar que LDAP continua obrigatório e que admin local na máquina de execução é necessário para driver e test page

## Critérios de aceitação

1. Botão «Adicionar este PC» insere `Environment.MachineName` na lista sem duplicar (case-insensitive)
2. Deploy com **apenas** este PC na lista: driver faltante instala, fila cria, test page funciona quando marcado
3. Deploy misto (local + remoto): ambos concluem na mesma operação
4. Grelha mostra sufixo «(este PC)» nas linhas do alvo local durante/após deploy
5. Login LDAP permanece obrigatório em todos os cenários
6. `dotnet test` passa

## Riscos e mitigações

| Risco | Mitigação |
|-------|-----------|
| Operador usa FQDN na lista mas atalho insere hostname curto | `IsLocalMachine` reconhece ambos; hint na grelha usa comparação por identidade local, não string exacta |
| Token não elevado apesar de perfil admin | Documentação; mensagem WMI 5 existente |
| Duplicação aparente hostname vs IP local | Atalho só adiciona hostname curto; operador pode usar IP manualmente — roteamento trata ambos |
