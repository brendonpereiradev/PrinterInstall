# Remoção completa do WinRM — canal remoto somente WMI

**Data:** 2026-05-28  
**Status:** Aprovado pelo utilizador (opção B)  
**Contexto:** WinRM indisponível nos alvos; WMI remoto validado (`Get-WmiObject Win32_Printer`). A app WPF já regista apenas `CimRemotePrinterOperations` em `App.xaml.cs`; este desenho remove o código WinRM/Composite morto do repositório.

## Objetivo

Eliminar por completo o canal WinRM (PowerShell Remoting / WS-Man) e o padrão primário+fallback, deixando **uma única** implementação de `IRemotePrinterOperations`: WMI/DCOM via `CimRemotePrinterOperations`.

## Fora de âmbito

- Renomear `CimRemotePrinterOperations` para outro nome (ex. `WmiRemotePrinterOperations`) — evita churn desnecessário; apenas comentários e documentação passam a dizer “WMI”.
- Reescrever specs/planos históricos em `docs/superpowers/` que mencionam WinRM (permanecem como arquivo).
- Alterar comportamento dos orquestradores (`PrinterDeploymentOrchestrator`, `PrinterControlOrchestrator`, etc.) — continuam a depender só de `IRemotePrinterOperations`.
- Configurar WinRM nos alvos ou políticas de firewall (infraestrutura).

## Estado actual vs alvo

| Item | Hoje | Alvo |
|------|------|------|
| Runtime WPF | `CimRemotePrinterOperations` | Igual |
| `WinRmRemotePrinterOperations` | Presente, não registado | **Removido** |
| `CompositeRemotePrinterOperations` | Presente | **Removido** |
| `PowerShellInvoker` / `IPowerShellInvoker` | Presente | **Removido** |
| `RemotePrinterQueueInfoJsonParser` | Só usado pelo WinRM (listagem JSON) | **Removido** |
| Pacote `System.Management.Automation` | Referenciado no Core | **Removido** |
| Testes WinRM/Composite/JsonParser | Presentes | **Removidos** |
| `docs/conexao-remota.md` | Menciona código WinRM legado | **Actualizado** (WMI único) |

## Arquitectura após a mudança

```text
PrinterInstall.App (WPF)
    └── IRemotePrinterOperations
            └── CimRemotePrinterOperations
                    ├── ManagementScope (\\host\root\cimv2)
                    ├── IRemoteDriverFileStager (SMB ADMIN$)
                    └── IRemoteProcessRunner (WmiRemoteProcessRunner / Win32_Process)
```

Não há camada composite nem invoker PowerShell.

## Ficheiros a apagar

**Core (`src/PrinterInstall.Core/Remote/`):**

- `WinRmRemotePrinterOperations.cs`
- `CompositeRemotePrinterOperations.cs`
- `PowerShellInvoker.cs`
- `IPowerShellInvoker.cs`
- `RemotePrinterQueueInfoJsonParser.cs`

**Testes (`tests/PrinterInstall.Core.Tests/Remote/`):**

- `WinRmRemotePrinterOperationsTests.cs`
- `WinRmRemotePrinterOperationsRemovalTests.cs`
- `CompositeRemotePrinterOperationsTests.cs`
- `RemotePrinterQueueInfoJsonParserTests.cs`

## Ficheiros a modificar

| Ficheiro | Alteração |
|----------|-----------|
| `PrinterInstall.Core.csproj` | Remover `PackageReference` `System.Management.Automation` |
| `CimRemotePrinterOperations.cs` | Comentário de classe: canal **único** WMI (não “fallback”); remover referências a sincronização com WinRM em `BuildInstallerScript` |
| `App.xaml.cs` | Comentário DI: WMI único; sem menção a WinRM legado |
| `docs/conexao-remota.md` | Documentar arquitectura WMI-only; remover secções de WinRM/Composite como código existente |

## Contrato `IRemotePrinterOperations`

Sem alterações de assinatura. Todos os métodos permanecem implementados em `CimRemotePrinterOperations` (já cobrem deploy, remoção, rename, driver install, test page, etc.).

## Comportamento e erros

- Falhas de rede/permissão WMI propagam-se como hoje (`UnauthorizedAccessException`, timeouts de `WmiRemoteProcessRunner`, etc.).
- Mensagens que citavam “WinRM” no composite deixam de existir; erros vêm só do caminho WMI.
- Instalação de driver: continua SMB + `install.ps1` via `Win32_Process.Create` (sem mudança funcional).

## Testes

- `dotnet build` da solution em Release.
- `dotnet test` em `PrinterInstall.Core.Tests` — todos os testes restantes devem passar (68 − removidos ≈ expectativa de ~55+ testes, conforme contagem após delete).
- Não adicionar testes de integração WMI real neste trabalho (rede/alvo variável).

## Riscos e mitigação

| Risco | Mitigação |
|-------|-----------|
| Ambiente futuro só com WinRM | Reintrodução exigiria novo código; aceite explícito do utilizador (opção B). |
| Parser JSON removido | Listagem no assistente usa WMI `Win32_Printer` directamente — já implementado em CIM. |
| Documentação histórica desactualizada | Spec nova + `conexao-remota.md`; planos antigos intactos. |

## Critérios de aceitação

1. Nenhuma referência compilável a `WinRm`, `CompositeRemote`, `IPowerShellInvoker`, `PowerShellInvoker` no `src/` ou `tests/`.
2. `System.Management.Automation` ausente do `.csproj`.
3. App inicia e DI resolve `IRemotePrinterOperations` → `CimRemotePrinterOperations`.
4. Build e testes unitários verdes.
5. `docs/conexao-remota.md` reflecte WMI como único canal.

## Plano de implementação

Seguir plano gerado pelo skill **writing-plans** após revisão desta spec pelo utilizador.
