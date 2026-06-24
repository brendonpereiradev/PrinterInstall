# Bypass automático de UAC remoto (elevação transient via schtasks)

**Data:** 2026-06-24  
**Status:** Aprovado pelo utilizador  
**Contexto:** Ao executar o Printer Install a partir de um perfil de administrador de domínio para configurar impressoras em PCs alvo (também com admin de domínio), operações remotas falham com "Acesso Negado" apesar de a conta ter privilégios teóricos. Causa provável: **UAC remoto** — conexões WMI/SMB recebem token administrativo **filtrado**, insuficiente para mutações no spooler, `pnputil` e `Add-PrinterDriver`.

## Objetivo

Contornar automaticamente a filtragem de token UAC remoto em ambientes **domínio AD**, usando apenas alterações **transientes** no PC alvo (tarefa agendada efémera + ficheiros temp), sem WinRM, sem alteração permanente de registo/GPO, e **sem alterar** o contrato `IRemotePrinterOperations`.

## Restrições confirmadas

| Item | Decisão |
|------|---------|
| Ambiente | Domínio AD; conta de domínio com admin local nos alvos |
| Ponto da falha | Indefinido na UI (mensagem genérica) — preflight + retry cobrem todos os cenários |
| Alterações no alvo | Somente transientes; revertidas no `finally` |
| Canal remoto | WMI + SMB (WinRM permanece fora do produto) |
| Registo permanente | **Fora de âmbito** (`LocalAccountTokenFilterPolicy`, etc.) |

## Abordagens consideradas

| # | Abordagem | Decisão |
|---|-----------|---------|
| 1 | Scheduled task efémera `/RL HIGHEST` | **Adotada** (runner elevado principal) |
| 2 | Autenticação de sessão IPC$ antes de SMB/WMI | **Adotada** (complementar, preflight) |
| 3 | Toggle temporário de registo | **Descartada** (foco errado para domínio; mesma limitação de token para escrever HKLM) |

## Arquitetura

```text
PrinterDeploymentOrchestrator / PrinterControlOrchestrator / PrinterRemovalOrchestrator
    └── IRemotePrinterOperations (contrato inalterado)
            └── RoutingRemotePrinterOperations
                    └── CimRemotePrinterOperations  ← integração
                            ├── RemoteHostSessionFactory   (preflight por alvo)
                            ├── WmiRemoteProcessRunner     (caminho direto + bootstrap schtasks)
                            └── ElevatedRemoteProcessRunner (schtasks efêmera)
```

**Princípio:** elevação encapsulada em `PrinterInstall.Core/Remote`; orquestradores e ViewModels não mudam assinaturas.

### RemoteHostSession

Devolvido pelo factory **uma vez por alvo por corrida** (cache em memória):

| Campo | Tipo | Significado |
|-------|------|-------------|
| `Host` | `string` | Nome/IP do alvo |
| `RequiresElevatedExecution` | `bool` | `true` se probe detectou token filtrado |
| `PreflightCompleted` | `bool` | Evita repetir probes na mesma corrida |

### Routing direct vs elevated

| Tipo | Operações | Caminho |
|------|-----------|---------|
| Leitura | `GetInstalledDriverNames`, `ListPrinterQueues`, `PrinterQueueExists`, `CountPrintersUsingPort` | WMI direto (token filtrado suficiente) |
| Mutação | `CreateTcpPrinterPort`, `AddPrinter`, `RemovePrinterQueue`, `RemoveTcpPrinterPort`, `PrintTestPage`, `InstallPrinterDriver`, `RenamePrinterQueue` | Direct se `RequiresElevatedExecution == false`; senão `ElevatedRemoteProcessRunner` |
| Fallback | Qualquer mutação | Se falhar com Access Denied → marcar `RequiresElevatedExecution = true` → **retry 1×** via elevado |

## Componentes novos

### RemoteHostSessionFactory

Preflight, em ordem:

1. `SmbShareConnection.Open(host, "IPC$", credential)` — autentica sessão SMB
2. `SmbShareConnection.Open(host, "ADMIN$", credential)` — confirma admin share
3. Probe WMI leitura: `SELECT Name FROM Win32_PrinterDriver` (sucesso da query = conectividade WMI OK; lista vazia é aceitável)
4. Probe de elevação via `Win32_Process.Create`:
   ```powershell
   powershell.exe -NoProfile -Command ^
     "if(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){'ELEVATION_PROBE>> TRUE'}else{'ELEVATION_PROBE>> FALSE'}"
   ```
   - Saída `FALSE` → `RequiresElevatedExecution = true`
5. Descartar handles SMB do preflight (sessão IPC$ permanece no perfil de rede do operador)

Cache: `ConcurrentDictionary<string, RemoteHostSession>` keyed por host normalizado; válido durante a corrida do orquestrador.

### ElevatedRemoteProcessRunner

Implementa `IRemoteProcessRunner` para execução elevada via schtasks efêmera:

1. Gerar `guid` e paths: `C:\Windows\Temp\PrinterInstall\<guid>\` (UNC via `ADMIN$`)
2. Stage `task.ps1` (conteúdo fornecido pelo caller) via `IRemoteDriverFileStager.WriteTextFileAsync`
3. Via `WmiRemoteProcessRunner`, executar sequência:
   - `schtasks /Create /TN PrinterInstall_<guid> /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Windows\Temp\PrinterInstall\<guid>\task.ps1" /SC ONCE /ST <HH:MM> /SD <MM/DD/YYYY> /RU SYSTEM /RL HIGHEST /F`
   - `schtasks /Run /TN PrinterInstall_<guid>`
4. Poll conclusão: ler ficheiro de log/sentinel via SMB até `RESULT>> OK|FAIL` ou timeout
5. **`finally` (sempre):**
   - `schtasks /Delete /TN PrinterInstall_<guid> /F` (best-effort via WMI)
   - Remover `C:\Windows\Temp\PrinterInstall\<guid>\` via stager cleanup

**`/RU SYSTEM`:** evita passar senha na linha de comando do schtasks. Criação de task SYSTEM requer privilégios de admin remoto (disponível com token filtrado na maioria dos ambientes de domínio).

Se `/Create` com SYSTEM falhar, **não** fazer fallback para `/RU DOMAIN\user /RP` (expõe credencial em linha de comando). Reportar erro acionável (ver § Erros).

### RemoteElevatedScriptBuilder

Gera scripts PowerShell executados no alvo (padrão `RESULT>> OK|FAIL`):

| Operação | Script |
|----------|--------|
| `CreateTcpPrinterPort` | `Add-PrinterPort` ou equivalente PrintManagement |
| `AddPrinter` | `Add-Printer -Name ... -DriverName ... -PortName ...` |
| `RemovePrinterQueue` | `Remove-Printer -Name ...` |
| `RemoveTcpPrinterPort` | `Remove-PrinterPort -Name ...` |
| `PrintTestPage` | WMI `PrintTestPage` no contexto local elevado |
| `InstallPrinterDriver` | `WmiPrinterOperationsCore.BuildInstallerScript(..., skipRunAsBlock: true)` |
| `RenamePrinterQueue` | Reutilizar `BuildRenamePrinterCommandLine` em script `-File` |

### AccessDeniedDetector

Helper estático que identifica falhas de token filtrado:

- `ManagementException` com HRESULT `0x80070005`
- `UnauthorizedAccessException`
- Mensagens contendo `Access is denied`, `Acesso negado`, `Access Denied`
- WMI `ReturnValue == 5` em mutações

## Fluxo de dados

```text
Orchestrator → CimRemotePrinterOperations.Mutate(...)
    → RemoteHostSessionFactory.PrepareAsync(host, cred)
        → IPC$ + ADMIN$ + WMI read + elevation probe
    → if RequiresElevatedExecution:
        → ElevatedRemoteProcessRunner.RunAsync(..., task.ps1)
    else:
        → WMI Put / WmiRemoteProcessRunner direct
    → on AccessDeniedDetector.IsAccessDenied(ex):
        → session.RequiresElevatedExecution = true
        → retry once via ElevatedRemoteProcessRunner
```

### Mensagens de log (UI)

Via `IProgress<string>` existente:

- `Autenticando sessão remota em {host} (IPC$)...`
- `Token administrativo filtrado detectado em {host} — execução elevada temporária`
- `Executando via tarefa agendada elevada (será removida ao concluir)...`

Credenciais e comandos com senha **nunca** aparecem no log.

## Tratamento de erros

| Situação | Comportamento |
|----------|---------------|
| IPC$ ou ADMIN$ falha | Abortar alvo: `Não foi possível autenticar sessão SMB em {host}. Verifique firewall (445) e permissões de admin.` |
| WMI leitura falha | Abortar antes de mutação: `WMI remoto indisponível em {host} (RPC 135, firewall WMI-In).` |
| Probe filtrado + schtasks OK | Caminho elevado silencioso |
| Mutação direct → Access Denied | Retry automático 1× via elevado |
| Retry elevado falha | `Acesso negado em {host} mesmo com execução elevada temporária. Verifique permissão para criar tarefas agendadas como SYSTEM (admin local / SeBatchLogonRight).` |
| `schtasks /Create` falha | Erro com Win32 code; mensagem específica |
| Timeout da task | Terminar processos best-effort; `schtasks /Delete /F` no `finally` |
| Cleanup falha | Warning no log; não falha operação se `RESULT>> OK` |

**Cancelamento:** `CancellationToken` propaga-se ao poll; `finally` garante delete da task e temp folder.

## Registo DI (`App.xaml.cs`)

```csharp
builder.Services.AddSingleton<RemoteHostSessionFactory>();
builder.Services.AddSingleton<WmiRemoteProcessRunner>();
builder.Services.AddSingleton<ElevatedRemoteProcessRunner>();
builder.Services.AddSingleton<IRemoteProcessRunner>(sp => sp.GetRequiredService<WmiRemoteProcessRunner>());
// CimRemotePrinterOperations recebe factory + ambos runners via construtor
```

`CimRemotePrinterOperations` passa a receber `RemoteHostSessionFactory`, `WmiRemoteProcessRunner` e `ElevatedRemoteProcessRunner` explicitamente (não só `IRemoteProcessRunner`).

## Ficheiros

| Ação | Ficheiro |
|------|----------|
| **Novo** | `RemoteHostSession.cs` |
| **Novo** | `RemoteHostSessionFactory.cs` |
| **Novo** | `ElevatedRemoteProcessRunner.cs` |
| **Novo** | `RemoteElevatedScriptBuilder.cs` |
| **Novo** | `AccessDeniedDetector.cs` |
| **Modificar** | `CimRemotePrinterOperations.cs` |
| **Modificar** | `WmiPrinterOperationsCore.cs` — `BuildInstallerScript(..., skipRunAsBlock: bool)` |
| **Modificar** | `App.xaml.cs` |
| **Modificar** | `docs/conexao-remota.md` |
| **Novos testes** | `AccessDeniedDetectorTests.cs`, `ElevatedRemoteProcessRunnerTests.cs`, `RemoteHostSessionFactoryTests.cs`, `RemoteElevatedScriptBuilderTests.cs`, `CimRemotePrinterOperationsElevationTests.cs` |

## Testes

### Unitários (CI)

| Teste | Valida |
|-------|--------|
| `AccessDeniedDetectorTests` | Parsing exceções pt/en, HRESULT, WMI return 5 |
| `ElevatedRemoteProcessRunnerTests` | Sequência Create→Run→Delete; cleanup no `finally`; timeout |
| `RemoteHostSessionFactoryTests` | Probe TRUE→direct; FALSE→elevated; cache por host |
| `RemoteElevatedScriptBuilderTests` | Scripts emitem `RESULT>>`; install sem bloco RunAs quando elevado |
| `CimRemotePrinterOperationsElevationTests` | Access Denied → retry elevado → sucesso (mocks) |

### Manuais (checklist no plano de implementação)

- [ ] Dois PCs domínio: deploy completo com conta admin de domínio, UAC ligado nos alvos
- [ ] Log mostra detecção de token filtrado e execução via schtasks
- [ ] Após deploy, `schtasks /Query` no alvo **não** lista `PrinterInstall_*`
- [ ] Pasta `C:\Windows\Temp\PrinterInstall\` no alvo limpa após operação
- [ ] Removal wizard e rename funcionam com elevação automática

## Fora de âmbito

- Reintroduzir WinRM / PowerShell Remoting
- Alteração permanente de registo ou GPO nos alvos
- PsExec ou binários externos
- Suporte a workgroup/contas locais (política diferente; pode ser spec futura)
- UI dedicada para escolher modo de elevação (100% automático)

## Critérios de aceite

1. Deploy remoto em PC domínio com UAC remoto activo conclui sem "Acesso Negado" para conta admin de domínio válida
2. Nenhum artefacto permanente no alvo após sucesso ou falha (task + temp removidos)
3. Operações de leitura continuam via WMI directo (sem overhead de schtasks)
4. Retry automático cobre falha em qualquer operação mutante sem configuração manual
5. Testes unitários novos passam; documentação `conexao-remota.md` actualizada
