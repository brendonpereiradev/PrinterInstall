namespace PrinterInstall.Core.Auth;

public sealed class LdapValidationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static LdapValidationResult Success() => new() { IsSuccess = true };

    public static LdapValidationResult Failure(string message) => new()
    {
        IsSuccess = false,
        ErrorMessage = message
    };
}
