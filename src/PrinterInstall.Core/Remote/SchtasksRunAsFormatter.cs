using System.Net;

namespace PrinterInstall.Core.Remote;

internal static class SchtasksRunAsFormatter
{
    public static string FormatRunAsUser(NetworkCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential.UserName);

        return string.IsNullOrWhiteSpace(credential.Domain)
            ? credential.UserName
            : $"{credential.Domain}\\{credential.UserName}";
    }

    public static string EscapeCmdArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);
}
