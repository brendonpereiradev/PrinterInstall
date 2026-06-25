using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using PrinterInstall.App.Resources;
using PrinterInstall.Core.Auth;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ILdapCredentialValidator _ldap;
    private readonly ISessionContext _session;
    private readonly IRememberedUserStore _rememberedUserStore;
    private readonly string _domainName;

    public LoginViewModel(
        ILdapCredentialValidator ldap,
        ISessionContext session,
        IConfiguration configuration,
        IRememberedUserStore rememberedUserStore)
    {
        _ldap = ldap;
        _session = session;
        _rememberedUserStore = rememberedUserStore;
        _domainName = (configuration["DomainName"] ?? "preventsenior.local").Trim();
    }

    [ObservableProperty]
    private string _userName = "";

    public string Password { get; set; } = "";

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private string? _errorMessage;

    public void LoadRememberedUser()
    {
        var remembered = _rememberedUserStore.Load();
        if (remembered is null)
            return;

        UserName = remembered.UserName;
        RememberMe = true;
    }

    public async Task<(bool Success, string? Error)> TryLoginAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = UiStrings.Login_Validation_DomainUserRequired;
            return (false, ErrorMessage);
        }

        var cred = new NetworkCredential(UserName, Password, _domainName);
        var result = await _ldap.ValidateAsync(_domainName, cred, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return (false, result.ErrorMessage);
        }

        if (RememberMe)
            _rememberedUserStore.Save(new RememberedUser(_domainName, UserName));
        else
            _rememberedUserStore.Clear();

        _session.Credential = cred;
        _session.DomainName = _domainName;
        return (true, null);
    }
}
