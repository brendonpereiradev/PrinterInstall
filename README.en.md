<h1 align="center">PrinterInstall</h1>

<p align="center">
  Windows desktop application for automated installation and standardization of network printers and thermal label printers across workstations.
</p>

<p align="center">
  <a href="README.md">Português</a> · <strong>English</strong>
</p>

---

## About the project

**PrinterInstall** is a desktop application built with .NET 8 and WPF to automate network printer configuration in corporate and hospital environments. The system creates TCP/IP ports, installs print drivers, and configures queues on multiple computers remotely, removing the need for manual station by station setup.

The application includes driver packages for various manufacturers, authenticates against Active Directory via LDAP, configures Gainscha thermal labels through structured data streams, and automatically reverts changes if an installation fails on any machine.

---

## Features

| Feature | Description |
|---|---|
| **Batch deployment** | Installs queues on multiple computers simultaneously with live status tracking |
| **Automatic rollback** | Reverts created ports and queues when an error occurs on a target workstation |
| **Control wizard** | Lists, renames, and removes queues on remote machines or the local computer |
| **Direct network test** | Validates connectivity and sends test pages or labels directly to raw port 9100 before deployment |
| **Gainscha presets** | Configures four predefined label sizes: Patient, Matrix, Wristband, and Batch |
| **Domain authentication** | Validates credentials against Active Directory via LDAP using UPN or NetBIOS formats |
| **Customizable settings** | Allows modifying the default domain and LDAP server through the settings view |
| **Privilege elevation** | Runs remote administrative actions via scheduled tasks when required |
| **Report export** | Generates structured text log files with results from every operation |

---

## Stack

The project relies on native Windows platform technologies for print spooler and network management:

- **.NET 8 (`net8.0-windows`)**: Primary runtime platform
- **WPF with WPF-UI**: Graphical interface styled with Windows 11 Fluent Design
- **CommunityToolkit.Mvvm**: MVVM architecture with commands and observable properties
- **Microsoft.Extensions.Hosting**: Dependency injection and application lifecycle management
- **System.Management**: WMI/CIM queries and calls for managing spooler ports, drivers, and queues
- **System.DirectoryServices.Protocols**: Direct LDAP communication with Active Directory
- **Seagull SSDAL and SDS**: Configuration protocol and media calibration for Gainscha thermal printers
- **xUnit and Moq**: Automated test suite for domain and presentation layers

---

## Project structure

```
PrinterInstall/
├── drivers/
│   ├── Brother/                        # Driver packages for Brother printers
│   ├── Epson/                          # EPSON Universal Print Driver packages
│   ├── Gainscha/                       # Drivers and utilities for Gainscha thermal printers
│   └── Lexmark/                        # Lexmark Universal v4 and v2 drivers
├── publish/                            # Output directory for compiled self-contained binaries
├── scripts/
│   ├── Capture-GainschaLabelPreset.ps1 # Captures SDS label calibration profiles
│   ├── Publish-PrinterInstall.ps1      # Builds and publishes the standalone executable
│   └── Test-GainschaAddType.ps1        # Tests C# type importing in PowerShell
├── src/
│   ├── PrinterInstall.App/             # WPF graphical user interface
│   │   ├── Assets/                     # Icons, images, and visual assets
│   │   ├── Converters/                 # XAML value converters
│   │   ├── Localization/               # UI localization and strings
│   │   ├── Services/                   # UI, dialog, session, and log export services
│   │   ├── ViewModels/                 # MVVM presentation logic
│   │   ├── Views/                      # Windows and views
│   │   ├── App.xaml                    # Application styles and global resources
│   │   ├── App.xaml.cs                 # Application entry point and DI container setup
│   │   └── appsettings.json            # Default domain and LDAP server configuration
│   └── PrinterInstall.Core/            # Domain and business logic layer
│       ├── Auth/                       # LDAP authentication and credential handling
│       ├── Catalog/                    # Model and driver catalog mapping
│       ├── Drivers/                    # Package extraction and pnputil driver installation
│       ├── Gainscha/                   # SSDAL, SDS communication, and media calibration
│       ├── Logging/                    # Structured logging and event tracking
│       ├── Models/                     # Domain models, records, and state enums
│       ├── Network/                    # Raw port 9100 communication and test commands
│       ├── Orchestration/              # Deployment orchestrator and rollback journal
│       ├── Remote/                     # Remote CIM/WMI operations and privilege elevation
│       └── Validation/                 # Input and network parameter validation
├── tests/
│   ├── PrinterInstall.App.Tests/       # Unit tests for ViewModels and UI services
│   └── PrinterInstall.Core.Tests/      # Unit tests for domain logic, catalog, and orchestration
├── GEMINI.md                           # Development guidelines and technical references
├── LICENSE                             # MIT license terms
├── MANUAL_DO_USUARIO.md                # User manual with operational instructions (Portuguese)
├── MODELOS_TESTADOS.txt                # List of validated printers and models
├── PrinterInstall.sln                  # .NET solution file
└── README.md                           # Main repository documentation (Portuguese)
```

---

## Architecture

The solution is divided into two primary layers, each accompanied by dedicated test projects:

1. **PrinterInstall.Core**: UI-independent class library containing the hardware catalog, input validation, LDAP authentication, deployment orchestration, raw socket communication, and remote WMI/CIM operations.
2. **PrinterInstall.App**: WPF client application implementing MVVM with WPF-UI. It handles screen navigation, operator input, real-time feedback, and report generation.

Operations modifying system state record their actions in the rollback journal (`DeploymentRollbackJournal`). On cancellation or failure, the orchestrator reverts completed steps, removing partial ports or queues. Local and remote calls pass through `RoutingRemotePrinterOperations`, which selects the appropriate execution strategy for each target machine.

---

## Gainscha label presets

| Preset | Dimensions | Hospital usage |
|---|:---:|---|
| **Patient** | 89 × 36 mm | Identification sheets, patient charts, and bed tags |
| **Matrix** | 50 × 30 mm | Blood collection tubes and lab specimen vials |
| **Wristband** | 25 × 270 mm | Patient identification wristbands |
| **Batch** | 45 × 13 mm | Medication labeling and supply inventory |

Verify which roll is loaded in the physical printer before configuring a Gainscha queue. Selecting a preset larger than the loaded media will cause text to print outside the label boundaries.

---

## Build and run

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10, Windows 11, or Windows Server
- Administrative credentials for remote workstation management

### Step by step

1. **Clone the repository:**
   ```powershell
   git clone https://github.com/brendonpereiradev/PrinterInstall
   cd PrinterInstall
   ```

2. **Build the solution:**
   ```powershell
   dotnet build PrinterInstall.sln
   ```

3. **Run the application:**
   ```powershell
   dotnet run --project src/PrinterInstall.App
   ```

### Available commands

| Command | Description |
|---|---|
| `dotnet build PrinterInstall.sln` | Builds all projects in the solution |
| `dotnet run --project src/PrinterInstall.App` | Starts the WPF application in development mode |
| `dotnet test PrinterInstall.sln` | Runs the automated test suite |
| `pwsh scripts/Publish-PrinterInstall.ps1` | Generates a standalone self-contained executable in `publish/PrinterInstall` |

---

## Workflow

1. **Authentication:** Enter network credentials in `user@domain` or `DOMAIN\user` format. If you need to change the default domain or LDAP server, click the settings icon in the header. Credentials are only kept in memory during the active session.
2. **Target selection:** Add destination workstations by hostname or IP address. The "Add This PC" button includes the current machine. Pasting a list adds multiple targets at once.
3. **Queue configuration:** Select the manufacturer, enter the printer IP address, and define the queue name. For Gainscha thermal printers, select the matching label preset.
4. **Deployment:** Start the installation and follow progress per machine. When finished, export the summary report to a text file.

---

## Documentation

- [User manual](MANUAL_DO_USUARIO.md): Operational guide with screenshots and common troubleshooting (Portuguese)
- [Tested models](MODELOS_TESTADOS.txt): Validated equipment and drivers by manufacturer
- [GEMINI.md](GEMINI.md): Coding standards and architectural documentation (Portuguese)

---

## License and terms

Distributed under the **MIT** license. See [`LICENSE`](LICENSE) for details.

> **Note:** Included third-party printer drivers and utilities belong to their respective manufacturers and are subject to their own license terms.

