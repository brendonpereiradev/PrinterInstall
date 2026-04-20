using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.App.Views;
using PrinterInstall.Core.Auth;
using PrinterInstall.Core.Drivers;
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

        var builder = Host.CreateApplicationBuilder();
        // appsettings.json is loaded by default from the app directory.

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
