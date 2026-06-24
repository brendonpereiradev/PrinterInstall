# Conexão remota no Printer Install

Este documento descreve como o aplicativo se conecta aos computadores alvo para listar drivers, criar portas TCP/IP, instalar filas de impressão, remover impressoras e instalar drivers quando configurado.

## Visão geral

O núcleo remoto está em `PrinterInstall.Core/Remote`. `IRemotePrinterOperations` é implementado por `RoutingRemotePrinterOperations`, que escolhe automaticamente entre caminho **local** e **remoto**:

| Alvo | Implementação | Tecnologia |
| ---- | ------------- | ---------- |
| **Máquina local** (hostname, IP ou literais) | `LocalPrinterOperations` | WMI `root\cimv2` sem credencial alternativa; driver via `%TEMP%` + `install.ps1` local |
| **PC remoto** | `CimRemotePrinterOperations` (+ `RemoteHostSessionFactory`, `ElevatedRemoteProcessRunner`) | WMI `\\host\root\cimv2` + SMB `ADMIN$`; mutações elevadas via schtasks quando UAC remoto filtra o token |

Registo em `App.xaml.cs`: `IRemotePrinterOperations` → `RoutingRemotePrinterOperations` → (`LocalPrinterOperations` | `CimRemotePrinterOperations`).

No caminho remoto, `CimRemotePrinterOperations` coordena leituras WMI directas e mutações privilegiadas. `RemoteHostSessionFactory` faz preflight (IPC$, WMI, probe de elevação) e cacheia o estado por host; `ElevatedRemoteProcessRunner` executa mutações via tarefa agendada efémera (`schtasks /RU SYSTEM /RL HIGHEST`) quando o token administrativo está filtrado.

**WinRM / PowerShell Remoting não faz parte do produto.** Não é necessário o serviço WinRM nem a porta **5985** nos alvos.

## Máquina local

Quando o operador inclui na lista o hostname ou IP do PC onde o app está a correr, `LocalMachineIdentity` detecta o alvo e usa WMI local — evitando falhas de loopback WMI/SMB com credenciais alternativas.

Identificadores reconhecidos (case-insensitive):

- `Environment.MachineName` e aliases DNS/FQDN
- Endereços IP das interfaces de rede locais
- Literais: `localhost`, `.`, `127.0.0.1`, `::1`

### Configurar este PC (fluxo recomendado)

1. Faça login LDAP (obrigatório em todos os cenários).
2. Na tela principal, clique em **Adicionar este PC** — insere o hostname curto desta máquina na lista de alvos (sem duplicar se já estiver presente como hostname, IP ou literal local).
3. Preencha a impressora e, se desejar, marque **Imprimir teste**.
4. Clique em **Implantar**.

O mesmo fluxo serve para operações mistas: adicione este PC e outros hosts do domínio na mesma lista. A grelha de status mostra **(este PC)** nas linhas do alvo local.

Requisitos na máquina de execução: perfil com privilégios de administrador local (WMI, `pnputil`, página de teste). Login LDAP permanece obrigatório mesmo quando todos os alvos são locais.

## Credenciais

1. **Login na interface** — `LdapCredentialValidator` valida domínio, usuário e senha via LDAP (porta **389**).
2. **Operações remotas** — `System.Net.NetworkCredential` (`Domínio\Usuário`) em todas as chamadas WMI e montagem SMB.

A conta precisa ser **administrador no computador alvo** e ter permissão para WMI remoto e DCOM. Sem isso, operações falham com `Access is denied`.

## UAC remoto e elevação automática

Em PCs de domínio com UAC activo, ligações WMI/SMB recebem frequentemente um **token administrativo filtrado**. Leituras (listar drivers, filas) funcionam; mutações (porta, fila, driver, remoção) podem falhar com *Acesso negado*.

Antes de mutar cada alvo remoto, o app executa **preflight**:

1. Autentica `\\host\IPC$` e `\\host\ADMIN$`
2. Testa WMI (`Win32_PrinterDriver`)
3. Probe de elevação (processo remoto escreve `ELEVATION_PROBE>> TRUE|FALSE` em ficheiro temp)

Se o token estiver filtrado, mutações passam por **tarefa agendada efémera** (`schtasks /RU SYSTEM /RL HIGHEST`). A tarefa e a pasta temp são removidas no `finally`. Nenhuma alteração permanente de registo.

Mensagens na UI: *Autenticando sessão remota*, *Token administrativo filtrado detectado*, *Executando via tarefa agendada elevada*.

## Ligação WMI

`CimRemotePrinterOperations` usa `ManagementScope` para `\\<computador>\root\cimv2` com:

- `Impersonation = Impersonate`
- `Authentication = PacketPrivacy`
- `EnablePrivileges = true`

Na rede: RPC (porta **135/TCP** e portas dinâmicas DCOM) e firewall **WMI-In** no perfil em uso.

### Classes WMI (exemplos)

- `Win32_PrinterDriver` — drivers instalados (nome normalizado)
- `Win32_TCPIPPrinterPort` — portas TCP/IP
- `Win32_Printer` — filas, listagem, remoção, `PrintTestPage`
- `Win32_Process.Create` — execução remota de `install.ps1` (instalação de driver)

### Instalação de driver

1. Cópia do pacote para `\\host\ADMIN$\...` (`SmbRemoteDriverFileStager`)
2. Script `install.ps1` no alvo via `WmiRemoteProcessRunner`
3. Progresso na UI: mensagens com `via WMI` quando aplicável

## Identificação do alvo

Nomes **um por linha** (hostname, FQDN ou IP), passados tal como estão para `\\host\root\cimv2` e `\\host\ADMIN$`.

## Requisitos de rede (resumo)

| Requisito | Detalhe |
| --------- | ------- |
| RPC | **135/TCP** + DCOM dinâmico |
| Firewall | **WMI-In** habilitado |
| Conta | Admin local no alvo + WMI remoto |
| SMB | `\\host\ADMIN$` para drivers |

## Ficheiros principais

- `Remote/RoutingRemotePrinterOperations.cs` — roteamento local vs remoto
- `Remote/LocalMachineIdentity.cs` — detecção da máquina local
- `Remote/LocalPrinterOperations.cs` — operações WMI locais
- `Remote/CimRemotePrinterOperations.cs` — operações WMI remotas
- `Remote/RemoteHostSessionFactory.cs` — preflight IPC$/WMI e probe de elevação
- `Remote/ElevatedRemoteProcessRunner.cs` — mutações via schtasks efémera
- `Remote/RemoteElevatedScriptBuilder.cs` — scripts PowerShell para mutações elevadas
- `Remote/AccessDeniedDetector.cs` — detecção de *Access Denied* / token filtrado
- `Remote/WmiPrinterOperationsCore.cs` — helpers WMI partilhados
- `Remote/WmiRemoteProcessRunner.cs` — `Win32_Process` (remoto)
- `Remote/SmbRemoteDriverFileStager.cs` — staging SMB (remoto)
- `Remote/IRemotePrinterOperations.cs` — contrato
- `Auth/LdapCredentialValidator.cs` — login (independente de WMI por host)

## LDAP vs WMI por máquina

LDAP no login só confirma credenciais no Active Directory. **Cada host alvo** é contactado individualmente via WMI com as mesmas credenciais de sessão.
