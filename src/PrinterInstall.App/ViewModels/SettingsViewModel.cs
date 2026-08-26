using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IDomainDetector _domainDetector;

    public SettingsViewModel(
        IAppSettingsStore settingsStore,
        IDomainDetector domainDetector)
    {
        _settingsStore = settingsStore;
        _domainDetector = domainDetector;
        Initialize();
    }

    [ObservableProperty]
    private string _domainName = "";

    [ObservableProperty]
    private string _ldapHost = "";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isSuccessMessage;

    [ObservableProperty]
    private bool _isSaved;

    public void Initialize()
    {
        var settings = _settingsStore.Load();
        DomainName = settings.DomainName;
        LdapHost = settings.LdapHost ?? "";
        StatusMessage = null;
        IsSuccessMessage = false;
        IsSaved = false;
    }

    [RelayCommand]
    public void DetectDomain()
    {
        StatusMessage = null;
        var detected = _domainDetector.DetectCurrentDomain();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            DomainName = detected.Trim();
            StatusMessage = $"Domínio detectado: {DomainName}";
            IsSuccessMessage = true;
        }
        else
        {
            StatusMessage = "Nenhum domínio corporativo detectado automaticamente nesta máquina.";
            IsSuccessMessage = false;
        }
    }

    [RelayCommand]
    public void Save()
    {
        TrySave();
    }

    public bool TrySave()
    {
        StatusMessage = null;
        if (string.IsNullOrWhiteSpace(DomainName))
        {
            StatusMessage = "O nome do domínio padrão é obrigatório.";
            IsSuccessMessage = false;
            IsSaved = false;
            return false;
        }

        var customLdap = string.IsNullOrWhiteSpace(LdapHost) ? null : LdapHost.Trim();
        _settingsStore.Save(new AppSettings(DomainName.Trim(), customLdap));

        StatusMessage = "Configurações salvas com sucesso!";
        IsSuccessMessage = true;
        IsSaved = true;
        return true;
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        _settingsStore.ResetToDefaults();
        Initialize();
        StatusMessage = "Configurações restauradas para o padrão de fábrica.";
        IsSuccessMessage = true;
    }
}
