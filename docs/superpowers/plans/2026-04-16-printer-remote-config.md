# Printer Remote Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar uma solução .NET 8 WPF com biblioteca Core que valida credenciais LDAP ao domínio, configura impressoras TCP/IP remotamente (WinRM primeiro, CIM em recurso), valida drivers instalados sem os instalar, e apresenta estado sequencial por máquina com log.

**Architecture:** `PrinterInstall.Core` concentra modelos, catálogo, correspondência de nomes de driver, autenticação LDAP injectável, gateway remoto (WinRM via `System.Management.Automation`, recurso CIM via `CimSession`) e orquestrador sequencial com eventos para a UI. `PrinterInstall.App` é WPF MVVM (`CommunityToolkit.Mvvm`), `Host.CreateDefaultBuilder` para DI, e vistas que consomem apenas interfaces e DTOs da Core.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.DirectoryServices.Protocols (LDAP bind), System.Management.Automation (PowerShell remoto), Microsoft.Management.Infrastructure (CIM remoto), xUnit, Moq.

**Spec de referência:** `docs/superpowers/specs/2026-04-16-printer-remote-config-design.md`

---

## Estrutura de ficheiros (greenfield)

| Caminho | Responsabilidade |
|---------|------------------|
| `PrinterInstall.sln` | Solução na raiz do repositório |
| `src/PrinterInstall.Core/PrinterInstall.Core.csproj` | Biblioteca: domínio, serviços, orquestração |
| `src/PrinterInstall.Core/Models/PrinterBrand.cs` | Enum Epson / Lexmark / Gainscha |
| `src/PrinterInstall.Core/Models/TargetMachineState.cs` | Enum de estado por máquina (spec §7) |
| `src/PrinterInstall.Core/Models/DeploymentParameters.cs` | DTO: alvos, marca, modelo, nome de exibição, TCP, credenciais em `NetworkCredential` |
| `src/PrinterInstall.Core/Models/DeploymentLogEntry.cs` | Timestamp + alvo + mensagem (sem segredos) |
| `src/PrinterInstall.Core/Catalog/PrinterCatalog.cs` | Lista de modelos por marca + resolução de nome de driver esperado |
| `src/PrinterInstall.Core/Validation/ComputerNameListParser.cs` | Normaliza texto multilinha → lista de nomes não vazios |
| `src/PrinterInstall.Core/Validation/ComputerNameValidator.cs` | Regras simples de formato (comprimento, caracteres) |
| `src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs` | Comparação case-insensitive contra lista de nomes remotos |
| `src/PrinterInstall.Core/Auth/ILdapCredentialValidator.cs` | Contrato de validação LDAP |
| `src/PrinterInstall.Core/Auth/LdapCredentialValidator.cs` | Implementação com `LdapConnection` + `LdapDirectoryIdentifier` |
| `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` | Listar nomes de driver, criar porta TCP, criar impressora |
| `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` | `Invoke-Command` + script blocks PowerShell |
| `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` | CIM remoto quando WinRM falha |
| `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` | Ordem: WinRM → CIM; erros documentados |
| `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` | Pipeline sequencial + eventos `IProgress` ou delegates |
| `src/PrinterInstall.App/PrinterInstall.App.csproj` | WPF executável |
| `src/PrinterInstall.App/App.xaml` / `App.xaml.cs` | Arranque + DI |
| `src/PrinterInstall.App/Views/LoginWindow.xaml` | Login |
| `src/PrinterInstall.App/Views/MainWindow.xaml` | Lista de alvos + parâmetros + log |
| `src/PrinterInstall.App/ViewModels/LoginViewModel.cs` | |
| `src/PrinterInstall.App/ViewModels/MainViewModel.cs` | |
| `src/PrinterInstall.App/appsettings.json` | `DomainName` por defeito `preventsenior.local` |
| `tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj` | xUnit + Moq |

---

### Task 1: Solução e projectos .NET

**Files:**
- Create: `PrinterInstall.sln`
- Create: `src/PrinterInstall.Core/PrinterInstall.Core.csproj`
- Create: `src/PrinterInstall.App/PrinterInstall.App.csproj`
- Create: `tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj`

- [ ] **Step 1: Criar pastas e solução**

Run (a partir da raiz do repositório `Printer Install 2`):

```powershell
dotnet new sln -n PrinterInstall
dotnet new classlib -n PrinterInstall.Core -o src/PrinterInstall.Core -f net8.0-windows
dotnet new wpf -n PrinterInstall.App -o src/PrinterInstall.App -f net8.0-windows
dotnet new xunit -n PrinterInstall.Core.Tests -o tests/PrinterInstall.Core.Tests -f net8.0
dotnet sln PrinterInstall.sln add src/PrinterInstall.Core/PrinterInstall.Core.csproj src/PrinterInstall.App/PrinterInstall.App.csproj tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj
dotnet add src/PrinterInstall.App/PrinterInstall.App.csproj reference src/PrinterInstall.Core/PrinterInstall.Core.csproj
dotnet add tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj reference src/PrinterInstall.Core/PrinterInstall.Core.csproj
```

Expected: comandos terminam com código 0.

- [ ] **Step 2: Ajustar test project para Windows**

Editar `tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj` e substituir `<TargetFramework>net8.0</TargetFramework>` por `<TargetFramework>net8.0-windows</TargetFramework>` para alinhar com APIs Windows usadas indirectamente.

- [ ] **Step 3: Pacotes NuGet Core**

Run:

```powershell
dotnet add src/PrinterInstall.Core/PrinterInstall.Core.csproj package System.Management.Automation --version 7.4.6
dotnet add src/PrinterInstall.Core/PrinterInstall.Core.csproj package Microsoft.Management.Infrastructure --version 3.0.0
dotnet add src/PrinterInstall.Core/PrinterInstall.Core.csproj package System.DirectoryServices.Protocols --version 8.0.0
```

- [ ] **Step 4: Pacotes App e testes**

Run:

```powershell
dotnet add src/PrinterInstall.App/PrinterInstall.App.csproj package CommunityToolkit.Mvvm --version 8.2.2
dotnet add src/PrinterInstall.App/PrinterInstall.App.csproj package Microsoft.Extensions.Hosting --version 8.0.0
dotnet add tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj package Moq --version 4.20.70
```

- [ ] **Step 5: Compilar solução**

Run: `dotnet build PrinterInstall.sln -c Release`

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add PrinterInstall.sln src/PrinterInstall.Core/ src/PrinterInstall.App/ tests/PrinterInstall.Core.Tests/
git commit -m "chore: scaffold PrinterInstall solution and projects"
```

---

### Task 2: Modelos e catálogo (sem IO)

**Files:**
- Create: `src/PrinterInstall.Core/Models/PrinterBrand.cs`
- Create: `src/PrinterInstall.Core/Models/TargetMachineState.cs`
- Create: `src/PrinterInstall.Core/Models/TcpPrinterProtocol.cs`
- Create: `src/PrinterInstall.Core/Models/PrinterDeploymentRequest.cs`
- Create: `src/PrinterInstall.Core/Catalog/PrinterCatalog.cs`
- Test: `tests/PrinterInstall.Core.Tests/Catalog/PrinterCatalogTests.cs`

- [ ] **Step 1: Escrever teste que falha para `GetExpectedDriverName`**

Criar `tests/PrinterInstall.Core.Tests/Catalog/PrinterCatalogTests.cs`:

```csharp
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Catalog;

public class PrinterCatalogTests
{
    [Fact]
    public void GetExpectedDriverName_Epson_ReturnsUniversalName()
    {
        var name = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Epson);
        Assert.Equal("EPSON Universal Print Driver", name);
    }

    [Fact]
    public void GetModels_ContainsAtLeastOnePerBrand()
    {
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Epson));
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Lexmark));
        Assert.NotEmpty(PrinterCatalog.GetModels(PrinterBrand.Gainscha));
    }
}
```

- [ ] **Step 2: Executar teste — deve falhar**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~PrinterCatalogTests" -v n`

Expected: erro de tipo não encontrado / compilação falha.

- [ ] **Step 3: Implementação mínima**

`src/PrinterInstall.Core/Models/PrinterBrand.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public enum PrinterBrand
{
    Epson,
    Lexmark,
    Gainscha
}
```

`src/PrinterInstall.Core/Models/TargetMachineState.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public enum TargetMachineState
{
    Pending,
    ContactingRemote,
    ValidatingDriver,
    Configuring,
    CompletedSuccess,
    AbortedDriverMissing,
    Error
}
```

`src/PrinterInstall.Core/Models/TcpPrinterProtocol.cs`:

```csharp
namespace PrinterInstall.Core.Models;

public enum TcpPrinterProtocol
{
    Raw,
    Lpr,
    Ipp
}
```

`src/PrinterInstall.Core/Models/PrinterDeploymentRequest.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Models;

public sealed class PrinterDeploymentRequest
{
    public required IReadOnlyList<string> TargetComputerNames { get; init; }
    public required PrinterBrand Brand { get; init; }
    public required string SelectedModelId { get; init; }
    public required string DisplayName { get; init; }
    public required string PrinterHostAddress { get; init; }
    public required int PortNumber { get; init; }
    public required TcpPrinterProtocol Protocol { get; init; }
    public required NetworkCredential DomainCredential { get; init; }
}
```

`src/PrinterInstall.Core/Catalog/PrinterCatalog.cs`:

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Catalog;

public static class PrinterCatalog
{
    private static readonly IReadOnlyDictionary<PrinterBrand, string> DriverNames = new Dictionary<PrinterBrand, string>
    {
        [PrinterBrand.Epson] = "EPSON Universal Print Driver",
        [PrinterBrand.Gainscha] = "Gainscha GA-2408T",
        [PrinterBrand.Lexmark] = "Lexmark Universal v4 XL"
    };

    private static readonly IReadOnlyDictionary<PrinterBrand, IReadOnlyList<(string Id, string DisplayName)>> Models =
        new Dictionary<PrinterBrand, IReadOnlyList<(string Id, string DisplayName)>>
        {
            [PrinterBrand.Epson] = new[] { ("epson-default", "Epson (Universal)") },
            [PrinterBrand.Lexmark] = new[] { ("lexmark-default", "Lexmark (Universal v4 XL)") },
            [PrinterBrand.Gainscha] = new[] { ("gainscha-default", "Gainscha (GA-2408T)") }
        };

    public static string GetExpectedDriverName(PrinterBrand brand) => DriverNames[brand];

    public static IReadOnlyList<(string Id, string DisplayName)> GetModels(PrinterBrand brand) => Models[brand];
}
```

- [ ] **Step 4: Executar testes — deve passar**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~PrinterCatalogTests" -v n`

Expected: Passed!

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Models/ src/PrinterInstall.Core/Catalog/ tests/PrinterInstall.Core.Tests/Catalog/
git commit -m "feat(core): add printer brand models and static catalog"
```

---

### Task 3: `DriverNameMatcher` e lista de computadores

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs`
- Create: `src/PrinterInstall.Core/Validation/ComputerNameListParser.cs`
- Create: `src/PrinterInstall.Core/Validation/ComputerNameValidator.cs`
- Test: `tests/PrinterInstall.Core.Tests/Drivers/DriverNameMatcherTests.cs`
- Test: `tests/PrinterInstall.Core.Tests/Validation/ComputerNameListParserTests.cs`

- [ ] **Step 1: Teste que falha — `DriverNameMatcher`**

`tests/PrinterInstall.Core.Tests/Drivers/DriverNameMatcherTests.cs`:

```csharp
using PrinterInstall.Core.Drivers;

namespace PrinterInstall.Core.Tests.Drivers;

public class DriverNameMatcherTests
{
    [Fact]
    public void IsDriverInstalled_CaseInsensitive_Matches()
    {
        var installed = new[] { "EPSON Universal Print Driver", "Other" };
        Assert.True(DriverNameMatcher.IsDriverInstalled(installed, "epson universal print driver"));
    }

    [Fact]
    public void IsDriverInstalled_Missing_ReturnsFalse()
    {
        Assert.False(DriverNameMatcher.IsDriverInstalled(Array.Empty<string>(), "Lexmark Universal v4 XL"));
    }
}
```

- [ ] **Step 2: Run test — FAIL**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~DriverNameMatcherTests" -v n`

- [ ] **Step 3: Implementação**

`src/PrinterInstall.Core/Drivers/DriverNameMatcher.cs`:

```csharp
namespace PrinterInstall.Core.Drivers;

public static class DriverNameMatcher
{
    public static bool IsDriverInstalled(IEnumerable<string> installedDriverNames, string expectedDriverName)
    {
        var expected = expectedDriverName.Trim();
        foreach (var name in installedDriverNames)
        {
            if (string.Equals(name.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
```

`src/PrinterInstall.Core/Validation/ComputerNameListParser.cs`:

```csharp
namespace PrinterInstall.Core.Validation;

public static class ComputerNameListParser
{
    public static IReadOnlyList<string> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}
```

`src/PrinterInstall.Core/Validation/ComputerNameValidator.cs`:

```csharp
using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Validation;

public static partial class ComputerNameValidator
{
    [GeneratedRegex(@"^[\w.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    public static bool IsPlausibleComputerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 253)
            return false;
        return HostPattern().IsMatch(name.Trim());
    }
}
```

- [ ] **Step 4: Testes de parser**

`tests/PrinterInstall.Core.Tests/Validation/ComputerNameListParserTests.cs`:

```csharp
using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class ComputerNameListParserTests
{
    [Fact]
    public void Parse_Multiline_TrimsAndSkipsEmpty()
    {
        var list = ComputerNameListParser.Parse(" pc1 \r\n\r\npc2.preventsenior.local ");
        Assert.Equal(new[] { "pc1", "pc2.preventsenior.local" }, list);
    }
}
```

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj -v n`

Expected: todos passam.

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/ src/PrinterInstall.Core/Validation/ tests/PrinterInstall.Core.Tests/Drivers/ tests/PrinterInstall.Core.Tests/Validation/
git commit -m "feat(core): driver name matching and computer name parsing"
```

---

### Task 4: Validação LDAP (interface + implementação)

**Files:**
- Create: `src/PrinterInstall.Core/Auth/ILdapCredentialValidator.cs`
- Create: `src/PrinterInstall.Core/Auth/LdapCredentialValidator.cs`
- Create: `src/PrinterInstall.Core/Auth/LdapValidationResult.cs`
- Test: `tests/PrinterInstall.Core.Tests/Auth/LdapCredentialValidatorTests.cs` (usa Moq só se injectar socket — aqui teste de contrato com `FakeLdapCredentialValidator` duplicado é proibido; usar teste de integração opcional OU testar apenas resultado de mapeamento de erro)

Para cumprir TDD sem DC real, extrair lógica pura `MapLdapException` não é necessária; o plano usa **teste de unidade** com uma **subclasse de teste** que não existe — melhor: **interface** `ILdapCredentialValidator` e **Fake** no teste:

`tests/PrinterInstall.Core.Tests/Auth/FakeLdapCredentialValidator.cs`:

```csharp
using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public sealed class FakeLdapCredentialValidator : ILdapCredentialValidator
{
    public bool NextResult { get; set; } = true;
    public string? NextError { get; set; }

    public Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(NextResult
            ? LdapValidationResult.Success()
            : LdapValidationResult.Failure(NextError ?? "fail"));
    }
}
```

Implementação real em `LdapCredentialValidator.cs` (bind LDAP simples):

`src/PrinterInstall.Core/Auth/ILdapCredentialValidator.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Auth;

public interface ILdapCredentialValidator
{
    Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default);
}
```

`src/PrinterInstall.Core/Auth/LdapValidationResult.cs`:

```csharp
namespace PrinterInstall.Core.Auth;

public sealed class LdapValidationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static LdapValidationResult Success() => new() { IsSuccess = true };

    public static LdapValidationResult Failure(string message) => new()
    {
        IsSuccess = false,
        ErrorMessage = message
    };
}
```

`src/PrinterInstall.Core/Auth/LdapCredentialValidator.cs`:

```csharp
using System.DirectoryServices.Protocols;
using System.Net;

namespace PrinterInstall.Core.Auth;

public sealed class LdapCredentialValidator : ILdapCredentialValidator
{
    public Task<LdapValidationResult> ValidateAsync(string domainName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return Task.FromResult(LdapValidationResult.Failure("Domain name is required."));

        try
        {
            var identifier = new LdapDirectoryIdentifier(domainName.Trim(), 389, 636, true);
            using var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
                Credential = credential,
                SessionOptions =
                {
                    ProtocolVersion = 3
                }
            };
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            connection.Bind();
            return Task.FromResult(LdapValidationResult.Success());
        }
        catch (LdapException ex)
        {
            return Task.FromResult(LdapValidationResult.Failure($"LDAP error: {ex.Message} (0x{ex.ErrorCode:X})"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(LdapValidationResult.Failure(ex.Message));
        }
    }
}
```

- [ ] **Step 1: Teste do Fake (smoke)**

`tests/PrinterInstall.Core.Tests/Auth/LdapCredentialValidatorContractTests.cs`:

```csharp
using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Tests.Auth;

public class LdapCredentialValidatorContractTests
{
    [Fact]
    public async Task Fake_ReturnsSuccess()
    {
        var fake = new FakeLdapCredentialValidator { NextResult = true };
        var r = await fake.ValidateAsync("preventsenior.local", new NetworkCredential("u", "p"));
        Assert.True(r.IsSuccess);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~LdapCredentialValidatorContractTests" -v n`

- [ ] **Step 3: Adicionar ficheiros Core Auth e implementação**

Copiar código dos blocos acima para os ficheiros indicados.

- [ ] **Step 4: Teste manual documentado (não automatizado)**

Após implementação, validar contra DC real com conta de domínio (fora do CI). Documentar em comentário no plano de release: credenciais de teste nunca em repositório.

- [ ] **Step 5: Commit**

```bash
git add src/PrinterInstall.Core/Auth/ tests/PrinterInstall.Core.Tests/Auth/
git commit -m "feat(core): LDAP credential validation for domain login"
```

---

### Task 5: Operações remotas WinRM (PowerShell)

**Files:**
- Create: `src/PrinterInstall.Core/Remote/IPowerShellInvoker.cs`
- Create: `src/PrinterInstall.Core/Remote/PowerShellInvoker.cs`
- Create: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`
- Create: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`
- Test: `tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsTests.cs`

Para testabilidade sem rede, definir `IPowerShellInvoker` com método que recebe **credencial** e **script a executar no runspace remoto** (sem embutir palavras-passe em strings de script).

`src/PrinterInstall.Core/Remote/IPowerShellInvoker.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IPowerShellInvoker
{
    Task<IReadOnlyList<string>> InvokeOnRemoteRunspaceAsync(string computerName, NetworkCredential credential, string innerScript, CancellationToken cancellationToken = default);
}
```

`src/PrinterInstall.Core/Remote/PowerShellInvoker.cs` — **implementação segura:** `WSManConnectionInfo` com URI `http://{computerName}:5985/wsman` (ou `https://{computerName}:5986/wsman` quando a app usar HTTPS), `ShellUri` `http://schemas.microsoft.com/powershell/Microsoft.PowerShell`, `PSCredential` construído com `SecureString` (copiar `NetworkCredential.Password` carácter a carácter para `SecureString` e `MakeReadOnly()`), `RunspaceFactory.CreateRunspace(connectionInfo)`, `runspace.Open()` ou `OpenAsync()`, `PowerShell.Create()` com `ps.Runspace = runspace`, `ps.AddScript(innerScript)`, `Invoke`, devolver linhas de saída. Em erros, agregar `ps.Streams.Error` (sem registar `Password`).

`src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemotePrinterOperations
{
    Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default);

    Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default);

    Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default);
}
```

`src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` — chama apenas `innerScript` no alvo (sem credenciais no texto):

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class WinRmRemotePrinterOperations : IRemotePrinterOperations
{
    private readonly IPowerShellInvoker _invoker;

    public WinRmRemotePrinterOperations(IPowerShellInvoker invoker)
    {
        _invoker = invoker;
    }

    public Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        const string inner = "Get-PrinterDriver | Select-Object -ExpandProperty Name";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-PrinterPort -Name '{Escape(portName)}' -PrinterHostAddress '{Escape(printerHostAddress)}' -PortNumber {portNumber}";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
    {
        var inner = $"Add-Printer -Name '{Escape(printerName)}' -DriverName '{Escape(driverName)}' -PortName '{Escape(portName)}'";
        return _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, inner, cancellationToken);
    }

    private static string Escape(string s) => s.Replace("'", "''");
}
```

- [ ] **Step 1:** Implementar `IPowerShellInvoker`, `PowerShellInvoker` e `WinRmRemotePrinterOperations` conforme acima.

- [ ] **Step 2:** Teste com Moq de `IPowerShellInvoker` verificando que `GetInstalledDriverNamesAsync` chama `InvokeOnRemoteRunspaceAsync` com `innerScript` contendo `Get-PrinterDriver`.

`tests/PrinterInstall.Core.Tests/Remote/WinRmRemotePrinterOperationsTests.cs`:

```csharp
using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class WinRmRemotePrinterOperationsTests
{
    [Fact]
    public async Task GetInstalledDriverNamesAsync_InvokesScriptContainingGetPrinterDriver()
    {
        var mock = new Mock<IPowerShellInvoker>();
        mock.Setup(m => m.InvokeOnRemoteRunspaceAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "EPSON Universal Print Driver" });

        var sut = new WinRmRemotePrinterOperations(mock.Object);
        var cred = new NetworkCredential("DOM\\admin", "x");
        var names = await sut.GetInstalledDriverNamesAsync("pc1", cred);

        Assert.Single(names);
        mock.Verify(m => m.InvokeOnRemoteRunspaceAsync("pc1", cred, It.Is<string>(s => s.Contains("Get-PrinterDriver")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3:** Run: `dotnet test tests/PrinterInstall.Core.Tests/PrinterInstall.Core.Tests.csproj --filter "FullyQualifiedName~WinRmRemotePrinterOperationsTests" -v n`

- [ ] **Step 4:** Commit

```bash
git add src/PrinterInstall.Core/Remote/ tests/PrinterInstall.Core.Tests/Remote/
git commit -m "feat(core): WinRM remote printer operations via PowerShell abstraction"
```

---

### Task 6: Operações remotas CIM (recurso)

**Files:**
- Create: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`
- Implementa `IRemotePrinterOperations` listando `Win32_PrinterDriver` via `CimSession` com `Credential` (DCOM).

Esboço de `GetInstalledDriverNamesAsync`:

```csharp
using Microsoft.Management.Infrastructure;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class CimRemotePrinterOperations : IRemotePrinterOperations
{
    public async Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var options = new CimSessionOptions { };
            using var session = CimSession.Create(computerName, options);
            // Nota: CimSession.Create overload com credenciais — usar:
            // CimSession.Create(computerName, credential.UserName, credential.SecurePassword, ...)
            var instances = session.QueryInstances(@"root\cimv2", "WQL", "SELECT Name FROM Win32_PrinterDriver");
            return instances.Select(i => i.CimInstanceProperties["Name"]?.Value?.ToString() ?? "").Where(n => n.Length > 0).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CIM path: use WinRM for port creation or implement WMI Win32_TCPIPPrinterPort.");

    public Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CIM path: use WinRM for Add-Printer or implement WMI Win32_Printer.");
}
```

**Correcção de plano:** para cumprir spec, CIM deve permitir **validação de driver**; criação de porta/fila pode exigir WinRM. Definir interface estreita:

`src/PrinterInstall.Core/Remote/IRemoteDriverQuery.cs` com `GetInstalledDriverNamesAsync` apenas para CIM.

Refactor: `IRemotePrinterOperations` herda ou dividir em `IDriverQuery` + `IPrinterConfigurator`. Para YAGNI neste plano: manter `CimRemotePrinterOperations` **só** com `GetInstalledDriverNamesAsync` implementado; `CompositeRemotePrinterOperations` usa CIM só para listagem se WinRM falhar na listagem, e **obriga** WinRM para `Add-PrinterPort` / `Add-Printer` — se WinRM indisponível e só CIM disponível, retornar erro claro: *"Configuração de impressora requer WinRM; apenas validação de driver foi possível via WMI."*

- [ ] **Step 1:** Implementar `CimRemotePrinterOperations.GetInstalledDriverNamesAsync` com `CimSession.Create` e credenciais documentadas em [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.management.infrastructure.cimsession).

- [ ] **Step 2:** Teste de unidade com `CimSession` não mockável facilmente — extrair `ICimDriverEnumerator` interface e fake para testes.

- [ ] **Step 3:** Commit

```bash
git add src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs
git commit -m "feat(core): CIM fallback for listing printer drivers"
```

---

### Task 7: `CompositeRemotePrinterOperations` + orquestrador

**Files:**
- Create: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`
- Create: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`
- Create: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentEvents.cs`
- Test: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`

`CompositeRemotePrinterOperations`:

- `GetInstalledDriverNamesAsync`: tentar `WinRmRemotePrinterOperations`; em falha, `CimRemotePrinterOperations`.
- `CreateTcpPrinterPortAsync` / `AddPrinterAsync`: apenas `WinRmRemotePrinterOperations`; se falhar com erro de canal, propagar mensagem com texto de pré-requisitos (spec §4).

`PrinterDeploymentOrchestrator` (pseudo-código completo no ficheiro):

```csharp
public sealed class PrinterDeploymentOrchestrator
{
    public async Task RunAsync(PrinterDeploymentRequest request, IProgress<DeploymentProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var computer in request.TargetComputerNames)
        {
            progress.Report(new(computer, TargetMachineState.ContactingRemote, "Connecting..."));
            var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken);
            progress.Report(new(computer, TargetMachineState.ValidatingDriver, "Checking driver..."));
            var expected = PrinterCatalog.GetExpectedDriverName(request.Brand);
            if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
            {
                progress.Report(new(computer, TargetMachineState.AbortedDriverMissing, $"Driver not installed: {expected}"));
                continue;
            }
            var portName = $"IP_{request.PrinterHostAddress}_{request.PortNumber}";
            progress.Report(new(computer, TargetMachineState.Configuring, "Creating port..."));
            await _remote.CreateTcpPrinterPortAsync(computer, request.DomainCredential, portName, request.PrinterHostAddress, request.PortNumber, "RAW", cancellationToken);
            progress.Report(new(computer, TargetMachineState.Configuring, "Adding printer..."));
            await _remote.AddPrinterAsync(computer, request.DomainCredential, request.DisplayName, expected, portName, cancellationToken);
            progress.Report(new(computer, TargetMachineState.CompletedSuccess, "Done"));
        }
    }
}
```

Definir `DeploymentProgressEvent` com propriedades `ComputerName`, `TargetMachineState`, `Message`.

- [ ] **Step 1:** Escrever teste com `IRemotePrinterOperations` Moq que devolve drivers contendo nome esperado e verifica ordem de chamadas.

- [ ] **Step 2:** Implementar orquestrador e eventos.

- [ ] **Step 3:** `dotnet test` no projecto de testes.

- [ ] **Step 4:** Commit

```bash
git add src/PrinterInstall.Core/Orchestration/ src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs tests/PrinterInstall.Core.Tests/Orchestration/
git commit -m "feat(core): composite remote gateway and sequential orchestrator"
```

---

### Task 8: WPF App — DI, definições, janelas

**Files:**
- Create: `src/PrinterInstall.App/appsettings.json`
- Modify: `src/PrinterInstall.App/App.xaml.cs`
- Create: `src/PrinterInstall.App/ViewModels/LoginViewModel.cs`
- Create: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`
- Create: `src/PrinterInstall.App/Views/LoginWindow.xaml`
- Create: `src/PrinterInstall.App/Views/MainWindow.xaml`

`appsettings.json`:

```json
{
  "DomainName": "preventsenior.local"
}
```

`App.xaml.cs` regista `ILdapCredentialValidator`, `IRemotePrinterOperations` (composite), `PrinterDeploymentOrchestrator`, `LoginViewModel`, `MainViewModel`.

Login: ao sucesso LDAP, abrir `MainWindow` e passar `NetworkCredential` para `MainViewModel` (via serviço de sessão `ISessionContext`).

`ISessionContext` (`src/PrinterInstall.App/Services/SessionContext.cs`):

```csharp
using System.Net;

namespace PrinterInstall.App.Services;

public interface ISessionContext
{
    NetworkCredential? Credential { get; set; }
    string DomainName { get; set; }
}
```

MainViewModel: propriedades observáveis para texto de computadores, marca seleccionada, modelo, nome de exibição, IP, porta, protocolo; coleção `ObservableCollection<TargetRowViewModel>` com estado; comando `Deploy` chama `Task.Run` com `PrinterDeploymentOrchestrator` e `IProgress` que faz `Application.Current.Dispatcher.Invoke` para actualizar UI.

- [ ] **Step 1:** Criar `LoginWindow` + `MainWindow` XAML mínimos e ViewModels com `ObservableObject`.

- [ ] **Step 2:** Wire `Host.CreateApplicationBuilder` em `App.xaml.cs` (padrão .NET Generic Host para WPF — ver documentação Microsoft 2024).

- [ ] **Step 3:** Build: `dotnet build PrinterInstall.sln -c Release`

- [ ] **Step 4:** Commit

```bash
git add src/PrinterInstall.App/
git commit -m "feat(app): WPF shell, login, main form, DI host"
```

---

### Task 9: Testes de integração mockados e checklist manual

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs` (completo com Moq)

- [ ] **Step 1:** Implementar teste onde `IRemotePrinterOperations` devolve lista sem driver → estado `AbortedDriverMissing`.

- [ ] **Step 2:** Implementar teste com driver presente → `CreateTcpPrinterPortAsync` e `AddPrinterAsync` chamados uma vez cada.

- [ ] **Step 3:** Checklist manual (documentar no README do developer ou comentário no plano): executar app em VM de domínio, validar LDAP, validar WinRM `Enter-PSSession`, validar nomes `Get-PrinterDriver` coincidem com tabela da spec.

- [ ] **Step 4:** Commit

```bash
git add tests/PrinterInstall.Core.Tests/
git commit -m "test(core): orchestrator scenarios with mocked remote operations"
```

---

## Cobertura da spec (auto-revisão)

| Secção spec | Tarefa(s) |
|-------------|-----------|
| §3.1 Login + LDAP | Task 4, Task 8 |
| §3.2 Lista manual | Task 3, Task 8 (`ComputerNameListParser` na UI) |
| §3.3 Marca/modelo/TCP | Task 2, Task 8 |
| §3.4 Validação driver | Task 2–3, Task 5–7 |
| §3.5 Sequencial + UI + log | Task 7–8 |
| §4 WinRM + CIM | Task 5–6–7 |
| §7 Estados/erros | Task 7 (`TargetMachineState`), Task 8 |
| §8 Testes | Tasks 2–3–5–7–9 |

**Placeholders:** nenhum `TBD` propositado; Task 6 nota explícita de limite CIM vs WinRM para criação de fila.

**Consistência de tipos:** `PrinterBrand`, `PrinterDeploymentRequest`, `TargetMachineState`, `ILdapCredentialValidator`, `IRemotePrinterOperations`, `PrinterDeploymentOrchestrator` usados de forma coerente em todas as tarefas.

---

## Nota sobre worktree

A skill *writing-plans* sugere worktree dedicado; opcionalmente executar `git worktree add` antes de implementar para isolar branches.

---

**Plano concluído e gravado em `docs/superpowers/plans/2026-04-16-printer-remote-config.md`. Duas opções de execução:**

**1. Subagent-Driven (recomendado)** — despacho de um subagente por tarefa, revisão entre tarefas, iteração rápida.

**2. Inline Execution** — executar tarefas nesta sessão com *executing-plans*, lotes com pontos de verificação.

**Qual abordagem prefere?**
