namespace PrinterInstall.App.Services;

public sealed record RememberedUser(string DomainName, string UserName);

public interface IRememberedUserStore
{
    RememberedUser? Load();
    void Save(RememberedUser user);
    void Clear();
}
