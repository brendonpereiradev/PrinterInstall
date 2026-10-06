namespace PrinterInstall.Core.Auth;

public enum LoginFailureKind
{
    Unknown,
    InvalidCredentials,
    NetworkUnavailable,
    DomainNotFound,
    Timeout,
    AccountLocked,
    AccountDisabled,
    AccountExpired,
    PasswordExpired,
    PasswordMustChange,
    AccountRestricted,
    AccessDenied,
    UnsupportedAuthentication
}
