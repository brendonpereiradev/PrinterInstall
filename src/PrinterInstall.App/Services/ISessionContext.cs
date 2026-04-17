using System.Net;

namespace PrinterInstall.App.Services;

public interface ISessionContext
{
    NetworkCredential? Credential { get; set; }
    string DomainName { get; set; }
}
