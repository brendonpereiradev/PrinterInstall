# Remote UAC Elevation Bypass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Contornar automaticamente a filtragem de token UAC remoto em PCs de domínio, executando mutações privilegiadas via schtasks efémera (`/RU SYSTEM /RL HIGHEST`) com preflight IPC$ + retry em Access Denied.

**Architecture:** Novos helpers em `PrinterInstall.Core/Remote` (`AccessDeniedDetector`, `RemoteHostSessionFactory`, `RemoteElevatedScriptBuilder`, `ElevatedRemoteProcessRunner`) integrados em `CimRemotePrinterOperations` sem alterar `IRemotePrinterOperations`. Leituras permanecem WMI directo; mutações usam caminho directo ou elevado conforme preflight/fallback.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, `System.Management` (WMI), SMB (`WNetAddConnection2`), xUnit, Moq.

**Spec:** `docs/superpowers/specs/2026-06-24-remote-uac-elevation-bypass-design.md`

**Note on commits:** O repositório só faz commit quando pedido explicitamente. Passos "Commit" são opcionais.

---

## File map

| Responsibility | Action | Path |
|----------------|--------|------|
| Access-denied detection | **Create** | `src/PrinterInstall.Core/Remote/AccessDeniedDetector.cs` |
| Session state | **Create** | `src/PrinterInstall.Core/Remote/RemoteHostSession.cs` |
| IPC$/WMI preflight | **Create** | `src/PrinterInstall.Core/Remote/RemoteHostSessionFactory.cs` |
| Elevated PS scripts | **Create** | `src/PrinterInstall.Core/Remote/RemoteElevatedScriptBuilder.cs` |
| WMI process abstraction | **Create** | `src/PrinterInstall.Core/Remote/IRemoteWmiProcessRunner.cs` |
| schtasks runner | **Create** | `src/PrinterInstall.Core/Remote/ElevatedRemoteProcessRunner.cs` |
| Remote ops integration | **Modify** | `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` |
| Install script flag | **Modify** | `src/PrinterInstall.Core/Remote/WmiPrinterOperationsCore.cs` |
| DI | **Modify** | `src/PrinterInstall.App/App.xaml.cs` |
| Docs | **Modify** | `docs/conexao-remota.md` |
| Tests | **Create** | `tests/PrinterInstall.Core.Tests/Remote/AccessDeniedDetectorTests.cs` |
| Tests | **Create** | `tests/PrinterInstall.Core.Tests/Remote/RemoteElevatedScriptBuilderTests.cs` |
| Tests | **Create** | `tests/PrinterInstall.Core.Tests/Remote/ElevatedRemoteProcessRunnerTests.cs` |
| Tests | **Create** | `tests/PrinterInstall.Core.Tests/Remote/RemoteHostSessionFactoryTests.cs` |
| Tests | **Create** | `tests/PrinterInstall.Core.Tests/Remote/CimRemotePrinterOperationsElevationTests.cs` |
| Tests | **Modify** | `tests/PrinterInstall.Core.Tests/Remote/WmiPrinterOperationsCoreTests.cs` |

---

### Task 1: AccessDeniedDetector

**Files:**
- Create: `src/PrinterInstall.Core/Remote/AccessDeniedDetector.cs`
- Create: `tests/PrinterInstall.Core.Tests/Remote/AccessDeniedDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/PrinterInstall.Core.Tests/Remote/AccessDeniedDetectorTests.cs`:

```csharp
using System.Management;
using System.Runtime.InteropServices;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class AccessDeniedDetectorTests
{
    [Fact]
    public void IsAccessDenied_UnauthorizedAccessException_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new UnauthorizedAccessException()));
    }

    [Fact]
    public void IsAccessDenied_InnerUnauthorizedAccess_ReturnsTrue()
    {
        var ex = new InvalidOperationException("wrap", new UnauthorizedAccessException());
        Assert.True(AccessDeniedDetector.IsAccessDenied(ex));
    }

    [Fact]
    public void IsAccessDenied_MessageAcessoNegado_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new Exception("Falha: Acesso negado.")));
    }

    [Fact]
    public void IsAccessDenied_MessageAccessIsDenied_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new Exception("Access is denied.")));
    }

    [Fact]
    public void IsAccessDenied_WmiReturnValue5_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsWmiAccessDeniedReturnValue(5));
        Assert.False(AccessDeniedDetector.IsWmiAccessDeniedReturnValue(0));
    }

    [Fact]
    public void IsAccessDenied_UnrelatedException_ReturnsFalse()
    {
        Assert.False(AccessDeniedDetector.IsAccessDenied(new InvalidOperationException("timeout")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~AccessDeniedDetectorTests" -v n
```

Expected: FAIL — `AccessDeniedDetector` not found.

- [ ] **Step 3: Implement AccessDeniedDetector**

Create `src/PrinterInstall.Core/Remote/AccessDeniedDetector.cs`:

```csharp
using System.Management;

namespace PrinterInstall.Core.Remote;

public static class AccessDeniedDetector
{
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    public static bool IsAccessDenied(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException)
                return true;

            if (current is ManagementException mgmt &&
                mgmt.ErrorCode == ManagementStatus.AccessDenied)
                return true;

            if (current.HResult == HResultAccessDenied)
                return true;

            var message = current.Message;
            if (message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Access Denied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsWmiAccessDeniedReturnValue(uint returnValue) => returnValue == 5;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~AccessDeniedDetectorTests" -v n
```

Expected: `Passed!`

---

### Task 2: RemoteHostSession + probe parsing

**Files:**
- Create: `src/PrinterInstall.Core/Remote/RemoteHostSession.cs`
- Create: `tests/PrinterInstall.Core.Tests/Remote/RemoteHostSessionFactoryTests.cs` (partial — parsing tests only)

- [ ] **Step 1: Write failing parsing tests**

Add to `tests/PrinterInstall.Core.Tests/Remote/RemoteHostSessionFactoryTests.cs`:

```csharp
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemoteHostSessionFactoryTests
{
    [Theory]
    [InlineData("ELEVATION_PROBE>> TRUE", false)]
    [InlineData("ELEVATION_PROBE>> FALSE", true)]
    [InlineData("noise\nELEVATION_PROBE>> FALSE\n", true)]
    public void ParseElevationProbeOutput_DetectsFilteredToken(string output, bool requiresElevated)
    {
        var result = RemoteHostSessionFactory.ParseElevationProbeOutput(output);
        Assert.Equal(requiresElevated, result);
    }

    [Theory]
    [InlineData("PC01", "pc01")]
    [InlineData("  PC01  ", "pc01")]
    public void NormalizeHostKey_IsCaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, RemoteHostSessionFactory.NormalizeHostKey(input));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~RemoteHostSessionFactoryTests" -v n
```

- [ ] **Step 3: Implement RemoteHostSession and static helpers**

Create `src/PrinterInstall.Core/Remote/RemoteHostSession.cs`:

```csharp
namespace PrinterInstall.Core.Remote;

public sealed class RemoteHostSession
{
    public RemoteHostSession(string host, bool requiresElevatedExecution)
    {
        Host = host;
        RequiresElevatedExecution = requiresElevatedExecution;
        PreflightCompleted = true;
    }

    public string Host { get; }
    public bool RequiresElevatedExecution { get; private set; }
    public bool PreflightCompleted { get; }

    public void MarkRequiresElevatedExecution() => RequiresElevatedExecution = true;
}
```

Add static helpers to `RemoteHostSessionFactory.cs` (create file with helpers first; full factory in Task 3):

```csharp
namespace PrinterInstall.Core.Remote;

public sealed class RemoteHostSessionFactory
{
    internal const string ElevationProbeCommand =
        "powershell.exe -NoProfile -Command \"if(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){'ELEVATION_PROBE>> TRUE'}else{'ELEVATION_PROBE>> FALSE'}\"";

    public static string NormalizeHostKey(string host) => host.Trim().ToUpperInvariant();

    public static bool ParseElevationProbeOutput(string output)
    {
        foreach (var line in WmiPrinterOperationsCore.SplitLines(output))
        {
            if (line.Contains("ELEVATION_PROBE>> FALSE", StringComparison.OrdinalIgnoreCase))
                return true;
            if (line.Contains("ELEVATION_PROBE>> TRUE", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        // Probe inconclusive — assume filtered (safe default for domain UAC remote)
        return true;
    }
}
```

- [ ] **Step 4: Run parsing tests — expect PASS**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~RemoteHostSessionFactoryTests" -v n
```

---

### Task 3: RemoteHostSessionFactory (preflight)

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/RemoteHostSessionFactory.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Remote/RemoteHostSessionFactoryTests.cs`

- [ ] **Step 1: Complete factory implementation**

Replace/extend `RemoteHostSessionFactory.cs` (use `IRemoteWmiProcessRunner` if introduced in Task 5):

```csharp
using System.Collections.Concurrent;
using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class RemoteHostSessionFactory
{
    private readonly IRemoteWmiProcessRunner _processRunner;
    private readonly ConcurrentDictionary<string, RemoteHostSession> _cache = new();

    public RemoteHostSessionFactory(IRemoteWmiProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<RemoteHostSession> PrepareAsync(
        string host,
        NetworkCredential credential,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeHostKey(host);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var trimmedHost = host.Trim();
        log?.Report($"Autenticando sessão remota em {trimmedHost} (IPC$)...");

        try
        {
            using (SmbShareConnection.Open(trimmedHost, "IPC$", credential)) { }
            using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential)) { }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não foi possível autenticar sessão SMB em {trimmedHost}. Verifique firewall (445) e permissões de admin.",
                ex);
        }

        try
        {
            var scope = WmiPrinterOperationsCore.CreateRemoteScope(trimmedHost, credential);
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM Win32_PrinterDriver"));
            foreach (ManagementObject mo in searcher.Get())
            {
                mo.Dispose();
                break;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"WMI remoto indisponível em {trimmedHost} (RPC 135, firewall WMI-In).",
                ex);
        }

        var paths = RemoteDriverStagingPaths.Create(trimmedHost);
        var probeLogLocal = paths.LocalLogPath("probe.log");
        var probeCmd = $"cmd.exe /c \"{ElevationProbeCommand} > {probeLogLocal} 2>&1\"";

        using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential))
            Directory.CreateDirectory(paths.UncRoot);

        await _processRunner.RunAsync(trimmedHost, credential, probeCmd, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        string probeText;
        using (SmbShareConnection.Open(trimmedHost, "ADMIN$", credential))
        {
            var uncProbe = paths.UncLogPath("probe.log");
            probeText = File.Exists(uncProbe) ? await File.ReadAllTextAsync(uncProbe, cancellationToken).ConfigureAwait(false) : string.Empty;
            try { Directory.Delete(paths.UncRoot, recursive: true); } catch { /* best effort */ }
        }

        var requiresElevated = ParseElevationProbeOutput(probeText);
        var session = new RemoteHostSession(trimmedHost, requiresElevated);
        _cache[key] = session;

        if (requiresElevated)
            log?.Report($"Token administrativo filtrado detectado em {trimmedHost} — execução elevada temporária");

        return session;
    }
}
```

Create `src/PrinterInstall.Core/Remote/IRemoteWmiProcessRunner.cs`:

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemoteWmiProcessRunner
{
    Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken);
}
```

Make `WmiRemoteProcessRunner` implement `IRemoteWmiProcessRunner`.

- [ ] **Step 2: Add cache hit test (mock-free, uses static parsing only)**

Append to `RemoteHostSessionFactoryTests.cs`:

```csharp
[Fact]
public void ParseElevationProbeOutput_EmptyOutput_AssumesFiltered()
{
    Assert.True(RemoteHostSessionFactory.ParseElevationProbeOutput(string.Empty));
}
```

- [ ] **Step 3: Build Core**

```powershell
dotnet build "src\PrinterInstall.Core\PrinterInstall.Core.csproj" -c Release
```

Expected: `Build succeeded.`

---

### Task 4: RemoteElevatedScriptBuilder

**Files:**
- Create: `src/PrinterInstall.Core/Remote/RemoteElevatedScriptBuilder.cs`
- Create: `tests/PrinterInstall.Core.Tests/Remote/RemoteElevatedScriptBuilderTests.cs`
- Modify: `src/PrinterInstall.Core/Remote/WmiPrinterOperationsCore.cs`
- Modify: `tests/PrinterInstall.Core.Tests/Remote/WmiPrinterOperationsCoreTests.cs`

- [ ] **Step 1: Write failing script builder tests**

Create `tests/PrinterInstall.Core.Tests/Remote/RemoteElevatedScriptBuilderTests.cs`:

```csharp
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemoteElevatedScriptBuilderTests
{
    [Fact]
    public void BuildCreateTcpPortScript_EmitsResultMarker()
    {
        var script = RemoteElevatedScriptBuilder.BuildCreateTcpPortScript(
            "IP_10.0.0.5", "10.0.0.5", 9100, "RAW");
        Assert.Contains("Add-PrinterPort", script, StringComparison.Ordinal);
        Assert.Contains("RESULT>> OK", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAddPrinterScript_EscapesQuotes()
    {
        var script = RemoteElevatedScriptBuilder.BuildAddPrinterScript(
            "Recepção L'Impressora", "Lexmark Universal v4 XL", "IP_10.0.0.5");
        Assert.Contains("Recepção L''Impressora", script, StringComparison.Ordinal);
        Assert.Contains("Add-Printer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapWithResultHandling_IncludesTryCatch()
    {
        var inner = "Write-Output 'hello'";
        var wrapped = RemoteElevatedScriptBuilder.WrapWithResultHandling(inner);
        Assert.Contains("$ErrorActionPreference = 'Stop'", wrapped, StringComparison.Ordinal);
        Assert.Contains("RESULT>> FAIL", wrapped, StringComparison.Ordinal);
        Assert.Contains("RESULT>> OK", wrapped, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~RemoteElevatedScriptBuilderTests" -v n
```

- [ ] **Step 3: Implement RemoteElevatedScriptBuilder**

Create `src/PrinterInstall.Core/Remote/RemoteElevatedScriptBuilder.cs`:

```csharp
using System.Globalization;

namespace PrinterInstall.Core.Remote;

public static class RemoteElevatedScriptBuilder
{
    public static string WrapWithResultHandling(string innerScriptBody) =>
$@"$ErrorActionPreference = 'Stop'
try {{
{innerScriptBody}
    Write-Output 'RESULT>> OK'
    exit 0
}} catch {{
    Write-Output ('RESULT>> FAIL ' + $_.Exception.Message)
    exit 1
}}";

    public static string BuildCreateTcpPortScript(string portName, string hostAddress, int portNumber, string protocol)
    {
        var port = EscapePs(portName);
        var host = EscapePs(hostAddress);
        var body = protocol.Equals("LPR", StringComparison.OrdinalIgnoreCase)
            ? $@"
    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} -PortMonitor 'LPR Port Monitor' | Out-Null"
            : $@"
    if (Get-PrinterPort -Name '{port}' -ErrorAction SilentlyContinue) {{ return }}
    Add-PrinterPort -Name '{port}' -PrinterHostAddress '{host}' -PortNumber {portNumber.ToString(CultureInfo.InvariantCulture)} | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildAddPrinterScript(string printerName, string driverName, string portName)
    {
        var n = EscapePs(printerName);
        var d = EscapePs(driverName);
        var p = EscapePs(portName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    if (Get-Printer -Name '{n}' -ErrorAction SilentlyContinue) {{ return }}
    Add-Printer -Name '{n}' -DriverName '{d}' -PortName '{p}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRemovePrinterScript(string printerName)
    {
        var n = EscapePs(printerName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    Remove-Printer -Name '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRemoveTcpPortScript(string portName)
    {
        var p = EscapePs(portName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    if (Get-PrinterPort -Name '{p}' -ErrorAction SilentlyContinue) {{
        Remove-PrinterPort -Name '{p}' -ErrorAction Stop | Out-Null
    }}";
        return WrapWithResultHandling(body);
    }

    public static string BuildPrintTestPageScript(string printerQueueName)
    {
        var n = EscapePs(printerQueueName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    Print-TestPage -PrinterName '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    public static string BuildRenamePrinterScript(string currentName, string newName)
    {
        var c = EscapePs(currentName);
        var n = EscapePs(newName);
        var body = $@"
    Import-Module PrintManagement -ErrorAction Stop
    $null = Get-Printer -Name '{c}' -ErrorAction Stop
    Rename-Printer -Name '{c}' -NewName '{n}' -ErrorAction Stop | Out-Null";
        return WrapWithResultHandling(body);
    }

    private static string EscapePs(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
```

- [ ] **Step 4: Add skipRunAsBlock to BuildInstallerScript**

In `WmiPrinterOperationsCore.cs`, change signature:

```csharp
public static string BuildInstallerScript(string infLocal, string driverName, string logPath, bool skipRunAsBlock = false)
```

Wrap the existing `-Verb RunAs` block:

```csharp
if (-not $skipRunAsBlock) {{
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {{
        // ... existing RunAs block unchanged ...
    }}
}}
```

When `skipRunAsBlock` is `true`, omit the entire administrator check block.

- [ ] **Step 5: Add test for skipRunAsBlock**

Add to `WmiPrinterOperationsCoreTests.cs`:

```csharp
[Fact]
public void BuildInstallerScript_SkipRunAsBlock_OmitsElevationRelaunch()
{
    var script = WmiPrinterOperationsCore.BuildInstallerScript(
        @"C:\Temp\pkg\LMUX1l50.inf",
        "Lexmark Universal v4 XL",
        @"C:\Temp\pkg\install.log",
        skipRunAsBlock: true);

    Assert.DoesNotContain("-Verb RunAs", script, StringComparison.Ordinal);
    Assert.Contains("$pnpOutput = & pnputil.exe", script, StringComparison.Ordinal);
}
```

- [ ] **Step 6: Run tests — expect PASS**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~RemoteElevatedScriptBuilderTests|FullyQualifiedName~BuildInstallerScript_SkipRunAsBlock" -v n
```

---

### Task 5: ElevatedRemoteProcessRunner

**Files:**
- Create: `src/PrinterInstall.Core/Remote/ElevatedRemoteProcessRunner.cs`
- Create: `tests/PrinterInstall.Core.Tests/Remote/ElevatedRemoteProcessRunnerTests.cs`

- [ ] **Step 1: Write failing tests with Moq**

Create `tests/PrinterInstall.Core.Tests/Remote/ElevatedRemoteProcessRunnerTests.cs`:

```csharp
using System.Net;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class ElevatedRemoteProcessRunnerTests
{
    private static readonly NetworkCredential Cred = new("user", "pass", "DOMAIN");
    private const string Host = "remote-pc";

    [Fact]
    public async Task RunElevatedScriptAsync_CreatesRunsAndDeletesScheduledTask()
    {
        var wmi = new Mock<IRemoteWmiProcessRunner>();
        var stager = new Mock<IRemoteDriverFileStager>();
        var paths = RemoteDriverStagingPaths.Create(Host);
        var commands = new List<string>();

        wmi.Setup(x => x.RunAsync(Host, Cred, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, NetworkCredential, string, TimeSpan, CancellationToken>((_, _, cmd, _, _) => commands.Add(cmd))
            .ReturnsAsync(new RemoteProcessResult(0, 123, TimedOut: false));

        stager.Setup(x => x.WriteTextFileAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), "task.ps1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        stager.Setup(x => x.ReadLogAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), "task.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> OK");

        stager.Setup(x => x.CleanupAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new ElevatedRemoteProcessRunner(wmi.Object, stager.Object);
        await sut.RunElevatedScriptAsync(
            Host,
            Cred,
            RemoteElevatedScriptBuilder.WrapWithResultHandling("Write-Output 'test'"),
            TimeSpan.FromMinutes(1),
            log: null,
            CancellationToken.None);

        Assert.Contains(commands, c => c.Contains("schtasks /Create", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("/RU SYSTEM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("schtasks /Run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, c => c.Contains("schtasks /Delete", StringComparison.OrdinalIgnoreCase));
        stager.Verify(x => x.CleanupAsync(Host, Cred, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunElevatedScriptAsync_ResultFail_Throws()
    {
        var wmi = new Mock<IRemoteWmiProcessRunner>();
        var stager = new Mock<IRemoteDriverFileStager>();

        wmi.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(0, 1, TimedOut: false));
        stager.Setup(x => x.WriteTextFileAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stager.Setup(x => x.ReadLogAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), "task.log", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> FAIL Acesso negado");
        stager.Setup(x => x.CleanupAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new ElevatedRemoteProcessRunner(wmi.Object, stager.Object);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunElevatedScriptAsync(Host, Cred, "Write-Output x", TimeSpan.FromMinutes(1), null, CancellationToken.None));
        Assert.Contains("Acesso negado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

**Prerequisite:** `IRemoteWmiProcessRunner` (Task 3) — Moq mocks the interface, not the sealed `WmiRemoteProcessRunner`.

- [ ] **Step 2: Run tests — expect FAIL**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~ElevatedRemoteProcessRunnerTests" -v n
```

- [ ] **Step 3: Implement ElevatedRemoteProcessRunner**

Create `src/PrinterInstall.Core/Remote/ElevatedRemoteProcessRunner.cs`:

```csharp
using System.Globalization;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class ElevatedRemoteProcessRunner
{
    private static readonly TimeSpan SchtasksBootstrapTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IRemoteWmiProcessRunner _wmiRunner;
    private readonly IRemoteDriverFileStager _stager;

    public ElevatedRemoteProcessRunner(IRemoteWmiProcessRunner wmiRunner, IRemoteDriverFileStager stager)
    {
        _wmiRunner = wmiRunner;
        _stager = stager;
    }

    public async Task RunElevatedScriptAsync(
        string host,
        NetworkCredential credential,
        string scriptContent,
        TimeSpan timeout,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var paths = RemoteDriverStagingPaths.Create(host);
        var taskName = $"PrinterInstall_{paths.StagingId}";
        var scriptLocal = paths.LocalInfPath("task.ps1");
        var logLocal = paths.LocalLogPath("task.log");
        var transcriptWrapper = WrapScriptWithTranscript(scriptContent, logLocal);

        try
        {
            log?.Report("Executando via tarefa agendada elevada (será removida ao concluir)...");

            using (SmbShareConnection.Open(host, "ADMIN$", credential))
            {
                Directory.CreateDirectory(paths.UncRoot);
            }

            await _stager.WriteTextFileAsync(host, credential, paths, "task.ps1", transcriptWrapper, cancellationToken)
                .ConfigureAwait(false);

            var runAt = DateTime.Now.AddMinutes(1);
            var createCmd = string.Format(
                CultureInfo.InvariantCulture,
                "schtasks /Create /TN \"{0}\" /TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{1}\\\"\" /SC ONCE /ST {2:HH:mm} /SD {2:MM/dd/yyyy} /RU SYSTEM /RL HIGHEST /F",
                taskName,
                scriptLocal,
                runAt);

            var createResult = await _wmiRunner.RunAsync(host, credential, createCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (createResult.ReturnValue != 0)
                throw new InvalidOperationException(
                    $"schtasks /Create falhou em {host} (WMI return {createResult.ReturnValue}). Verifique permissão para criar tarefas agendadas como SYSTEM.");

            var runCmd = $"schtasks /Run /TN \"{taskName}\"";
            await _wmiRunner.RunAsync(host, credential, runCmd, SchtasksBootstrapTimeout, cancellationToken)
                .ConfigureAwait(false);

            await PollForResultAsync(host, credential, paths, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var deleteCmd = $"schtasks /Delete /TN \"{taskName}\" /F";
            try
            {
                await _wmiRunner.RunAsync(host, credential, deleteCmd, SchtasksBootstrapTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }

            try
            {
                await _stager.CleanupAsync(host, credential, paths, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
    }

    private async Task PollForResultAsync(
        string host,
        NetworkCredential credential,
        RemoteDriverStagingPaths paths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var logText = await _stager.ReadLogAsync(host, credential, paths, "task.log", cancellationToken)
                .ConfigureAwait(false);
            var resultLine = WmiPrinterOperationsCore.ExtractResultLine(logText);
            if (!string.IsNullOrEmpty(resultLine))
            {
                if (string.Equals(resultLine, "RESULT>> OK", StringComparison.Ordinal))
                    return;
                var detail = resultLine.StartsWith("RESULT>> FAIL ", StringComparison.Ordinal)
                    ? resultLine["RESULT>> FAIL ".Length..]
                    : resultLine;
                throw new InvalidOperationException(
                    $"Acesso negado em {host} mesmo com execução elevada temporária. {detail}");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Execução elevada expirou em {host} após {timeout}.");
    }

    private static string WrapScriptWithTranscript(string scriptContent, string logPath)
    {
        var escapedLog = logPath.Replace("'", "''", StringComparison.Ordinal);
        return $@"
Start-Transcript -Path '{escapedLog}' -Force | Out-Null
try {{
{scriptContent}
}} finally {{
    Stop-Transcript | Out-Null
}}";
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~ElevatedRemoteProcessRunnerTests" -v n
```

---

### Task 6: Integrate CimRemotePrinterOperations

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`
- Create: `tests/PrinterInstall.Core.Tests/Remote/CimRemotePrinterOperationsElevationTests.cs`

- [ ] **Step 1: Update constructor and add mutation helper**

Change constructor to:

```csharp
public CimRemotePrinterOperations(
    IRemoteDriverFileStager stager,
    RemoteHostSessionFactory sessionFactory,
    WmiRemoteProcessRunner processRunner,
    ElevatedRemoteProcessRunner elevatedRunner)
```

Add private helper:

```csharp
private async Task ExecuteMutationAsync(
    string computerName,
    NetworkCredential credential,
    IProgress<string>? log,
    CancellationToken cancellationToken,
    Func<Task> direct,
    Func<Task> elevated)
{
    var session = await _sessionFactory.PrepareAsync(computerName, credential, log, cancellationToken)
        .ConfigureAwait(false);

    async Task RunElevatedAsync()
    {
        log?.Report($"Executando via tarefa agendada elevada em {computerName} (será removida ao concluir)...");
        await elevated().ConfigureAwait(false);
    }

    if (session.RequiresElevatedExecution)
    {
        await RunElevatedAsync().ConfigureAwait(false);
        return;
    }

    try
    {
        await direct().ConfigureAwait(false);
    }
    catch (Exception ex) when (AccessDeniedDetector.IsAccessDenied(ex))
    {
        session.MarkRequiresElevatedExecution();
        log?.Report($"Token administrativo filtrado detectado em {computerName} — execução elevada temporária");
        await RunElevatedAsync().ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Refactor CreateTcpPrinterPortAsync (template for other mutations)**

```csharp
public Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default)
{
    return ExecuteMutationAsync(
        computerName,
        credential,
        log: null,
        cancellationToken,
        direct: () => Task.Run(() =>
        {
            var scope = ConnectRemote(computerName, credential);
            if (WmiPrinterOperationsCore.PortExists(scope, portName))
                return;
            using var portClass = new ManagementClass(scope, new ManagementPath("Win32_TCPIPPrinterPort"), null);
            using var port = portClass.CreateInstance()
                ?? throw new InvalidOperationException("Failed to create Win32_TCPIPPrinterPort instance.");
            port["Name"] = portName;
            port["HostAddress"] = printerHostAddress;
            port["PortNumber"] = portNumber;
            port["Protocol"] = WmiPrinterOperationsCore.MapProtocol(protocol);
            port["SNMPEnabled"] = false;
            port["Queue"] = "";
            port.Put(new PutOptions { Type = PutType.CreateOnly });
        }, cancellationToken),
        elevated: () => _elevatedRunner.RunElevatedScriptAsync(
            computerName,
            credential,
            RemoteElevatedScriptBuilder.BuildCreateTcpPortScript(portName, printerHostAddress, portNumber, protocol),
            InstallTimeout,
            log: null,
            cancellationToken));
}
```

Apply the same pattern to:
- `AddPrinterAsync` → `BuildAddPrinterScript`
- `RemovePrinterQueueAsync` → `BuildRemovePrinterScript`
- `RemoveTcpPrinterPortAsync` → `BuildRemoveTcpPortScript`
- `PrintTestPageAsync` → `BuildPrintTestPageScript`
- `RenamePrinterQueueAsync` → `BuildRenamePrinterScript`

**Read operations** (`GetInstalledDriverNames`, `ListPrinterQueues`, `PrinterQueueExists`, `CountPrintersUsingPort`) — **no changes**.

- [ ] **Step 3: Refactor InstallPrinterDriverAsync for elevated path**

When `session.RequiresElevatedExecution` (check via `PrepareAsync` at start):

```csharp
var scriptContent = WmiPrinterOperationsCore.BuildInstallerScript(
    infLocal, package.ExpectedDriverName, installLogLocal, skipRunAsBlock: session.RequiresElevatedExecution);

// Use _elevatedRunner.RunElevatedScriptAsync instead of _processRunner when elevated;
// read install.log via stager (elevated runner uses task.log — align: pass log file name or read task.log)
```

**Align log file names:** For driver install via elevated runner, use `task.log` as transcript and map `ExtractResultLine` from it; or write install script output to `task.log` via `WrapScriptWithTranscript`.

On Access Denied from direct WMI install path, retry with `_elevatedRunner` and `skipRunAsBlock: true`.

- [ ] **Step 4: Write CimRemotePrinterOperations elevation test (mocked dependencies)**

Create `tests/PrinterInstall.Core.Tests/Remote/CimRemotePrinterOperationsElevationTests.cs` using Moq for `RemoteHostSessionFactory`, `ElevatedRemoteProcessRunner`, and `IRemoteDriverFileStager`. Because `CimRemotePrinterOperations` uses real WMI in direct path, test **only** the elevated branch:

- Setup factory to return `RequiresElevatedExecution = true`
- Verify `ElevatedRemoteProcessRunner.RunElevatedScriptAsync` called for `CreateTcpPrinterPortAsync`
- Setup factory with `RequiresElevatedExecution = false` + inject a test subclass or internal hook that throws `UnauthorizedAccessException` on direct path → verify elevated retry

If direct WMI cannot be mocked easily, use `internal` test hook or move mutation routing to testable `RemoteMutationRouter` class.

Minimal approach — test `ExecuteMutationAsync` via subclass in test assembly with `InternalsVisibleTo`:

Add to `PrinterInstall.Core.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PrinterInstall.Core.Tests" />
</ItemGroup>
```

Extract `ExecuteMutationAsync` as `internal` and test directly.

- [ ] **Step 5: Build and run new tests**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" --filter "FullyQualifiedName~CimRemotePrinterOperationsElevation|FullyQualifiedName~ElevatedRemoteProcessRunner|FullyQualifiedName~AccessDeniedDetector|FullyQualifiedName~RemoteElevatedScriptBuilder|FullyQualifiedName~RemoteHostSessionFactory" -v n
```

Expected: all PASS.

---

### Task 7: DI registration (App.xaml.cs)

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`

- [ ] **Step 1: Register new services**

Replace process runner registration block with:

```csharp
builder.Services.AddSingleton<WmiRemoteProcessRunner>();
builder.Services.AddSingleton<IRemoteWmiProcessRunner>(sp => sp.GetRequiredService<WmiRemoteProcessRunner>());
builder.Services.AddSingleton<RemoteHostSessionFactory>();
builder.Services.AddSingleton<ElevatedRemoteProcessRunner>();
builder.Services.AddSingleton<IRemoteProcessRunner>(sp => sp.GetRequiredService<WmiRemoteProcessRunner>());
```

Update `CimRemotePrinterOperations` registration — if using explicit constructor, ensure DI resolves all four dependencies automatically (default singleton registration):

```csharp
builder.Services.AddSingleton<CimRemotePrinterOperations>();
```

- [ ] **Step 2: Build solution**

```powershell
dotnet build "PrinterInstall.sln" -c Release
```

Expected: `Build succeeded.`

---

### Task 8: Documentation

**Files:**
- Modify: `docs/conexao-remota.md`

- [ ] **Step 1: Add section "UAC remoto e elevação automática"**

Insert after the "Credenciais" section:

```markdown
## UAC remoto e elevação automática

Em PCs de domínio com UAC activo, ligações WMI/SMB recebem frequentemente um **token administrativo filtrado**. Leituras (listar drivers, filas) funcionam; mutações (porta, fila, driver, remoção) podem falhar com *Acesso negado*.

Antes de mutar cada alvo remoto, o app executa **preflight**:

1. Autentica `\\host\IPC$` e `\\host\ADMIN$`
2. Testa WMI (`Win32_PrinterDriver`)
3. Probe de elevação (processo remoto escreve `ELEVATION_PROBE>> TRUE|FALSE` em ficheiro temp)

Se o token estiver filtrado, mutações passam por **tarefa agendada efémera** (`schtasks /RU SYSTEM /RL HIGHEST`). A tarefa e a pasta temp são removidas no `finally`. Nenhuma alteração permanente de registo.

Mensagens na UI: *Autenticando sessão remota*, *Token administrativo filtrado detectado*, *Executando via tarefa agendada elevada*.
```

- [ ] **Step 2: Update architecture table** to mention `RemoteHostSessionFactory` and `ElevatedRemoteProcessRunner`.

---

### Task 9: Full verification

**Files:** None (read-only).

- [ ] **Step 1: Run full Core test suite**

```powershell
dotnet test "tests\PrinterInstall.Core.Tests\PrinterInstall.Core.Tests.csproj" -c Release -v n
```

Expected: all tests PASS (count ≥ baseline + ~15 new tests).

- [ ] **Step 2: Grep for incomplete integration**

```powershell
rg "RequiresElevatedExecution|ElevatedRemoteProcessRunner|RemoteHostSessionFactory" src
```

Expected: hits in `CimRemotePrinterOperations`, new files, `App.xaml.cs`.

- [ ] **Step 3: Manual checklist (domain lab)**

- [ ] Dois PCs domínio: deploy completo com conta admin de domínio, UAC ligado nos alvos
- [ ] Log mostra detecção de token filtrado e execução via schtasks
- [ ] Após deploy, `schtasks /Query /TN PrinterInstall_*` no alvo **não** encontra tarefas
- [ ] Pasta `C:\Windows\Temp\PrinterInstall\` no alvo limpa após operação
- [ ] Removal wizard e rename funcionam com elevação automática

---

## Spec coverage self-review

| Spec requirement | Task |
|------------------|------|
| IPC$ + ADMIN$ preflight | Task 3 |
| Elevation probe | Task 3, 5 |
| schtasks SYSTEM/HIGHEST | Task 5 |
| Transient cleanup | Task 5 `finally` |
| Read ops WMI direct | Task 6 (unchanged methods) |
| Mutating ops elevated routing | Task 6 |
| Access Denied retry 1× | Task 6 `ExecuteMutationAsync` |
| skipRunAsBlock install script | Task 4 |
| UI log messages | Task 3, 5, 6 |
| DI registration | Task 7 |
| Unit tests | Tasks 1–6 |
| Manual checklist | Task 9 |
| docs/conexao-remota.md | Task 8 |
| No WinRM / no permanent registry | Out of scope — not implemented |

## Type consistency notes

- `RemoteHostSession.MarkRequiresElevatedExecution()` used on retry (mutable flag).
- `ElevatedRemoteProcessRunner.RunElevatedScriptAsync` — **not** `IRemoteProcessRunner` (schtasks orchestration exceeds single command line).
- `IRemoteWmiProcessRunner` introduced in Task 3; all runners/factory depend on it.
