# Roteamento de operações para a máquina local

**Data:** 2026-05-29  
**Status:** Implementado  
**Contexto:** Ao incluir o hostname ou IP da máquina onde o Printer Install está a correr na lista de alvos, o deploy e o Removal Wizard falham com estado **Erro** (típico de loopback WMI/SMB com credenciais alternativas). Os PCs remotos na mesma lista configuram-se correctamente.

## Objetivo

Quando o alvo for a **máquina local**, executar operações de impressora via WMI local e instalação de driver in-process, em vez de `\\host\root\cimv2` e `\\host\ADMIN$` com `NetworkCredential`. PCs remotos mantêm o caminho actual (`CimRemotePrinterOperations`).

## Fora de âmbito

- Auto-inclusão da máquina local na lista de alvos (continua manual).
- Novos estados ou rótulos na UI (`Local`, `Conectando localmente`, etc.).
- Alteração do fluxo de login LDAP ou validação de credenciais.
- Renomear `IRemotePrinterOperations` ou `ContactingRemote` nos enums de progresso.
- Testes de integração WMI real contra hardware de rede.

## Problema confirmado

| Facto | Detalhe |
|-------|---------|
| Lista de alvos | Operador inclui manualmente hostname curto **ou** IP da máquina local |
| Sintoma | Estado **Erro** na grelha (ex.: Access denied, falha ao ligar) |
| Remoção | Mesmo comportamento no Removal Wizard |
| Causa raiz | Todo o tráfego passa por `CimRemotePrinterOperations`, que usa WMI/SMB remoto mesmo para loopback |

## Arquitectura alvo

```text
PrinterInstall.App (WPF)
    └── Orquestradores (deploy, remoção, rollback, controlo)
            └── IRemotePrinterOperations
                    └── RoutingRemotePrinterOperations
                            ├── LocalMachineIdentity.IsLocalMachine(name)?
                            │       ├── sim  → LocalPrinterOperations
                            │       └── não  → CimRemotePrinterOperations
                            │
LocalPrinterOperations
    ├── WMI local (ManagementScope "root\\cimv2", sem credencial alternativa)
    └── Driver: staging em %TEMP% + powershell.exe -File install.ps1 (processo local)

CimRemotePrinterOperations (inalterado em comportamento remoto)
    ├── ManagementScope (\\host\root\cimv2 + NetworkCredential)
    ├── IRemoteDriverFileStager (SMB ADMIN$)
    └── IRemoteProcessRunner (Win32_Process.Create)
```

Orquestradores, ViewModels e contratos públicos **não** recebem alterações de assinatura.

## Componentes novos

### `LocalMachineIdentity`

Responsável por resolver e cachear (lazy, thread-safe) os identificadores da máquina actual e responder `bool IsLocalMachine(string computerName)`.

Comparação **case-insensitive** contra:

1. `Environment.MachineName`
2. `Dns.GetHostName()` e `HostName` / `Aliases` de `Dns.GetHostEntry` (best-effort; falhas DNS ignoradas)
3. Endereços IPv4 e IPv6 unicast de todas as interfaces (`NetworkInterface.GetAllNetworkInterfaces`)
4. Literais: `localhost`, `.`, `127.0.0.1`, `::1`

Entrada já trimada pelo caller; strings vazias → `false`.

### `LocalPrinterOperations`

Implementa `IRemotePrinterOperations` usando WMI local. O parâmetro `NetworkCredential` é **ignorado** — operações correm com o token do utilizador que executa a app (mesmo requisito de admin local que já existe para WMI remoto).

| Método | Implementação local |
|--------|---------------------|
| `GetInstalledDriverNamesAsync` | WMI `Win32_PrinterDriver` |
| `CreateTcpPrinterPortAsync` | WMI `Win32_TCPIPPrinterPort`; idempotente se porta existe |
| `PrinterQueueExistsAsync` | WMI `Win32_Printer` |
| `AddPrinterAsync` | WMI `Win32_Printer` |
| `PrintTestPageAsync` | `Win32_Printer.PrintTestPage` |
| `ListPrinterQueuesAsync` | WMI `Win32_Printer` |
| `RemovePrinterQueueAsync` | WMI delete |
| `RenamePrinterQueueAsync` | `Process.Start` local com o mesmo comando PowerShell usado remotamente |
| `CountPrintersUsingPortAsync` | WMI query por `PortName` |
| `RemoveTcpPrinterPortAsync` | WMI delete |
| `InstallPrinterDriverAsync` | Copiar pacote para `%TEMP%\PrinterInstall\{guid}\`; escrever `install.ps1` (reutilizar `BuildInstallerScript`); executar `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "..."` com timeout; ler `install.log`; validar linha `RESULT>> OK`; limpar pasta temp no `finally` |

Mensagens de progresso do driver no caminho local:

- `"Staging driver files locally..."`
- `"Launching install script locally (timeout {N}min)..."`

### `RoutingRemotePrinterOperations`

Decorator que implementa `IRemotePrinterOperations` e delega cada método:

```csharp
if (_identity.IsLocalMachine(computerName))
    return _local.Método(...);
return _remote.Método(...);
```

`computerName` passado aos métodos internos pode ser o valor original da lista (local ou remoto); implementações locais ignoram o nome para o scope WMI.

### Refactor partilhado (mínimo)

Extrair de `CimRemotePrinterOperations` para classe interna estática `WmiPrinterOperationsCore` (ou equivalente):

- `CreateLocalScope()` → `new ManagementScope(@"root\cimv2")`
- Helpers que recebem `ManagementScope`: `PortExists`, `PrinterExists`, `NormalizeWmiDriverName`, `EscapeWql`, `MapProtocol`
- `BuildInstallerScript` (já estático)

`CimRemotePrinterOperations` mantém `CreateScope(computerName, credential)` para remoto; `LocalPrinterOperations` usa `CreateLocalScope()`.

## Registo DI (`App.xaml.cs`)

```csharp
builder.Services.AddSingleton<LocalMachineIdentity>();
builder.Services.AddSingleton<LocalPrinterOperations>();
builder.Services.AddSingleton<CimRemotePrinterOperations>();
builder.Services.AddSingleton<IRemotePrinterOperations>(sp =>
    new RoutingRemotePrinterOperations(
        sp.GetRequiredService<LocalMachineIdentity>(),
        sp.GetRequiredService<LocalPrinterOperations>(),
        sp.GetRequiredService<CimRemotePrinterOperations>()));
```

## Fluxos afectados (herdam correção automaticamente)

- `PrinterDeploymentOrchestrator` — deploy multi-PC
- `PrinterControlOrchestrator` / `PrinterRemovalOrchestrator` — Removal Wizard
- `DeploymentRollbackRunner` — rollback após cancelamento

## Comportamento e erros

- **Remotos:** sem alteração de latência ou mensagens.
- **Local — sucesso:** mesmos estados finais (`CompletedSuccess`, `TargetCompleted`, etc.).
- **Local — falha de privilégio:** WMI ou `pnputil` propagam excepção; grelha mostra **Erro** como hoje nos remotos.
- **Local — timeout de driver:** `TimeoutException` após `InstallTimeout` (3 min), alinhado ao remoto.
- Credencial LDAP continua obrigatória no login; não validamos se o utilizador logado coincide com a credencial de domínio para o caminho local.

## Impacto de performance

- Overhead do roteador: comparação O(n) contra lista cacheada de identificadores locais — desprezível.
- Caminho local evita loopback WMI/SMB; instalação de driver tende a ser **igual ou mais rápida** que o caminho remoto actual (que falhava).

## Testes

| Ficheiro | Casos |
|----------|-------|
| `LocalMachineIdentityTests` | Hostname, FQDN, IP local, literais → `true`; nome/IP remoto → `false`; string vazia → `false` |
| `RoutingRemotePrinterOperationsTests` | Mock de local e remote; verificar delegação por nome; verificar que remote não é chamado para identidade local |
| Orquestradores existentes | Continuam com mock de `IRemotePrinterOperations`; sem regressão |

## Documentação

Actualizar `docs/conexao-remota.md` com secção **Máquina local**: detecção automática, caminho WMI local, requisito de execução como administrador local.

## Critérios de aceitação

1. Lista com hostname local + PCs remotos: local e remotos concluem deploy com sucesso.
2. Lista com IP local + PCs remotos: idem.
3. Removal Wizard com local na lista: remoção conclui sem erro de loopback.
4. Apenas PCs remotos na lista: comportamento idêntico ao actual.
5. `dotnet test` passa sem regressões nos testes existentes.

## Decisões registadas

| Decisão | Escolha |
|---------|---------|
| Abordagem | A — roteador em `IRemotePrinterOperations` |
| Auto-inclusão local | Não |
| Interface pública | Sem alterações |
| Script de driver | Reutilizar `BuildInstallerScript` existente |
