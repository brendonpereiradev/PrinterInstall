using System.Net;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.Core.Remote;

internal static class SchtasksRunAsFormatter
{
    public static string FormatRunAsUser(NetworkCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential.UserName);

        return CredentialHelper.BuildCredentialUserName(credential);
    }

    public static string EscapeCmdArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);
}
