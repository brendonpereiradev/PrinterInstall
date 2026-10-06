namespace PrinterInstall.Core.Auth;

public sealed class LdapValidationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public LoginFailureKind FailureKind { get; private init; }
    public string? ValidatedUserName { get; private init; }
    public string? ValidatedDomain { get; private init; }

    public static LdapValidationResult Success() => new() { IsSuccess = true };

    internal static LdapValidationResult Success(System.Net.NetworkCredential credential) => new()
    {
        IsSuccess = true,
        ValidatedUserName = credential.UserName,
        ValidatedDomain = credential.Domain
    };

    public static LdapValidationResult Failure(string message, LoginFailureKind failureKind = LoginFailureKind.Unknown) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        FailureKind = failureKind
    };
}
