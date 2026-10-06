using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using PrinterInstall.App.Resources;
using PrinterInstall.Core.Auth;
using PrinterInstall.Core.Logging;
using PrinterInstall.App.Services;

namespace PrinterInstall.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ILdapCredentialValidator _ldap;
    private readonly ISessionContext _session;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IRememberedUserStore _rememberedUserStore;
    private readonly IThemeService? _themeService;
    private readonly IDiagnosticFileLogger? _diagnosticLogger;

    public LoginViewModel(
        ILdapCredentialValidator ldap,
        ISessionContext session,
        IAppSettingsStore settingsStore,
        IRememberedUserStore rememberedUserStore,
        IThemeService? themeService = null,
        IDiagnosticFileLogger? diagnosticLogger = null)
    {
        _ldap = ldap;
        _session = session;
        _settingsStore = settingsStore;
        _rememberedUserStore = rememberedUserStore;
        _themeService = themeService;
        _diagnosticLogger = diagnosticLogger;

        if (_themeService != null)
        {
            _isDarkMode = _themeService.IsDarkMode;
            _themeService.ThemeChanged += (_, _) =>
            {
                IsDarkMode = _themeService.IsDarkMode;
            };
        }
    }

    [ObservableProperty]
    private bool _isDarkMode;

    [RelayCommand]
    private void ToggleTheme()
    {
        if (_themeService != null)
        {
            _themeService.ToggleTheme();
            IsDarkMode = _themeService.IsDarkMode;
        }
        else
        {
            IsDarkMode = !IsDarkMode;
        }
    }

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isPasswordRevealed;

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordRevealed = !IsPasswordRevealed;
    }

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _errorGuidance;

    [ObservableProperty]
    private string? _errorDetails;

    [ObservableProperty]
    private bool _isErrorDetailsExpanded;

    [RelayCommand]
    private void ToggleErrorDetails()
    {
        if (!string.IsNullOrWhiteSpace(ErrorDetails))
            IsErrorDetailsExpanded = !IsErrorDetailsExpanded;
    }

    partial void OnUserNameChanged(string value) => ClearError();

    partial void OnPasswordChanged(string value) => ClearError();

    private void ClearError()
    {
        ErrorMessage = null;
        ErrorGuidance = null;
        ErrorDetails = null;
        IsErrorDetailsExpanded = false;
    }

    private void ShowAuthenticationError(LoginFailureKind kind, string? details, string? submittedPassword = null)
    {
        ClearError();
        var prefix = $"Login_Error_{kind}";
        ErrorMessage = UiStrings.ResourceManager.GetString($"{prefix}_Title")
            ?? UiStrings.ResourceManager.GetString("Login_Error_Unknown_Title");
        ErrorGuidance = UiStrings.ResourceManager.GetString($"{prefix}_Guidance")
            ?? UiStrings.ResourceManager.GetString("Login_Error_Unknown_Guidance");
        ErrorDetails = string.IsNullOrWhiteSpace(details) ? null : RedactPassword(details, submittedPassword);
    }

    private string RedactPassword(string details, string? submittedPassword)
    {
        if (!string.IsNullOrEmpty(submittedPassword))
            details = details.Replace(submittedPassword, "[SENHA OMITIDA]", StringComparison.Ordinal);
        return string.IsNullOrEmpty(Password)
            ? details
            : details.Replace(Password, "[SENHA OMITIDA]", StringComparison.Ordinal);
    }

    public void ShowUnexpectedError(Exception exception)
    {
        _diagnosticLogger?.RegisterSensitiveValue(Password);
        ShowAuthenticationError(LdapLoginErrorMessages.KindFromException(exception), exception.ToString());
        _diagnosticLogger?.LogError("Falha inesperada ao entrar.", "Login", exception);
    }

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
        ClearError();
        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = UiStrings.Login_Validation_DomainUserRequired;
            return (false, ErrorMessage);
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = UiStrings.Login_Validation_PasswordRequired;
            return (false, ErrorMessage);
        }

        IsAuthenticating = true;
        try
        {
            _diagnosticLogger?.RegisterSensitiveValue(Password);
            var settings = _settingsStore.Load();
            var configuredDomain = settings.DomainName;
            var (userName, domainName) = ParseCredentialIdentity(UserName, configuredDomain);
            var cred = new NetworkCredential(userName, Password, domainName);
            var ldapHost = !string.IsNullOrWhiteSpace(settings.LdapHost)
                ? settings.LdapHost.Trim()
                : ResolveLdapHost(domainName, configuredDomain);

            if (string.IsNullOrWhiteSpace(domainName) || string.IsNullOrWhiteSpace(ldapHost))
            {
                ErrorMessage = UiStrings.Login_Validation_DomainTitle;
                ErrorGuidance = UiStrings.Login_Validation_DomainRequired;
                return (false, ErrorMessage);
            }

            // Retoma no contexto da UI para atualizar bindings, estado e sessão.
            var result = await _ldap.ValidateAsync(ldapHost, cred, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                ShowAuthenticationError(result.FailureKind, result.ErrorMessage, cred.Password);
                _diagnosticLogger?.LogWarning($"{ErrorMessage}{Environment.NewLine}{ErrorDetails}", "Login");
                return (false, ErrorMessage);
            }

            // O validador pode ter autenticado a variante sem pontuação do usuário.
            // A implantação precisa usar exatamente a identidade que foi validada.
            if (result.ValidatedUserName is not null)
            {
                userName = result.ValidatedUserName;
                domainName = result.ValidatedDomain ?? domainName;
                cred = new NetworkCredential(userName, cred.Password, domainName);
            }

            if (RememberMe)
                _rememberedUserStore.Save(new RememberedUser(domainName, userName));
            else
                _rememberedUserStore.Clear();

            _session.Credential = cred;
            _session.DomainName = domainName;
            return (true, null);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    internal static (string UserName, string DomainName) ParseCredentialIdentity(string rawUserName, string configuredDomain) =>
        CredentialHelper.SplitDomainAndUser(rawUserName, configuredDomain);

    internal static string ResolveLdapHost(string parsedDomain, string configuredDomain) =>
        parsedDomain.Contains('.', StringComparison.Ordinal)
            ? parsedDomain.Trim()
            : configuredDomain.Trim();
}
