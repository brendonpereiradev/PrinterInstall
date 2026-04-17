using System.Net;

namespace PrinterInstall.App.Services;

public sealed class SessionContext : ISessionContext
{
    public NetworkCredential? Credential { get; set; }
    public string DomainName { get; set; } = "";
}
