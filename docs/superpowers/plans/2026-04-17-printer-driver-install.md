# Printer Driver Install — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the expected printer driver is missing on a target, install it automatically from an embedded per-brand INF package, then resume the existing port/queue creation flow.

**Architecture:** Introduce a `LocalDriverPackageCatalog` that discovers INF packages under `Drivers\<Brand>\` next to the executable. The `PrinterDeploymentOrchestrator` consults it when driver validation fails; if a package exists, it asks `IRemotePrinterOperations.InstallPrinterDriverAsync` to install it on the target, then revalidates. Both WinRM and CIM channels implement installation using a **shared SMB file-copy path** (to `\\<host>\ADMIN$\Temp\PrinterInstall\<guid>\`) and execute `pnputil` differently: WinRM via PS Remoting, CIM via `Win32_Process.Create` with WMI process-exit wait. The new flow is wrapped in two new `TargetMachineState` values (`InstallingDriver`, `DriverInstalledReconfirming`) so the UI shows the extra phase without disturbing existing transitions.

**Tech Stack:** .NET 8 / WPF / `System.Management.Automation` (WinRM), `System.Management` (WMI/CIM), P/Invoke to `mpr.dll` (SMB mount via `WNetUseConnection`/`WNetCancelConnection2`), xUnit + Moq for tests.

**Spec:** `docs/superpowers/specs/2026-04-17-printer-driver-install-design.md`

---

## File Structure

### New files

- `src/PrinterInstall.Core/Drivers/LocalDriverPackage.cs` — record describing an installable package.
- `src/PrinterInstall.Core/Drivers/ILocalDriverPackageCatalog.cs` — interface used by the orchestrator.
- `src/PrinterInstall.Core/Drivers/LocalDriverPackageCatalog.cs` — filesystem discovery under `AppContext.BaseDirectory\Drivers\<Brand>\`.
- `src/PrinterInstall.Core/Drivers/NullLocalDriverPackageCatalog.cs` — always returns `null`; used when DI not wired and in tests.
- `src/PrinterInstall.Core/Drivers/PnputilOutputParser.cs` — extracts the last useful line from a `pnputil` output log.
- `src/PrinterInstall.Core/Remote/SmbShareConnection.cs` — `IDisposable` P/Invoke wrapper around `WNetUseConnection`/`WNetCancelConnection2`.
- `src/PrinterInstall.Core/Remote/RemoteDriverStagingPaths.cs` — builds UNC and local-on-target paths for the temp driver folder.
- `src/PrinterInstall.Core/Remote/IRemoteDriverFileStager.cs` + `SmbRemoteDriverFileStager.cs` — stages the driver folder onto the target's `ADMIN$` share and cleans up.
- `src/PrinterInstall.Core/Remote/IRemoteProcessRunner.cs` + `WmiRemoteProcessRunner.cs` — runs a command line on a remote machine via `Win32_Process.Create`, waits for exit via `__InstanceDeletionEvent`, reads a log file produced via stdout redirection.
- `tests/PrinterInstall.Core.Tests/Drivers/LocalDriverPackageCatalogTests.cs`
- `tests/PrinterInstall.Core.Tests/Drivers/PnputilOutputParserTests.cs`
- `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`
- `tests/PrinterInstall.Core.Tests/TestDrivers/Fake/fake.inf` — tiny fixture text file.

### Modified files

- `src/PrinterInstall.Core/Models/TargetMachineState.cs` — add `InstallingDriver`, `DriverInstalledReconfirming`.
- `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs` — add `InstallPrinterDriverAsync` with default that throws `NotImplementedException`.
- `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs` — inject `ILocalDriverPackageCatalog`, add install+revalidate branch.
- `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs` — implement `InstallPrinterDriverAsync` (stage via SMB, `pnputil`/`Add-PrinterDriver` via existing `IPowerShellInvoker`).
- `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs` — implement `InstallPrinterDriverAsync` (stage via SMB, `pnputil` via `IRemoteProcessRunner`, register spooler driver via second `Win32_Process.Create`).
- `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs` — wire `InstallPrinterDriverAsync` with the primary/fallback pattern used elsewhere.
- `src/PrinterInstall.App/PrinterInstall.App.csproj` — copy `drivers\**\*` to `Drivers\` next to the exe.
- `src/PrinterInstall.App/App.xaml.cs` — register `ILocalDriverPackageCatalog`, `IRemoteDriverFileStager`, `IRemoteProcessRunner`.
- `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs` — keep existing tests green after orchestrator signature change (use the new constructor overload that defaults to `NullLocalDriverPackageCatalog`).

### Non-negotiable constraints used throughout

- **xUnit + Moq** for all tests (no FluentAssertions, no NUnit). Mirror the style of `PrinterDeploymentOrchestratorTests.cs`.
- **No FluentAssertions** — use `Assert.*`.
- **Threading/async:** `await ... .ConfigureAwait(false)` in `Core` (matches existing code).
- **WPF/WinForms not referenced** from `Core`. Only `System.Management`, `System.Management.Automation`, and P/Invoke.
- **Culture-invariant** string parsing (e.g., exit codes) — `CultureInfo.InvariantCulture`.
- **No secrets** logged. `NetworkCredential.Password` never appears in progress messages or logs.

---

## Task 1: Add new `TargetMachineState` values

**Files:**
- Modify: `src/PrinterInstall.Core/Models/TargetMachineState.cs`

- [ ] **Step 1: Add the new enum values**

Replace the file with:

```csharp
namespace PrinterInstall.Core.Models;

public enum TargetMachineState
{
    Pending,
    ContactingRemote,
    ValidatingDriver,
    InstallingDriver,
    DriverInstalledReconfirming,
    Configuring,
    CompletedSuccess,
    AbortedDriverMissing,
    Error
}
```

- [ ] **Step 2: Build the solution to confirm nothing else breaks**

Run: `dotnet build`
Expected: build succeeds (no consumers switch on this enum exhaustively).

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Models/TargetMachineState.cs
git commit -m "feat(core): add InstallingDriver and DriverInstalledReconfirming states"
```

---

## Task 2: Create `LocalDriverPackage` record

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/LocalDriverPackage.cs`

- [ ] **Step 1: Create the record**

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed record LocalDriverPackage(
    PrinterBrand Brand,
    string RootFolder,
    string InfFileName,
    string ExpectedDriverName);
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/LocalDriverPackage.cs
git commit -m "feat(core): add LocalDriverPackage record"
```

---

## Task 3: Create `ILocalDriverPackageCatalog` interface

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/ILocalDriverPackageCatalog.cs`

- [ ] **Step 1: Create the interface**

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public interface ILocalDriverPackageCatalog
{
    LocalDriverPackage? TryGet(PrinterBrand brand);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/ILocalDriverPackageCatalog.cs
git commit -m "feat(core): add ILocalDriverPackageCatalog interface"
```

---

## Task 4: Create `NullLocalDriverPackageCatalog`

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/NullLocalDriverPackageCatalog.cs`

- [ ] **Step 1: Create the null implementation**

```csharp
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed class NullLocalDriverPackageCatalog : ILocalDriverPackageCatalog
{
    public LocalDriverPackage? TryGet(PrinterBrand brand) => null;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/NullLocalDriverPackageCatalog.cs
git commit -m "feat(core): add NullLocalDriverPackageCatalog"
```

---

## Task 5: Write `LocalDriverPackageCatalogTests` (failing)

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Drivers/LocalDriverPackageCatalogTests.cs`
- Create: `tests/PrinterInstall.Core.Tests/TestDrivers/Fake/fake.inf`

- [ ] **Step 1: Create the fixture file**

Create `tests/PrinterInstall.Core.Tests/TestDrivers/Fake/fake.inf` with content:

```
; Fake INF used only for unit tests; do not install.
[Version]
Signature="$Windows NT$"
```

- [ ] **Step 2: Write the tests**

```csharp
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Tests.Drivers;

public class LocalDriverPackageCatalogTests
{
    private static string CreateTempDriverTree(string brandFolderName, Action<string> populate)
    {
        var root = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        var brandRoot = Path.Combine(root, "Drivers", brandFolderName);
        Directory.CreateDirectory(brandRoot);
        populate(brandRoot);
        return root;
    }

    [Fact]
    public void TryGet_WhenInfInTopLevel_ReturnsPackage()
    {
        var root = CreateTempDriverTree("Gainscha", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "Gprinter.inf"), "");
            File.WriteAllText(Path.Combine(brand, "Gprinter.cat"), "");
            Directory.CreateDirectory(Path.Combine(brand, "x64"));
        });
        var sut = new LocalDriverPackageCatalog(root);

        var pkg = sut.TryGet(PrinterBrand.Gainscha);

        Assert.NotNull(pkg);
        Assert.Equal(PrinterBrand.Gainscha, pkg!.Brand);
        Assert.Equal("Gprinter.inf", pkg.InfFileName);
        Assert.Equal(Path.Combine(root, "Drivers", "Gainscha"), pkg.RootFolder);
        Assert.Equal("Gainscha GA-2408T", pkg.ExpectedDriverName);
    }

    [Fact]
    public void TryGet_WhenBrandFolderMissing_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "PrinterInstallTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Epson));
    }

    [Fact]
    public void TryGet_WhenBrandFolderHasNoInfAtTopLevel_ReturnsNull()
    {
        var root = CreateTempDriverTree("Epson", brand =>
        {
            Directory.CreateDirectory(Path.Combine(brand, "WINX64"));
            File.WriteAllText(Path.Combine(brand, "WINX64", "nested.inf"), "");
            File.WriteAllText(Path.Combine(brand, "readme.txt"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Epson));
    }

    [Fact]
    public void TryGet_IgnoresCatAndPnfWhenLookingForInf()
    {
        var root = CreateTempDriverTree("Lexmark", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "foo.cat"), "");
            File.WriteAllText(Path.Combine(brand, "foo.pnf"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        Assert.Null(sut.TryGet(PrinterBrand.Lexmark));
    }

    [Fact]
    public void TryGet_PicksFirstInfAlphabeticallyWhenMultiple()
    {
        var root = CreateTempDriverTree("Lexmark", brand =>
        {
            File.WriteAllText(Path.Combine(brand, "BBB.inf"), "");
            File.WriteAllText(Path.Combine(brand, "AAA.inf"), "");
        });
        var sut = new LocalDriverPackageCatalog(root);

        var pkg = sut.TryGet(PrinterBrand.Lexmark);

        Assert.NotNull(pkg);
        Assert.Equal("AAA.inf", pkg!.InfFileName);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/PrinterInstall.Core.Tests/ --filter "FullyQualifiedName~LocalDriverPackageCatalogTests"`
Expected: 5 tests fail to compile (`LocalDriverPackageCatalog` doesn't exist yet).

---

## Task 6: Implement `LocalDriverPackageCatalog`

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/LocalDriverPackageCatalog.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Drivers;

public sealed class LocalDriverPackageCatalog : ILocalDriverPackageCatalog
{
    private readonly string _baseDirectory;

    public LocalDriverPackageCatalog() : this(AppContext.BaseDirectory) { }

    public LocalDriverPackageCatalog(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public LocalDriverPackage? TryGet(PrinterBrand brand)
    {
        var brandFolder = Path.Combine(_baseDirectory, "Drivers", brand.ToString());
        if (!Directory.Exists(brandFolder))
            return null;

        var inf = Directory.EnumerateFiles(brandFolder, "*.inf", SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (inf is null)
            return null;

        return new LocalDriverPackage(
            brand,
            brandFolder,
            Path.GetFileName(inf),
            PrinterCatalog.GetExpectedDriverName(brand));
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/PrinterInstall.Core.Tests/ --filter "FullyQualifiedName~LocalDriverPackageCatalogTests"`
Expected: 5 passed.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/LocalDriverPackageCatalog.cs \
        tests/PrinterInstall.Core.Tests/Drivers/LocalDriverPackageCatalogTests.cs \
        tests/PrinterInstall.Core.Tests/TestDrivers/Fake/fake.inf
git commit -m "feat(core): discover embedded driver packages via LocalDriverPackageCatalog"
```

---

## Task 7: Add `InstallPrinterDriverAsync` to `IRemotePrinterOperations`

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs`

- [ ] **Step 1: Extend the interface**

Replace the file with:

```csharp
using System.Net;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Remote;

public interface IRemotePrinterOperations
{
    Task<IReadOnlyList<string>> GetInstalledDriverNamesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default);

    Task CreateTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, string printerHostAddress, int portNumber, string protocol, CancellationToken cancellationToken = default);

    Task AddPrinterAsync(string computerName, NetworkCredential credential, string printerName, string driverName, string portName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemotePrinterQueueInfo>> ListPrinterQueuesAsync(string computerName, NetworkCredential credential, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task RemovePrinterQueueAsync(string computerName, NetworkCredential credential, string printerName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task<int> CountPrintersUsingPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task RemoveTcpPrinterPortAsync(string computerName, NetworkCredential credential, string portName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Build to confirm existing implementations still compile**

Run: `dotnet build`
Expected: success (default method means implementations don't have to override yet).

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/IRemotePrinterOperations.cs
git commit -m "feat(core): add IRemotePrinterOperations.InstallPrinterDriverAsync contract"
```

---

## Task 8: Write `PnputilOutputParserTests` (failing)

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Drivers/PnputilOutputParserTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using PrinterInstall.Core.Drivers;

namespace PrinterInstall.Core.Tests.Drivers;

public class PnputilOutputParserTests
{
    [Fact]
    public void ExtractLastUsefulLine_ReturnsLastNonEmptyLine()
    {
        var log = "Microsoft PnP Utility\r\n\r\nAdding driver package:  Gprinter.inf\r\nDriver package added successfully.\r\n\r\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("Driver package added successfully.", line);
    }

    [Fact]
    public void ExtractLastUsefulLine_ReturnsEmptyWhenBlank()
    {
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine(""));
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine(null));
        Assert.Equal(string.Empty, PnputilOutputParser.ExtractLastUsefulLine("\r\n\r\n  \r\n"));
    }

    [Fact]
    public void ExtractLastUsefulLine_TrimsTrailingWhitespace()
    {
        var log = "Line one\r\nLine two   \r\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("Line two", line);
    }

    [Fact]
    public void ExtractLastUsefulLine_WorksWithLfOnlyNewlines()
    {
        var log = "first\nsecond\nthird\n";

        var line = PnputilOutputParser.ExtractLastUsefulLine(log);

        Assert.Equal("third", line);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PrinterInstall.Core.Tests/ --filter "FullyQualifiedName~PnputilOutputParserTests"`
Expected: compilation failure (`PnputilOutputParser` doesn't exist).

---

## Task 9: Implement `PnputilOutputParser`

**Files:**
- Create: `src/PrinterInstall.Core/Drivers/PnputilOutputParser.cs`

- [ ] **Step 1: Write the implementation**

```csharp
namespace PrinterInstall.Core.Drivers;

public static class PnputilOutputParser
{
    public static string ExtractLastUsefulLine(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return string.Empty;

        var lines = log.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd();
            if (!string.IsNullOrWhiteSpace(trimmed))
                return trimmed.Trim();
        }
        return string.Empty;
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/PrinterInstall.Core.Tests/ --filter "FullyQualifiedName~PnputilOutputParserTests"`
Expected: 4 passed.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Drivers/PnputilOutputParser.cs \
        tests/PrinterInstall.Core.Tests/Drivers/PnputilOutputParserTests.cs
git commit -m "feat(core): add PnputilOutputParser for extracting log final line"
```

---

## Task 10: Update existing `PrinterDeploymentOrchestratorTests` for new constructor

**Files:**
- Modify: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorTests.cs`

**Rationale:** Task 11 changes the orchestrator constructor to require `ILocalDriverPackageCatalog`. We keep a second constructor without it (defaulting to `NullLocalDriverPackageCatalog`) so existing tests still compile; we only need to confirm tests still pass after Task 11.

- [ ] **Step 1: No change needed** — confirm current file compiles after Task 11 refactor by running the suite at the end of Task 11.

(No code change; this task exists only to call out that the existing tests rely on the single-argument constructor which we must preserve.)

---

## Task 11: Write orchestrator tests for new install flow (failing)

**Files:**
- Create: `tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Net;
using Moq;
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Orchestration;

public class PrinterDeploymentOrchestratorDriverInstallTests
{
    private static PrinterDeploymentRequest MakeRequest(PrinterBrand brand = PrinterBrand.Gainscha) => new()
    {
        TargetComputerNames = new[] { "pc1" },
        Brand = brand,
        SelectedModelId = $"{brand.ToString().ToLowerInvariant()}-default",
        DisplayName = "P1",
        PrinterHostAddress = "10.0.0.10",
        PortNumber = 9100,
        Protocol = TcpPrinterProtocol.Raw,
        DomainCredential = new NetworkCredential("u", "p")
    };

    private static LocalDriverPackage MakePackage(PrinterBrand brand) =>
        new(brand, "C:\\fake\\Drivers\\" + brand, "fake.inf", PrinterCatalog.GetExpectedDriverName(brand));

    private static Mock<ILocalDriverPackageCatalog> CatalogWith(PrinterBrand brand, LocalDriverPackage? package)
    {
        var mock = new Mock<ILocalDriverPackageCatalog>();
        mock.Setup(c => c.TryGet(brand)).Returns(package);
        return mock;
    }

    [Fact]
    public async Task DriverMissing_PackageAvailable_InstallSucceeds_Reconfirms_ContinuesFlow()
    {
        var expected = PrinterCatalog.GetExpectedDriverName(PrinterBrand.Gainscha);
        var remote = new Mock<IRemotePrinterOperations>(MockBehavior.Strict);
        var calls = 0;
        remote.Setup(m => m.GetInstalledDriverNamesAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => ++calls == 1 ? (IReadOnlyList<string>)new[] { "Other" } : new[] { expected });
        remote.Setup(m => m.InstallPrinterDriverAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        remote.Setup(m => m.CreateTcpPrinterPortAsync("pc1", It.IsAny<NetworkCredential>(), It.IsAny<string>(), "10.0.0.10", 9100, "RAW", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        remote.Setup(m => m.AddPrinterAsync("pc1", It.IsAny<NetworkCredential>(), "P1", expected, It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));

        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new Progress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.InstallingDriver);
        Assert.Contains(events, e => e.State == TargetMachineState.DriverInstalledReconfirming);
        Assert.Contains(events, e => e.State == TargetMachineState.CompletedSuccess);
        remote.VerifyAll();
    }

    [Fact]
    public async Task DriverMissing_PackageAvailable_InstallSucceeds_RevalidationFails_Aborts()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Still Wrong" });
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new Progress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.AbortedDriverMissing);
        remote.Verify(m => m.CreateTcpPrinterPortAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DriverMissing_NoLocalPackage_AbortsAsBefore()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });

        var catalog = CatalogWith(PrinterBrand.Gainscha, null);
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new Progress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.State == TargetMachineState.AbortedDriverMissing);
        remote.Verify(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstallThrowsNotImplemented_AbortsWithChannelMessage()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new NotImplementedException());

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(MakeRequest(), new Progress<DeploymentProgressEvent>(events.Add));

        var aborted = Assert.Single(events.Where(e => e.State == TargetMachineState.AbortedDriverMissing));
        Assert.Contains("install unsupported on this channel", aborted.Message);
    }

    [Fact]
    public async Task InstallThrowsGenericException_MapsToError_ContinuesRunForOtherTargets()
    {
        var remote = new Mock<IRemotePrinterOperations>();
        remote.Setup(m => m.GetInstalledDriverNamesAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { "Other" });
        var seq = new Queue<Func<Task>>(new Func<Task>[]
        {
            () => throw new InvalidOperationException("boom"),
            () => Task.CompletedTask
        });
        remote.Setup(m => m.InstallPrinterDriverAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<LocalDriverPackage>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
              .Returns(() => seq.Dequeue()());

        var catalog = CatalogWith(PrinterBrand.Gainscha, MakePackage(PrinterBrand.Gainscha));
        var sut = new PrinterDeploymentOrchestrator(remote.Object, catalog.Object);
        var request = MakeRequest();
        request.TargetComputerNames = new[] { "pc1", "pc2" };
        var events = new List<DeploymentProgressEvent>();

        await sut.RunAsync(request, new Progress<DeploymentProgressEvent>(events.Add));

        Assert.Contains(events, e => e.ComputerName == "pc1" && e.State == TargetMachineState.Error);
        Assert.Contains(events, e => e.ComputerName == "pc2" && e.State == TargetMachineState.InstallingDriver);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PrinterInstall.Core.Tests/ --filter "FullyQualifiedName~PrinterDeploymentOrchestratorDriverInstallTests"`
Expected: compilation failure (no two-argument `PrinterDeploymentOrchestrator` constructor).

---

## Task 12: Rewrite `PrinterDeploymentOrchestrator` for install flow

**Files:**
- Modify: `src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs`

- [ ] **Step 1: Replace file content**

```csharp
using PrinterInstall.Core.Catalog;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Orchestration;

public sealed class PrinterDeploymentOrchestrator
{
    private readonly IRemotePrinterOperations _remote;
    private readonly ILocalDriverPackageCatalog _localDrivers;

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote)
        : this(remote, new NullLocalDriverPackageCatalog()) { }

    public PrinterDeploymentOrchestrator(IRemotePrinterOperations remote, ILocalDriverPackageCatalog localDrivers)
    {
        _remote = remote;
        _localDrivers = localDrivers;
    }

    public async Task RunAsync(PrinterDeploymentRequest request, IProgress<DeploymentProgressEvent> progress, CancellationToken cancellationToken = default)
    {
        foreach (var computer in request.TargetComputerNames)
        {
            try
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ContactingRemote, "Connecting..."));
                var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.ValidatingDriver, $"Checking driver (found {drivers.Count})..."));
                var expected = PrinterCatalog.GetExpectedDriverName(request.Brand);

                if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
                {
                    if (!await TryInstallMissingDriverAsync(computer, request, expected, progress, cancellationToken).ConfigureAwait(false))
                        continue;
                }

                var portName = BuildPortName(request.PrinterHostAddress, request.PortNumber);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Creating port..."));
                var protocol = MapProtocol(request.Protocol);
                await _remote.CreateTcpPrinterPortAsync(computer, request.DomainCredential, portName, request.PrinterHostAddress, request.PortNumber, protocol, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Configuring, "Adding printer..."));
                await _remote.AddPrinterAsync(computer, request.DomainCredential, request.DisplayName, expected, portName, cancellationToken).ConfigureAwait(false);
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.CompletedSuccess, "Done"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.Error, Flatten(ex)));
            }
        }
    }

    private async Task<bool> TryInstallMissingDriverAsync(
        string computer,
        PrinterDeploymentRequest request,
        string expected,
        IProgress<DeploymentProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        var package = _localDrivers.TryGet(request.Brand);
        if (package is null)
        {
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver not installed: {expected}. No local package available."));
            return false;
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.InstallingDriver,
            $"Installing driver package '{package.InfFileName}' on {computer}..."));

        var log = new Progress<string>(msg =>
            progress.Report(new DeploymentProgressEvent(computer, TargetMachineState.InstallingDriver, msg)));

        try
        {
            await _remote.InstallPrinterDriverAsync(computer, request.DomainCredential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver not installed: {expected}. install unsupported on this channel."));
            return false;
        }

        progress.Report(new DeploymentProgressEvent(
            computer,
            TargetMachineState.DriverInstalledReconfirming,
            "Revalidating driver after install..."));

        var drivers = await _remote.GetInstalledDriverNamesAsync(computer, request.DomainCredential, cancellationToken).ConfigureAwait(false);
        if (!DriverNameMatcher.IsDriverInstalled(drivers, expected))
        {
            var sample = string.Join(" | ", drivers.Take(10));
            progress.Report(new DeploymentProgressEvent(
                computer,
                TargetMachineState.AbortedDriverMissing,
                $"Driver installed does not match expected. Expected: {expected}. Found: [{sample}]"));
            return false;
        }

        return true;
    }

    private static string BuildPortName(string printerHostAddress, int portNumber)
    {
        var host = printerHostAddress.Trim();
        return portNumber == 9100 ? host : $"{host}_{portNumber}";
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message?.Trim();
            if (!string.IsNullOrEmpty(msg))
                messages.Add(msg);
        }
        return string.Join(" | ", messages);
    }

    private static string MapProtocol(TcpPrinterProtocol p) => p switch
    {
        TcpPrinterProtocol.Raw => "RAW",
        TcpPrinterProtocol.Lpr => "LPR",
        TcpPrinterProtocol.Ipp => "IPP",
        _ => "RAW"
    };
}
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test tests/PrinterInstall.Core.Tests/`
Expected: **all tests pass**, including the 5 new `PrinterDeploymentOrchestratorDriverInstallTests` and the 2 pre-existing `PrinterDeploymentOrchestratorTests` (using the single-argument constructor overload).

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Orchestration/PrinterDeploymentOrchestrator.cs \
        tests/PrinterInstall.Core.Tests/Orchestration/PrinterDeploymentOrchestratorDriverInstallTests.cs
git commit -m "feat(core): install missing driver package on target before configuring printer"
```

---

## Task 13: Create `SmbShareConnection` (P/Invoke wrapper)

**Files:**
- Create: `src/PrinterInstall.Core/Remote/SmbShareConnection.cs`

- [ ] **Step 1: Write the wrapper**

```csharp
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace PrinterInstall.Core.Remote;

/// <summary>
/// Mounts a Windows SMB share (e.g. \\host\ADMIN$) with an explicit credential and releases it on dispose.
/// </summary>
public sealed class SmbShareConnection : IDisposable
{
    private readonly string _remoteName;
    private bool _disposed;

    private SmbShareConnection(string remoteName)
    {
        _remoteName = remoteName;
    }

    public static SmbShareConnection Open(string host, string shareName, NetworkCredential credential)
    {
        var remote = $"\\\\{host.Trim()}\\{shareName.Trim('\\', '/')}";
        var netResource = new NetResource
        {
            Scope = ResourceScope.GlobalNetwork,
            ResourceType = ResourceType.Disk,
            DisplayType = ResourceDisplaytype.Share,
            RemoteName = remote
        };
        var user = string.IsNullOrEmpty(credential.Domain)
            ? credential.UserName
            : credential.Domain + "\\" + credential.UserName;
        var code = WNetUseConnection(IntPtr.Zero, netResource, credential.Password, user, 0, null, null, null);
        if (code != 0)
            throw new Win32Exception(code, $"SMB mount of {remote} failed (Win32 error {code}).");
        return new SmbShareConnection(remote);
    }

    public string RemoteRoot => _remoteName;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = WNetCancelConnection2(_remoteName, 0, true);
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetUseConnection(
        IntPtr hwndOwner,
        NetResource lpNetResource,
        string lpPassword,
        string lpUserId,
        int dwFlags,
        string? lpAccessName,
        string? lpBufferSize,
        string? lpResult);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string lpName, int dwFlags, bool fForce);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class NetResource
    {
        public ResourceScope Scope;
        public ResourceType ResourceType;
        public ResourceDisplaytype DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    private enum ResourceScope { Connected = 1, GlobalNetwork, Remembered, Recent, Context }
    private enum ResourceType { Any = 0, Disk = 1, Print = 2 }
    private enum ResourceDisplaytype { Generic = 0, Domain, Server, Share, File, Group, Network, Root, Shareadmin, Directory, Tree, Ndscontainer }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/SmbShareConnection.cs
git commit -m "feat(core): add SmbShareConnection P/Invoke wrapper for ADMIN$ mount"
```

(Integration test of SMB mount itself is covered by the manual validation checklist — no unit test because it requires a real network peer.)

---

## Task 14: Create `RemoteDriverStagingPaths`

**Files:**
- Create: `src/PrinterInstall.Core/Remote/RemoteDriverStagingPaths.cs`

- [ ] **Step 1: Write the helper**

```csharp
namespace PrinterInstall.Core.Remote;

/// <summary>
/// Holds the UNC view and local-on-target view of the temporary driver staging folder.
/// Both point to the same physical location (C:\Windows\Temp\PrinterInstall\<id>\).
/// </summary>
public sealed record RemoteDriverStagingPaths(
    string StagingId,
    string UncRoot,
    string LocalOnTargetRoot)
{
    public static RemoteDriverStagingPaths Create(string host)
    {
        var id = Guid.NewGuid().ToString("N");
        return new RemoteDriverStagingPaths(
            id,
            $"\\\\{host.Trim()}\\ADMIN$\\Temp\\PrinterInstall\\{id}",
            $"C:\\Windows\\Temp\\PrinterInstall\\{id}");
    }

    public string UncInfPath(string infFileName) => System.IO.Path.Combine(UncRoot, infFileName);
    public string LocalInfPath(string infFileName) => System.IO.Path.Combine(LocalOnTargetRoot, infFileName);
    public string UncLogPath(string logName) => System.IO.Path.Combine(UncRoot, logName);
    public string LocalLogPath(string logName) => System.IO.Path.Combine(LocalOnTargetRoot, logName);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/RemoteDriverStagingPaths.cs
git commit -m "feat(core): introduce RemoteDriverStagingPaths helper"
```

---

## Task 15: Create `IRemoteDriverFileStager` + `SmbRemoteDriverFileStager`

**Files:**
- Create: `src/PrinterInstall.Core/Remote/IRemoteDriverFileStager.cs`
- Create: `src/PrinterInstall.Core/Remote/SmbRemoteDriverFileStager.cs`

- [ ] **Step 1: Interface**

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public interface IRemoteDriverFileStager
{
    Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken);
    Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken);
    Task CleanupAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implementation**

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class SmbRemoteDriverFileStager : IRemoteDriverFileStager
{
    public Task<RemoteDriverStagingPaths> StageAsync(string host, NetworkCredential credential, string localPackageFolder, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var paths = RemoteDriverStagingPaths.Create(host);
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            Directory.CreateDirectory(paths.UncRoot);
            CopyDirectory(localPackageFolder, paths.UncRoot, cancellationToken);
            return paths;
        }, cancellationToken);
    }

    public Task<string> ReadLogAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, string logName, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
            var logPath = paths.UncLogPath(logName);
            return File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
        }, cancellationToken);
    }

    public Task CleanupAsync(string host, NetworkCredential credential, RemoteDriverStagingPaths paths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                using var share = SmbShareConnection.Open(host, "ADMIN$", credential);
                if (Directory.Exists(paths.UncRoot))
                    Directory.Delete(paths.UncRoot, recursive: true);
            }
            catch
            {
                // Cleanup is best-effort; driver already installed or copy failed — either way, swallow.
            }
        }, CancellationToken.None);
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, rel));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.Core/Remote/IRemoteDriverFileStager.cs \
        src/PrinterInstall.Core/Remote/SmbRemoteDriverFileStager.cs
git commit -m "feat(core): add SMB-based remote driver file stager"
```

---

## Task 16: Create `IRemoteProcessRunner` + `WmiRemoteProcessRunner`

**Files:**
- Create: `src/PrinterInstall.Core/Remote/IRemoteProcessRunner.cs`
- Create: `src/PrinterInstall.Core/Remote/WmiRemoteProcessRunner.cs`

- [ ] **Step 1: Interface and result type**

```csharp
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed record RemoteProcessResult(uint ReturnValue, uint? ProcessId, bool TimedOut);

public interface IRemoteProcessRunner
{
    Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: WMI implementation**

```csharp
using System.Globalization;
using System.Management;
using System.Net;

namespace PrinterInstall.Core.Remote;

public sealed class WmiRemoteProcessRunner : IRemoteProcessRunner
{
    public Task<RemoteProcessResult> RunAsync(string host, NetworkCredential credential, string commandLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var scope = CreateScope(host, credential);
            scope.Connect();

            using var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), null);
            using var inParams = processClass.GetMethodParameters("Create");
            inParams["CommandLine"] = commandLine;

            using var outParams = processClass.InvokeMethod("Create", inParams, null);
            var returnValue = Convert.ToUInt32(outParams["ReturnValue"], CultureInfo.InvariantCulture);
            if (returnValue != 0)
                return new RemoteProcessResult(returnValue, null, TimedOut: false);

            var pid = Convert.ToUInt32(outParams["ProcessId"], CultureInfo.InvariantCulture);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ProcessExists(scope, pid))
                    return new RemoteProcessResult(0, pid, TimedOut: false);
                Thread.Sleep(1000);
            }

            TryTerminate(scope, pid);
            return new RemoteProcessResult(0, pid, TimedOut: true);
        }, cancellationToken);
    }

    private static bool ProcessExists(ManagementScope scope, uint pid)
    {
        var query = new ObjectQuery($"SELECT ProcessId FROM Win32_Process WHERE ProcessId = {pid}");
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject mo in searcher.Get())
        {
            mo.Dispose();
            return true;
        }
        return false;
    }

    private static void TryTerminate(ManagementScope scope, uint pid)
    {
        try
        {
            var query = new ObjectQuery($"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    mo.InvokeMethod("Terminate", new object[] { 1u });
                }
            }
        }
        catch
        {
            // Best effort; orchestrator will mark target Error/Abort regardless.
        }
    }

    private static ManagementScope CreateScope(string host, NetworkCredential credential)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            Username = string.IsNullOrEmpty(credential.Domain) ? credential.UserName : credential.Domain + "\\" + credential.UserName,
            Password = credential.Password ?? "",
            EnablePrivileges = true
        };
        return new ManagementScope($"\\\\{host.Trim()}\\root\\cimv2", options);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/PrinterInstall.Core/Remote/IRemoteProcessRunner.cs \
        src/PrinterInstall.Core/Remote/WmiRemoteProcessRunner.cs
git commit -m "feat(core): add WMI-based remote process runner"
```

(Like `SmbShareConnection`, this is covered by manual integration, not unit test.)

---

## Task 17: Implement `WinRmRemotePrinterOperations.InstallPrinterDriverAsync`

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs`

- [ ] **Step 1: Add constructor arg and method**

Replace the class declaration block and constructor with:

```csharp
public sealed class WinRmRemotePrinterOperations : IRemotePrinterOperations
{
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PnputilTimeout = TimeSpan.FromMinutes(10);

    private readonly IPowerShellInvoker _invoker;
    private readonly IRemoteDriverFileStager _stager;

    public WinRmRemotePrinterOperations(IPowerShellInvoker invoker, IRemoteDriverFileStager stager)
    {
        _invoker = invoker;
        _stager = stager;
    }
```

Then append the new method at the end of the class, before the `Escape` helper:

```csharp
    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(StageTimeout);

        RemoteDriverStagingPaths paths;
        try
        {
            log?.Report($"Staging driver files on \\\\{computerName}\\ADMIN$...");
            paths = await _stager.StageAsync(computerName, credential, package.RootFolder, stageCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to stage driver files to {computerName}: {ex.Message}", ex);
        }

        try
        {
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCts.CancelAfter(PnputilTimeout);

            var infLocal = paths.LocalInfPath(package.InfFileName);
            log?.Report($"Running pnputil on {computerName} for {package.InfFileName}...");

            var script = $@"
$ErrorActionPreference = 'Stop'
$output = & pnputil.exe /add-driver '{Escape(infLocal)}' /install 2>&1 | Out-String
$output
Add-PrinterDriver -Name '{Escape(package.ExpectedDriverName)}' -ErrorAction SilentlyContinue
";
            var result = await _invoker.InvokeOnRemoteRunspaceAsync(computerName, credential, script, runCts.Token).ConfigureAwait(false);
            if (result.Count > 0)
                log?.Report(PnputilOutputParser.ExtractLastUsefulLine(string.Join("\n", result)));
        }
        finally
        {
            await _stager.CleanupAsync(computerName, credential, paths, CancellationToken.None).ConfigureAwait(false);
        }
    }
```

Add the `using PrinterInstall.Core.Drivers;` import at the top of the file.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/WinRmRemotePrinterOperations.cs
git commit -m "feat(core): WinRm channel installs driver via SMB staging + PS pnputil"
```

---

## Task 18: Implement `CimRemotePrinterOperations.InstallPrinterDriverAsync`

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs`

- [ ] **Step 1: Add fields, constructor, and method**

At the top of the class (before `GetInstalledDriverNamesAsync`), add:

```csharp
    private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PnputilTimeout = TimeSpan.FromMinutes(10);

    private readonly IRemoteDriverFileStager _stager;
    private readonly IRemoteProcessRunner _processRunner;

    public CimRemotePrinterOperations(IRemoteDriverFileStager stager, IRemoteProcessRunner processRunner)
    {
        _stager = stager;
        _processRunner = processRunner;
    }
```

At the bottom of the class, add:

```csharp
    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(StageTimeout);

        RemoteDriverStagingPaths paths;
        try
        {
            log?.Report($"Staging driver files on \\\\{computerName}\\ADMIN$...");
            paths = await _stager.StageAsync(computerName, credential, package.RootFolder, stageCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to stage driver files to {computerName}: {ex.Message}", ex);
        }

        try
        {
            var infLocal = paths.LocalInfPath(package.InfFileName);
            var pnputilLog = paths.LocalLogPath("pnputil.log");
            var pnputilCmd =
                $"cmd.exe /c \"pnputil.exe /add-driver \\\"{infLocal}\\\" /install > \\\"{pnputilLog}\\\" 2>&1\"";

            log?.Report($"Launching pnputil on {computerName} via WMI...");
            var pnputilResult = await _processRunner.RunAsync(computerName, credential, pnputilCmd, PnputilTimeout, cancellationToken).ConfigureAwait(false);
            var pnputilOutput = await _stager.ReadLogAsync(computerName, credential, paths, "pnputil.log", cancellationToken).ConfigureAwait(false);
            log?.Report(PnputilOutputParser.ExtractLastUsefulLine(pnputilOutput));

            if (pnputilResult.ReturnValue != 0)
                throw new InvalidOperationException($"pnputil could not start on {computerName} (WMI return {pnputilResult.ReturnValue}).");
            if (pnputilResult.TimedOut)
                throw new TimeoutException($"pnputil timed out on {computerName} after {PnputilTimeout}.");

            var printuiLog = paths.LocalLogPath("printui.log");
            var printuiCmd =
                $"cmd.exe /c \"rundll32.exe printui.dll,PrintUIEntry /ia /m \\\"{package.ExpectedDriverName}\\\" /f \\\"{infLocal}\\\" > \\\"{printuiLog}\\\" 2>&1\"";

            log?.Report($"Registering driver with spooler on {computerName}...");
            var printuiResult = await _processRunner.RunAsync(computerName, credential, printuiCmd, PnputilTimeout, cancellationToken).ConfigureAwait(false);
            if (printuiResult.TimedOut)
                throw new TimeoutException($"printui timed out on {computerName} after {PnputilTimeout}.");
        }
        finally
        {
            await _stager.CleanupAsync(computerName, credential, paths, CancellationToken.None).ConfigureAwait(false);
        }
    }
```

Add `using PrinterInstall.Core.Drivers;` at the top of the file.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/CimRemotePrinterOperations.cs
git commit -m "feat(core): CIM channel installs driver via SMB staging + WMI pnputil/printui"
```

---

## Task 19: Wire `InstallPrinterDriverAsync` in `CompositeRemotePrinterOperations`

**Files:**
- Modify: `src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs`

- [ ] **Step 1: Append method**

Add at the bottom of the class (before the closing brace):

```csharp
    public async Task InstallPrinterDriverAsync(string computerName, NetworkCredential credential, LocalDriverPackage package, IProgress<string>? log, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.InstallPrinterDriverAsync(computerName, credential, package, log, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.InstallPrinterDriverAsync(computerName, credential, package, log, cancellationToken).ConfigureAwait(false);
        }
    }
```

Add `using PrinterInstall.Core.Drivers;` at the top of the file.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.Core/Remote/CompositeRemotePrinterOperations.cs
git commit -m "feat(core): delegate InstallPrinterDriverAsync in CompositeRemotePrinterOperations"
```

---

## Task 20: Update `App.xaml.cs` DI registration

**Files:**
- Modify: `src/PrinterInstall.App/App.xaml.cs`

- [ ] **Step 1: Register new services and rewire existing singletons**

Replace the service registration block inside `App_OnStartup` with:

```csharp
        builder.Services.AddSingleton<ISessionContext, SessionContext>();
        builder.Services.AddSingleton<ILdapCredentialValidator, LdapCredentialValidator>();
        builder.Services.AddSingleton<IPowerShellInvoker, PowerShellInvoker>();

        builder.Services.AddSingleton<IRemoteDriverFileStager, SmbRemoteDriverFileStager>();
        builder.Services.AddSingleton<IRemoteProcessRunner, WmiRemoteProcessRunner>();
        builder.Services.AddSingleton<ILocalDriverPackageCatalog>(_ => new LocalDriverPackageCatalog());

        builder.Services.AddSingleton<WinRmRemotePrinterOperations>(sp =>
            new WinRmRemotePrinterOperations(
                sp.GetRequiredService<IPowerShellInvoker>(),
                sp.GetRequiredService<IRemoteDriverFileStager>()));
        builder.Services.AddSingleton<CimRemotePrinterOperations>(sp =>
            new CimRemotePrinterOperations(
                sp.GetRequiredService<IRemoteDriverFileStager>(),
                sp.GetRequiredService<IRemoteProcessRunner>()));

        builder.Services.AddSingleton<IRemotePrinterOperations>(sp =>
        {
            var winRm = sp.GetRequiredService<WinRmRemotePrinterOperations>();
            var cim = sp.GetRequiredService<CimRemotePrinterOperations>();
            return new CompositeRemotePrinterOperations(winRm, cim);
        });

        builder.Services.AddSingleton<PrinterDeploymentOrchestrator>(sp =>
            new PrinterDeploymentOrchestrator(
                sp.GetRequiredService<IRemotePrinterOperations>(),
                sp.GetRequiredService<ILocalDriverPackageCatalog>()));
        builder.Services.AddSingleton<PrinterRemovalOrchestrator>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<RemovalWizardViewModel>();
        builder.Services.AddTransient<LoginWindow>();
        builder.Services.AddTransient<MainWindow>();
        builder.Services.AddTransient<RemovalWizardWindow>();
```

Add these `using` directives at the top of the file:

```csharp
using PrinterInstall.Core.Drivers;
```

- [ ] **Step 2: Build the app**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.App/App.xaml.cs
git commit -m "feat(app): register driver staging, WMI runner, and local package catalog in DI"
```

---

## Task 21: Copy `drivers/` tree into build output via csproj

**Files:**
- Modify: `src/PrinterInstall.App/PrinterInstall.App.csproj`

- [ ] **Step 1: Add the content item**

Insert a new `<ItemGroup>` just above the one that contains `<None Update="appsettings.json" ... />`:

```xml
  <ItemGroup>
    <Content Include="..\..\drivers\**\*">
      <Link>Drivers\%(RecursiveDir)%(Filename)%(Extension)</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

- [ ] **Step 2: Build and verify output layout**

Run: `dotnet build src/PrinterInstall.App/PrinterInstall.App.csproj`

Then:

```powershell
Get-ChildItem src\PrinterInstall.App\bin\Debug\net8.0-windows\Drivers -Recurse -Filter *.inf | Select FullName
```

Expected: three INF files visible — `E_JFB0DE.INF`, `LMUX1l50.inf`, `Gprinter.inf` — under `Drivers\Epson\`, `Drivers\Lexmark\`, `Drivers\Gainscha\`.

- [ ] **Step 3: Commit**

```bash
git add src/PrinterInstall.App/PrinterInstall.App.csproj
git commit -m "build(app): ship drivers/ folder next to the executable"
```

---

## Task 22: Update UI `TargetRowViewModel` / `MainViewModel` for new states

**Files:**
- Read only: `src/PrinterInstall.App/ViewModels/MainViewModel.cs`, `src/PrinterInstall.App/ViewModels/TargetRowViewModel.cs`

- [ ] **Step 1: Confirm no code change is required**

The UI already reflects `TargetMachineState` and `Message` directly through `TargetRowViewModel` (binds via `ObservableProperty`), and `MainViewModel` does not switch on state values exhaustively. The new states will surface automatically.

Skim both files to confirm no `switch`/`case` covers `TargetMachineState` requiring updates. Run:

```powershell
Select-String -Path src\PrinterInstall.App\**\*.cs -Pattern "TargetMachineState\." -SimpleMatch | ForEach-Object { $_.Line }
```

Expected output only contains the two usages (`State = TargetMachineState.Error`, `State = TargetMachineState.Pending`). If any exhaustive `switch` exists, add a case that maps new states to an icon/label consistent with `Configuring` (since installation is a configuration phase from the UI's perspective).

- [ ] **Step 2: If no change, skip commit. Otherwise commit.**

---

## Task 23: Run full test suite end-to-end

**Files:**
- (verification only)

- [ ] **Step 1: Run all tests**

Run: `dotnet test`

Expected output:

- `LocalDriverPackageCatalogTests` — 5 passed
- `PnputilOutputParserTests` — 4 passed
- `PrinterDeploymentOrchestratorTests` — 2 passed (legacy, unchanged)
- `PrinterDeploymentOrchestratorDriverInstallTests` — 5 passed
- Every other pre-existing test — passed

If any fails, fix inline, commit with message prefix `fix(tests):` and re-run.

- [ ] **Step 2: Build full solution in Release**

Run: `dotnet build -c Release`
Expected: success.

- [ ] **Step 3: Tag commit with a checkpoint**

```bash
git commit --allow-empty -m "chore: driver-install feature code complete; manual validation next"
```

---

## Task 24: Manual validation checklist (run by a human)

**Files:**
- Reference only (not executed by agent)

- [ ] **Step 1: Prepare a test target**

Identify a Windows x64 machine on the same domain that does **not** have any of the three printer drivers installed (uninstall them via `pnputil /delete-driver <oem>.inf /uninstall` if needed). Confirm via `Get-PrinterDriver` that none of `EPSON Universal Print Driver`, `Lexmark Universal v4 XL`, or `Gainscha GA-2408T` is listed.

- [ ] **Step 2: Run the app against the target via WinRM**

Launch `PrinterInstall.App.exe` from the build output, log in with a domain admin, enter the test target, pick brand Gainscha, configure any IP (can be a dummy such as `10.255.255.1`), click deploy.

Expected progress events, in order:

1. `ContactingRemote: Connecting...`
2. `ValidatingDriver: Checking driver (found N)...`
3. `InstallingDriver: Installing driver package 'Gprinter.inf' on <host>...`
4. `InstallingDriver: Staging driver files on \\<host>\ADMIN$...`
5. `InstallingDriver: Running pnputil on <host>...`
6. `InstallingDriver: Driver package added successfully.` (or equivalent pnputil tail)
7. `DriverInstalledReconfirming: Revalidating driver after install...`
8. `Configuring: Creating port...` → `Configuring: Adding printer...`
9. `CompletedSuccess: Done`

Verify on the target:

```powershell
Get-PrinterDriver | Where-Object Name -like "*Gainscha*"
Get-Printer    | Where-Object Name -eq "<DisplayName used in UI>"
```

Both must return one match.

- [ ] **Step 3: Repeat for Epson and Lexmark**

Same flow, different brand per run. Note in the validation log the `pnputil` tail message for each.

- [ ] **Step 4: Force the CIM/SMB channel**

Temporarily disable WinRM on the target (`Disable-PSRemoting -Force`) or block TCP 5985/5986 via firewall rule. Repeat Step 2 — the progress events must still reach `CompletedSuccess`, routed through `CimRemotePrinterOperations`. Re-enable WinRM after.

- [ ] **Step 5: Negative path — no local package**

Rename `bin\...\Drivers\Gainscha` to `Gainscha_off` and re-run the Gainscha scenario. Expected terminal state: `AbortedDriverMissing` with message `Driver not installed: Gainscha GA-2408T. No local package available.`

- [ ] **Step 6: Negative path — install but revalidation fails**

Hard to reproduce without altering the package. Skip unless the package name reported by `Get-PrinterDriver` after install diverges from the catalog — in which case document the actual reported name and update `PrinterCatalog.DriverNames` accordingly.

- [ ] **Step 7: Negative path — SMB unreachable**

Block TCP 445 on the target. Attempt any brand. Expected: the target marks as `Error` with message containing `SMB mount of \\<host>\ADMIN$ failed (Win32 error 53)` or similar; the run continues for other targets.

- [ ] **Step 8: Cleanup verification**

On the target after any successful run:

```powershell
Test-Path C:\Windows\Temp\PrinterInstall
```

Expected: the folder is empty (no leftover `<guid>` subdirectories). If a leftover exists, file a bug; do not auto-delete in the app — investigate first.

---

## Self-review

**Spec coverage check:**

| Spec section | Covered by |
|---|---|
| §3.1 árvore no repositório | Task 21 (csproj copy) + no-op (already committed in workflow) |
| §3.2 árvore no executável | Task 21 verification |
| §3.3 x64 only | Documented; no code change needed |
| §4.1 `LocalDriverPackageCatalog` | Tasks 2–6 |
| §4.2 `IRemotePrinterOperations.InstallPrinterDriverAsync` | Task 7 (contract), 17 (WinRM), 18 (CIM), 19 (composite) |
| §4.3 orchestrator flow | Tasks 10–12 |
| §5.1 messages | Task 12 (orchestrator) + Task 17/18 (channel-specific messages) |
| §5.2 timeouts | Tasks 17, 18 (`StageTimeout`, `PnputilTimeout`) |
| §5.3 cleanup | Task 15 (stager cleanup) + finally blocks in 17, 18 |
| §6 network prerequisites | Task 24 step 7 (negative path) |
| §7.1 csproj | Task 21 |
| §7.2 WiX modular installer | Out of scope |
| §8.1 catalog tests | Task 5 |
| §8.2 orchestrator tests | Task 11 |
| §8.3 parser tests | Task 8 |
| §8.4 integration validation | Task 24 |
| §9 out of scope | respected |

All spec requirements have a task.

**Placeholder scan:** No TBD/TODO/"implement later" in any task. Every step contains concrete code or commands.

**Type consistency:** 
- `ILocalDriverPackageCatalog.TryGet(PrinterBrand)` used consistently across Tasks 3, 4, 6, 11, 12, 20.
- `LocalDriverPackage` properties (`Brand`, `RootFolder`, `InfFileName`, `ExpectedDriverName`) used consistently in Tasks 2, 6, 11, 17, 18.
- `IRemoteDriverFileStager` methods (`StageAsync`, `ReadLogAsync`, `CleanupAsync`) used consistently in Tasks 15, 17, 18.
- `RemoteDriverStagingPaths` constructor shape matches usage (`UncRoot`, `LocalOnTargetRoot`, `UncInfPath`, `LocalInfPath`, `LocalLogPath`, `UncLogPath`) in Tasks 14, 15, 17, 18.
- `RemoteProcessResult(ReturnValue, ProcessId, TimedOut)` used consistently across Tasks 16, 18.
- Enum additions `InstallingDriver`, `DriverInstalledReconfirming` used across Tasks 1, 11, 12, 22.

No naming drift detected.
