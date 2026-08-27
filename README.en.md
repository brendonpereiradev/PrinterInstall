# PrinterInstall

A Windows desktop app that installs network printers and thermal label printers across many workstations at once. It was built for hospital environments, where IT support needs to standardize print queues on many computers without touching each machine by hand.

> **Languages:** English (this file) · [Português](README.md)

## What it does

You give it the target computers and the queues you want. PrinterInstall handles the rest on each machine:

- Installs the right Windows driver from a bundled package, with no CD or manual download.
- Creates the printer's TCP/IP port.
- Creates and names the queue to your standard.
- Calibrates the label size on Gainscha thermal printers.
- Undoes half-created ports and queues when something fails mid-install.

Tested brands: **Epson**, **Lexmark**, **Brother**, and **Gainscha**. Other models from the same maker usually work. See [`MODELOS_TESTADOS.txt`](MODELOS_TESTADOS.txt) for the validated list.

## Features

| Feature | Description |
| --- | --- |
| Batch deploy | Installs one or more queues across many computers at once, with live per-machine status. |
| Automatic rollback | Reverts the ports and queues it created when a machine fails, leaving the station clean. |
| Control wizard | Lists, removes, and renames queues on remote machines without reinstalling the driver. |
| Direct network test | Validates raw port 9100 and prints a test page or label before deploying. |
| Gainscha presets | Four ready label sizes: Patient, Matrix, Wristband, and Batch. |
| Domain login | Authenticates against Active Directory (UPN or NetBIOS) over LDAP. Allows changing default domain and server in settings (⚙️ icon). |
| Log export | Saves a report of everything that was installed. |
| UAC elevation | Runs elevated remote operations through a scheduled task when needed. |

## Architecture

The solution has two layers, each with its own tests.

```
src/
  PrinterInstall.Core/   Domain logic: drivers, remote, orchestration, rollback, network, auth
  PrinterInstall.App/    WPF UI (MVVM): views, view models, UI services
tests/
  PrinterInstall.Core.Tests/
  PrinterInstall.App.Tests/
```

**Stack:** .NET 8 (`net8.0-windows`), WPF with [WPF-UI](https://github.com/lepoco/wpfui), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), dependency injection through `Microsoft.Extensions.Hosting`, `System.Management` for WMI/CIM, and `System.DirectoryServices.Protocols` for LDAP.

**Patterns:** MVVM, Strategy, Orchestrator, Saga/Rollback, Router/Proxy, Result Pattern. Remote operations go through `RoutingRemotePrinterOperations`, which chooses between local and remote execution. Every operation that creates resources records to the `DeploymentRollbackJournal` so it can be reversed.

## Build and run

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Windows.

```powershell
git clone https://github.com/brendonpereiradev/PrinterInstall
cd PrinterInstall
dotnet build PrinterInstall.sln
dotnet run --project src/PrinterInstall.App
```

Run the tests:

```powershell
dotnet test PrinterInstall.sln
```

Produce a single self-contained executable (bundles the .NET runtime, the drivers, and the configuration):

```powershell
pwsh scripts/Publish-PrinterInstall.ps1
```

The output lands in `publish/PrinterInstall`.

## Usage

1. **Login.** Sign in with your domain account as `user@domain` or `DOMAIN\user`. If you need to change the default domain or LDAP server, click the settings icon (⚙️) in the upper corner. Credentials are not stored after you close the program.
2. **Targets.** Add computers by network name or IP. The "Add This PC" button includes the local machine. Pasting a list adds them all at once.
3. **Queues.** Pick the brand, enter the printer IP and the queue name. For Gainscha, select the label preset.
4. **Deploy.** Start the install and watch each machine's status. Export the report when it finishes.

The [user manual](MANUAL_DO_USUARIO.md) (Portuguese) has the full walkthrough, the Gainscha preset table, and fixes for the common errors.

## Gainscha label reference

| Preset | Size | Hospital use |
| --- | :---: | --- |
| Patient | 89 × 36 mm | Charts, records, beds |
| Matrix | 50 × 30 mm | Blood tubes and sample vials |
| Wristband | 25 × 270 mm | Patient ID wristband |
| Batch | 45 × 13 mm | Medication and storeroom |

Confirm which roll is in the printer before installing the queue. A preset larger than the physical label prints past the edge.

## Documentation

- [User manual](MANUAL_DO_USUARIO.md) (Portuguese) — guide for support technicians
- [Tested models](MODELOS_TESTADOS.txt) — validated printers by brand
- [GEMINI.md](GEMINI.md) (Portuguese) — development rules and guidelines

## License

Distributed under the **MIT** License. See [`LICENSE`](LICENSE) for more details.

> **Note:** Included third-party print drivers and utilities are property of their respective manufacturers and subject to their own licensing terms.

