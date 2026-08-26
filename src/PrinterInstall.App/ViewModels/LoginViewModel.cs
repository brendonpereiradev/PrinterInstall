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
    private readonly IAppSettingsStore _settingsStore;
    private readonly IRememberedUserStore _rememberedUserStore;

    public LoginViewModel(
        ILdapCredentialValidator ldap,
        ISessionContext session,
        IAppSettingsStore settingsStore,
        IRememberedUserStore rememberedUserStore)
    {
        _ldap = ldap;
        _session = session;
        _settingsStore = settingsStore;
        _rememberedUserStore = rememberedUserStore;
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

        var settings = _settingsStore.Load();
        var configuredDomain = settings.DomainName;
        var (userName, domainName) = ParseCredentialIdentity(UserName, configuredDomain);
        var cred = new NetworkCredential(userName, Password, domainName);
        var ldapHost = !string.IsNullOrWhiteSpace(settings.LdapHost)
            ? settings.LdapHost.Trim()
            : ResolveLdapHost(domainName, configuredDomain);

        var result = await _ldap.ValidateAsync(ldapHost, cred, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return (false, result.ErrorMessage);
        }

        if (RememberMe)
            _rememberedUserStore.Save(new RememberedUser(domainName, userName));
        else
            _rememberedUserStore.Clear();

        _session.Credential = cred;
        _session.DomainName = domainName;
        return (true, null);
    }

    internal static (string UserName, string DomainName) ParseCredentialIdentity(string rawUserName, string configuredDomain)
    {
        var trimmed = rawUserName.Trim();
        if (trimmed.Contains('\\', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('\\', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return (parts[1], parts[0]);
        }

        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('@', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                return (parts[0], parts[1]);
        }

        return (trimmed, configuredDomain.Trim());
    }

    internal static string ResolveLdapHost(string parsedDomain, string configuredDomain) =>
        parsedDomain.Contains('.', StringComparison.Ordinal)
            ? parsedDomain.Trim()
            : configuredDomain.Trim();
}
