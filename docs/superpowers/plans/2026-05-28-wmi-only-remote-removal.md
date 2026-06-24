# WMI-Only Remote Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all WinRM / Composite / PowerShell Remoting code from the repository so `IRemotePrinterOperations` is implemented only by `CimRemotePrinterOperations` (WMI/DCOM).

**Architecture:** Delete dead WinRM stack (`WinRmRemotePrinterOperations`, `PowerShellInvoker`, `CompositeRemotePrinterOperations`, JSON parser used only by WinRM). Drop `System.Management.Automation` package. Keep orchestrators and WPF DI unchanged except comments/docs. Verify with grep + `dotnet build` + `dotnet test`.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, `System.Management` (WMI), xUnit, Moq.

**Spec:** `docs/superpowers/specs/2026-05-28-wmi-only-remote-removal-design.md`

**Note on commits:** The repository owner commits only when explicitly requested. Steps labeled "Commit" are optional; otherwise stop after Task 5 verification.

---

## File map (before → after)

| Responsibility | Keep | Delete |
|----------------|------|--------|
| Remote contract | `IRemotePrinterOperations.cs` | — |
| WMI implementation | `CimRemotePrinterOperations.cs` | — |
| SMB staging | `SmbRemoteDriverFileStager.cs`, `SmbShareConnection.cs`, `RemoteDriverStagingPaths.cs` | — |
| Remote process | `WmiRemoteProcessRunner.cs`, `IRemoteProcessRunner.cs` | — |
| WinRM channel | — | `WinRmRemotePrinterOperations.cs` |
| Composite fallback | — | `CompositeRemotePrinterOperations.cs` |
| PS Remoting | — | `PowerShellInvoker.cs`, `IPowerShellInvoker.cs` |
| WinRM JSON list | — | `RemotePrinterQueueInfoJsonParser.cs` |
| App DI | `App.xaml.cs` (already CIM-only) | — |
| Docs | `docs/conexao-remota.md` (rewrite) | — |

---

### Task 1: Baseline verification

**Files:** None (read-only).

- [ ] **Step 1: Record current test count**

Run from repo root:

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Release --no-restore 2>&1 | Select-String "Passed!"
```

Expected (approximate): `Passed: 68` — baseline before deletions.

- [ ] **Step 2: Confirm WPF already uses CIM only**

Open `src/PrinterInstall.App/App.xaml.cs` and verify:

```csharp
builder.Services.AddSingleton<CimRemotePrinterOperations>();
builder.Services.AddSingleton<IRemotePrinterOperations>(sp =>
    sp.GetRequiredService<CimRemotePrinterOperations>());
```

No `WinRmRemotePrinterOperations` or `CompositeRemotePrinterOperations` registration.

- [ ] **Step 3: Build Release**

```powershell
dotnet build "PrinterInstall.sln" -c Release
```

Expected: `Build succeeded.` with 0 errors.

---

### Task 2: Delete WinRM implementation files (Core)

**Files:**
- Delete: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`
- Delete: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`
- Delete: `src/PrinterInstall.Core/Remote/PowerShellInvoker.cs`
- Delete: `src/PrinterInstall.Core/Remote/IPowerShellInvoker.cs`
- Delete: `src/PrinterInstall.Core/Remote/RemotePrinterQueueInfoJsonParser.cs`

- [ ] **Step 1: Delete the five files**

Use IDE delete or:

```powershell
Remove-Item @(
  "src\PrinterInstall.Core\Remote\WinRmRemotePrinterOperations.cs",
  "src\PrinterInstall.Core\Remote\CompositeRemotePrinterOperations.cs",
  "src\PrinterInstall.Core\Remote\PowerShellInvoker.cs",
  "src\PrinterInstall.Core\Remote\IPowerShellInvoker.cs",
  "src\PrinterInstall.Core\Remote\RemotePrinterQueueInfoJsonParser.cs"
) -Force
```

- [ ] **Step 2: Build Core (expect fail until Task 3)**

```powershell
dotnet build "src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release
```

Expected: **FAIL** only if something still references deleted types (there should be none in `src/` except package). If fail mentions `System.Management.Automation` only, proceed to Task 3.

---

### Task 3: Remove System.Management.Automation package

**Files:**
- Modify: `src/PrinterInstall.Core/PrinterInstall.Core.csproj`

- [ ] **Step 1: Edit csproj**

Remove this entire line from `ItemGroup`:

```xml
    <PackageReference Include="System.Management.Automation" Version="7.4.6" />
```

Resulting `ItemGroup` should contain only:

```xml
  <ItemGroup>
    <PackageReference Include="System.DirectoryServices.Protocols" Version="8.0.0" />
    <PackageReference Include="System.Management" Version="8.0.0" />
  </ItemGroup>
```

- [ ] **Step 2: Build Core**

```powershell
dotnet build "src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release
```

Expected: `Build succeeded.`

---

### Task 4: Delete WinRM-related tests

**Files:**
- Delete: `tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsTests.cs`
- Delete: `tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsRemovalTests.cs`
- Delete: `tests/PrinterInstall.Core.Tests/Remote/CompositeRemotePrinterOperationsTests.cs`
- Delete: `tests/PrinterInstall.Core.Tests/Remote/RemotePrinterQueueInfoJsonParserTests.cs`

- [ ] **Step 1: Delete the four test files**

```powershell
Remove-Item @(
  "tests\PrinterInstall.Core.Tests\Remote\WinRmRemotePrinterOperationsTests.cs",
  "tests\PrinterInstall.Core.Tests\Remote\WinRmRemotePrinterOperationsRemovalTests.cs",
  "tests\PrinterInstall.Core.Tests\Remote\CompositeRemotePrinterOperationsTests.cs",
  "tests\PrinterInstall.Core.Tests\Remote\RemotePrinterQueueInfoJsonParserTests.cs"
) -Force
```

- [ ] **Step 2: Run tests**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Release
```

Expected: `Passed: 53` (68 − 15 removed tests). Failed: 0.

---

### Task 5: Update CimRemotePrinterOperations summary comment

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` (lines 9–12)

- [ ] **Step 1: Replace class XML doc**

Replace:

```csharp
/// <summary>
/// Implementa operações remotas via WMI/DCOM (fallback para quando WinRM não está disponível).
/// Lista drivers instalados, cria portas TCP/IP e adiciona filas de impressão.
/// </summary>
```

With:

```csharp
/// <summary>
/// Implementação única de operações remotas via WMI/DCOM (<c>\\host\root\cimv2</c>).
/// Lista drivers, cria portas TCP/IP, gere filas, remove/renomeia e instala drivers (SMB + Win32_Process).
/// </summary>
```

- [ ] **Step 2: Build Core**

```powershell
dotnet build "src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release
```

Expected: PASS.

---

### Task 6: Clean App.xaml.cs DI comment

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`

- [ ] **Step 1: Update comment above CIM registration**

Replace:

```csharp
        // WMI/DCOM only (no WinRM attempt). WinRm + Composite types remain in Core for tests/future use.
        builder.Services.AddSingleton<CimRemotePrinterOperations>();
```

With:

```csharp
        // Remote operations: WMI/DCOM only (CimRemotePrinterOperations).
        builder.Services.AddSingleton<CimRemotePrinterOperations>();
```

- [ ] **Step 2: Build solution**

```powershell
dotnet build "PrinterInstall.sln" -c Release
```

Expected: PASS (App + Core + tests projects).

---

### Task 7: Rewrite docs/conexao-remota.md

**Files:**
- Modify: `docs/conexao-remota.md` (full replace)

- [ ] **Step 1: Replace file contents**

Overwrite `docs/conexao-remota.md` with:

```markdown
# Conexão remota no Printer Install

Este documento descreve como o aplicativo se conecta aos computadores alvo para listar drivers, criar portas TCP/IP, instalar filas de impressão, remover impressoras e instalar drivers quando configurado.

## Visão geral

O núcleo remoto está em `PrinterInstall.Core/Remote`. Existe **uma única** implementação de `IRemotePrinterOperations`:

| Canal | Tecnologia | Uso |
| ----- | ---------- | --- |
| **WMI/DCOM** | `System.Management`, namespace `\\host\root\cimv2` | Todas as operações remotas |

Registo em `App.xaml.cs`: `IRemotePrinterOperations` → `CimRemotePrinterOperations`.

**WinRM / PowerShell Remoting não faz parte do produto.** Não é necessário o serviço WinRM nem a porta **5985** nos alvos.

## Credenciais

1. **Login na interface** — `LdapCredentialValidator` valida domínio, usuário e senha via LDAP (porta **389**).
2. **Operações remotas** — `System.Net.NetworkCredential` (`Domínio\Usuário`) em todas as chamadas WMI e montagem SMB.

A conta precisa ser **administrador no computador alvo** e ter permissão para WMI remoto e DCOM. Sem isso, operações falham com `Access is denied`.

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

- `Remote/CimRemotePrinterOperations.cs` — operações WMI
- `Remote/WmiRemoteProcessRunner.cs` — `Win32_Process`
- `Remote/SmbRemoteDriverFileStager.cs` — staging SMB
- `Remote/IRemotePrinterOperations.cs` — contrato
- `Auth/LdapCredentialValidator.cs` — login (independente de WMI por host)

## LDAP vs WMI por máquina

LDAP no login só confirma credenciais no Active Directory. **Cada host alvo** é contactado individualmente via WMI com as mesmas credenciais de sessão.
```

- [ ] **Step 2: No build step** (markdown only).

---

### Task 8: Repository audit (acceptance criteria)

**Files:** None (verification).

- [ ] **Step 1: Grep src/ and tests/ for forbidden symbols**

From repo root:

```powershell
$patterns = @('WinRmRemote','CompositeRemote','IPowerShellInvoker','PowerShellInvoker','RemotePrinterQueueInfoJsonParser','System.Management.Automation')
$hits = Select-String -Path @('src\**\*.cs','tests\**\*.cs') -Pattern $patterns -SimpleMatch
if ($hits) { $hits; throw "Forbidden references remain" } else { "OK: no forbidden references in src/tests" }
```

Expected: `OK: no forbidden references in src/tests`

- [ ] **Step 2: Grep csproj for Automation package**

```powershell
Select-String -Path "src\**\*.csproj" -Pattern "System.Management.Automation"
```

Expected: **no output**.

- [ ] **Step 3: Full solution build + test**

```powershell
dotnet build "PrinterInstall.sln" -c Release
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Release --no-build
```

Expected: Build succeeded; Passed: 53 (or current count with 0 failed).

- [ ] **Step 4: Optional manual smoke**

Launch `PrinterInstall.App` (Release), login, list printers on a known-good WMI host (e.g. IP validated with `Get-WmiObject Win32_Printer`). Confirm wizard listing works.

---

### Task 9: Commit (optional — only if user requests)

**Files:** All touched in Tasks 2–7.

- [ ] **Step 1: Stage and commit**

```powershell
git add -A src/PrinterInstall.Core/ tests/PrinterInstall.Core.Tests/ src/PrinterInstall.App/App.xaml.cs docs/conexao-remota.md
git status
```

Suggested message:

```
refactor(core): remove WinRM stack; WMI-only remote operations

Delete WinRm, Composite, PowerShellInvoker, and JSON parser used only by WinRM.
Drop System.Management.Automation. Update conexao-remota.md and CIM docs.
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| Delete 5 Core WinRM files | Task 2 |
| Delete 4 test files | Task 4 |
| Remove Automation package | Task 3 |
| Update CimRemotePrinterOperations comment | Task 5 |
| Update App.xaml.cs comment | Task 6 |
| Update conexao-remota.md | Task 7 |
| No compilable WinRM refs in src/tests | Task 8 Step 1 |
| Build + tests green | Task 8 Step 3 |
| Orchestrators unchanged | No task (verify none import deleted types) |
| Historical superpowers docs untouched | No task (by design) |

## Self-review (plan author)

- [x] No TBD/TODO placeholders in steps
- [x] Exact paths and commands provided
- [x] Spec acceptance criteria mapped to Task 8
- [x] Deletion work does not use fake TDD; verification steps substitute
- [x] Expected test count documented (53 after −15 tests)
