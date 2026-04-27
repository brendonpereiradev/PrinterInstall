# Conexão remota no Printer Install

Este documento descreve como o aplicativo se conecta aos computadores alvo para listar drivers, criar portas TCP/IP, instalar filas de impressão, remover impressoras e instalar drivers quando configurado.

## Visão geral

O núcleo remoto está em `PrinterInstall.Core`, na pasta `Remote`. Duas implementações de `IRemotePrinterOperations` coexistem:


| Canal        | Tecnologia                                             | Uso típico                                                                        |
| ------------ | ------------------------------------------------------ | --------------------------------------------------------------------------------- |
| **Primário** | WinRM (PowerShell remoto sobre WS-Management)          | Preferido quando o serviço WinRM está ativo no alvo (porta HTTP 5985 por padrão). |
| **Fallback** | WMI/DCOM (`System.Management`, namespace `root\cimv2`) | Usado quando o canal WinRM falha (serviço parado, firewall, política, etc.).      |


A classe `CompositeRemotePrinterOperations` encadeia os dois: **tenta sempre o primário** e, em caso de exceção (exceto cancelamento), **repete a mesma operação pelo fallback**.

A aplicação WPF registra isso em `App.xaml.cs`: `WinRmRemotePrinterOperations` como primário, `CimRemotePrinterOperations` como fallback, ambos encapsulados em `CompositeRemotePrinterOperations`.

## Credenciais

1. **Login na interface**
  O usuário informa domínio, usuário e senha. O `LdapCredentialValidator` valida com **LDAP bind** ao controlador (porta **389**, sem SSL, no código atual), usando as mesmas credenciais.
2. **Operações remotas**
  Todas as chamadas WinRM e WMI usam `System.Net.NetworkCredential` com `Domínio\Usuário` (ou apenas usuário, se o domínio vier vazio). A senha é convertida para `SecureString` apenas no caminho WinRM (`PowerShellInvoker`), nunca embutida em scripts como texto claro.

**Requisito prático:** a conta precisa ter permissão de administrador (ou equivalente) no computador alvo para criar portas, filas e consultar drivers via WMI/Print Management.

## Canal WinRM (primário)

### Como a sessão é aberta

`PowerShellInvoker` cria um runspace remoto com:

- URI: `http://<computador>:5985/wsman` (WinRM HTTP padrão).
- Endpoint de shell: `http://schemas.microsoft.com/powershell/Microsoft.PowerShell`.
- Credencial: `PSCredential` com o mesmo utilizador do domínio.

O **nome do computador** pode ser hostname NetBIOS, FQDN ou endereço IP, desde que o cliente consiga resolver e alcançar o WinRM nesse host.

### O que é executado remotamente (`WinRmRemotePrinterOperations`)

Em linhas gerais, o primário usa cmdlets do subsistema de impressão:

- **Drivers instalados:** `Get-PrinterDriver` (nomes devolvidos pela API de impressão).
- **Porta TCP/IP:** `Add-PrinterPort` com nome, endereço do host da impressora e número da porta.
- **Fila:** `Add-Printer` com nome da fila, nome do driver e nome da porta.
- **Listagem / remoção:** `Get-Printer`, `Remove-Printer`, `Get-PrinterPort`, `Remove-PrinterPort`, contagens por porta.
- **Página de teste:** módulo `PrintManagement`, `Print-TestPage`.
- **Instalação de driver (quando habilitada):** cópia dos ficheiros do pacote para o alvo (via `IRemoteDriverFileStager`, tipicamente **ADMIN$**) e execução remota de `pnputil` / `Add-PrinterDriver` via PowerShell, com timeouts definidos no código.

Erros do PowerShell são agregados e lançados como exceção; o composite pode então acionar o fallback.

## Canal WMI (fallback)

### Ligação

`CimRemotePrinterOperations` usa `ManagementScope` para:

`\\<computador>\root\cimv2`

com:

- `Impersonation = Impersonate`
- `Authentication = PacketPrivacy`
- `EnablePrivileges = true`
- utilizador no formato `DOMÍNIO\utilizador`

Isto corresponde a **WMI remoto sobre DCOM**, não a WinRM. Na rede Windows, costuma implicar **RPC** (porta **135** e intervalo dinâmico de RPC) e regras de firewall que permitam **WMI** no alvo.

### Classes WMI usadas (exemplos)

- `Win32_PrinterDriver` — lista de drivers; o campo `Name` muitas vezes vem como `NomeDoDriver,Versão,Ambiente`; o código **normaliza** para ficar só o nome do driver antes de comparar com o catálogo.
- `Win32_TCPIPPrinterPort` — criação de porta TCP/IP (RAW/LPR conforme mapeamento numérico no código).
- `Win32_Printer` — criação da fila, listagem com `Name`/`PortName`, remoção quando aplicável.
- Métodos como `PrintTestPage` em `Win32_Printer` no caminho de teste de impressão.

Operações idempotentes: antes de criar porta ou fila, o código verifica se já existe com o mesmo nome.

### Instalação de driver no fallback

Quando o primário falha na instalação do driver, o composite regista no log que o WinRM falhou e delega ao CIM. O caminho WMI pode executar scripts remotos via `IRemoteProcessRunner` (por exemplo `Win32_Process.Create`) após staging dos ficheiros, com timeouts distintos dos do WinRM — ver implementação em `CimRemotePrinterOperations` para detalhes e limites.

## Comportamento do `CompositeRemotePrinterOperations`

- **Maioria das operações:** `try { primário } catch (não cancelamento) { fallback }`.
- **ListPrinterQueuesAsync:** tenta o primário; se devolver lista **vazia** ou falhar, tenta o fallback; mensagens de erro podem combinar falhas de ambos os canais para diagnóstico.
- **InstallPrinterDriverAsync:** tenta WinRM; em falha, reporta um resumo da mensagem de erro no progresso (`WINRM>> ...`) e tenta o caminho CIM.

Cancelamento (`OperationCanceledException`) **não** dispara fallback: a operação propaga-se.

## Identificação do computador alvo

O utilizador introduz nomes **um por linha**. O valor é passado tal como está para WinRM e WMI (`\\host\...` no WMI, URI WinRM com o mesmo host). Recomenda-se consistência com o DNS/rede (FQDN ou IP que o cliente resolve para a máquina certa).

## Requisitos de rede e serviços (resumo)


| Para WinRM                                                | Para WMI fallback                          |
| --------------------------------------------------------- | ------------------------------------------ |
| Serviço WinRM no alvo, escuta típica **5985/TCP** (HTTP). | Firewall a permitir **WMI** / RPC ao alvo. |
| Firewall cliente e servidor; perfil de rede adequado.     | Conta com direitos remotos WMI.            |
| Políticas de grupo podem restringir remoting.             |                                            |


Em ambientes onde WinRM **não** está configurado mas **WMI remoto** está permitido para administradores, o aplicativo ainda pode concluir deploy e outras operações pelo fallback — como observado em testes reais.

## Ficheiros de código principais

- `Remote/IPowerShellInvoker.cs`, `Remote/PowerShellInvoker.cs` — sessão WinRM e execução de script.
- `Remote/WinRmRemotePrinterOperations.cs` — operações via PowerShell remoto.
- `Remote/CimRemotePrinterOperations.cs` — operações via WMI e rotinas auxiliares (staging/processo remoto).
- `Remote/CompositeRemotePrinterOperations.cs` — estratégia primário + fallback.
- `Remote/IRemotePrinterOperations.cs` — contrato unificado.
- `Auth/LdapCredentialValidator.cs` — validação inicial no domínio (LDAP), independente do WinRM/WMI.

## Nota sobre LDAP e remoting

A validação **LDAP** no login confirma que as credenciais são aceites pelo Active Directory. **Não** substitui a necessidade de WinRM ou WMI estar acessível até cada **host** alvo: cada máquina é contactada individualmente com as mesmas credenciais de sessão.