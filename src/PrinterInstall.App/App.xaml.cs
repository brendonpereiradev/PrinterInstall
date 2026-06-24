using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.App.Views;
using PrinterInstall.Core.Auth;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Network;
using PrinterInstall.Core.Orchestration;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.App;

public partial class App : Application
{
    private IHost? _host;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var ptBr = new CultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = ptBr;
        CultureInfo.DefaultThreadCurrentUICulture = ptBr;

        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None);
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(61, 90, 128),
            ApplicationTheme.Light);

        var builder = Host.CreateApplicationBuilder();
        // appsettings.json is loaded by default from the app directory.

        builder.Services.AddSingleton<ISessionContext, SessionContext>();
        builder.Services.AddSingleton<ILdapCredentialValidator, LdapCredentialValidator>();

        builder.Services.AddSingleton<IRemoteDriverFileStager, SmbRemoteDriverFileStager>();
        builder.Services.AddSingleton<WmiRemoteProcessRunner>();
        builder.Services.AddSingleton<IRemoteWmiProcessRunner>(sp => sp.GetRequiredService<WmiRemoteProcessRunner>());
        builder.Services.AddSingleton<RemoteHostSessionFactory>();
        builder.Services.AddSingleton<ElevatedRemoteProcessRunner>();
        builder.Services.AddSingleton<IRemoteProcessRunner>(sp => sp.GetRequiredService<WmiRemoteProcessRunner>());
        builder.Services.AddSingleton<ILocalDriverPackageCatalog>(_ => new LocalDriverPackageCatalog());

        // Printer operations: local WMI fast-path + remote WMI/DCOM (CimRemotePrinterOperations).
        builder.Services.AddSingleton<LocalMachineIdentity>();
        builder.Services.AddSingleton<LocalPrinterOperations>();
        builder.Services.AddSingleton<CimRemotePrinterOperations>();
        builder.Services.AddSingleton<IRemotePrinterOperations>(sp =>
            new RoutingRemotePrinterOperations(
                sp.GetRequiredService<LocalMachineIdentity>(),
                sp.GetRequiredService<LocalPrinterOperations>(),
                sp.GetRequiredService<CimRemotePrinterOperations>()));

        builder.Services.AddSingleton<IDirectRawPrinterTestService, DirectRawPrinterTestService>();

        builder.Services.AddSingleton<PrinterDeploymentOrchestrator>(sp =>
            new PrinterDeploymentOrchestrator(
                sp.GetRequiredService<IRemotePrinterOperations>(),
                sp.GetRequiredService<ILocalDriverPackageCatalog>()));
        builder.Services.AddSingleton<PrinterControlOrchestrator>(sp =>
            new PrinterControlOrchestrator(sp.GetRequiredService<IRemotePrinterOperations>()));
        builder.Services.AddSingleton<DeploymentRollbackRunner>(sp =>
            new DeploymentRollbackRunner(
                sp.GetRequiredService<IRemotePrinterOperations>(),
                sp.GetRequiredService<PrinterControlOrchestrator>()));
        builder.Services.AddSingleton<PrinterRemovalOrchestrator>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<RemovalWizardViewModel>();
        builder.Services.AddTransient<PrinterNetworkTestViewModel>();
        builder.Services.AddTransient<LoginWindow>();
        builder.Services.AddTransient<MainWindow>();
        builder.Services.AddTransient<RemovalWizardWindow>();
        builder.Services.AddTransient<PrinterNetworkTestWindow>();

        _host = builder.Build();

        var login = _host.Services.GetRequiredService<LoginWindow>();
        MainWindow = login;
        login.Show();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        _host?.Dispose();
    }
}
