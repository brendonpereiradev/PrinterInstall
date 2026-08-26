using System.Net;
using Microsoft.Extensions.Configuration;
using Moq;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelRememberUserTests
{
    private sealed class FakeLdapValidator : ILdapCredentialValidator
    {
        public bool Succeed { get; set; } = true;

        public Task<LdapValidationResult> ValidateAsync(
            string domainName,
            NetworkCredential credential,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Succeed
                ? LdapValidationResult.Success()
                : LdapValidationResult.Failure("ldap fail"));
    }

    private sealed class FakeRememberedUserStore : IRememberedUserStore
    {
        public RememberedUser? Stored { get; set; }
        public int SaveCount { get; private set; }
        public int ClearCount { get; private set; }

        public RememberedUser? Load() => Stored;

        public void Save(RememberedUser user)
        {
            SaveCount++;
            Stored = user;
        }

        public void Clear()
        {
            ClearCount++;
            Stored = null;
        }
    }

    private static LoginViewModel CreateSut(
        FakeRememberedUserStore store,
        FakeLdapValidator? ldap = null,
        SessionContext? session = null)
    {
        ldap ??= new FakeLdapValidator();
        session ??= new SessionContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DomainName"] = "preventsenior.local"
            })
            .Build();

        return new LoginViewModel(ldap, session, new AppSettingsStore(config), store);
    }

    [Fact]
    public void LoadRememberedUser_SetsUserNameAndRememberMe()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "saved.user")
        };
        var sut = CreateSut(store);

        sut.LoadRememberedUser();

        Assert.Equal("saved.user", sut.UserName);
        Assert.True(sut.RememberMe);
    }

    [Fact]
    public void LoadRememberedUser_WhenNothingSaved_LeavesDefaults()
    {
        var store = new FakeRememberedUserStore();
        var sut = CreateSut(store);

        sut.LoadRememberedUser();

        Assert.Equal("", sut.UserName);
        Assert.False(sut.RememberMe);
    }

    [Fact]
    public async Task TryLoginAsync_WithRememberMe_CallsSave()
    {
        var store = new FakeRememberedUserStore();
        var session = new SessionContext();
        var sut = CreateSut(store, session: session);
        sut.UserName = "admin";
        sut.Password = "secret";
        sut.RememberMe = true;

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.NotNull(store.Stored);
        Assert.Equal("admin", store.Stored!.UserName);
        Assert.Equal("preventsenior.local", store.Stored.DomainName);
        Assert.NotNull(session.Credential);
    }

    [Fact]
    public async Task TryLoginAsync_WithoutRememberMe_CallsClear()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "old.user")
        };
        var sut = CreateSut(store);
        sut.UserName = "admin";
        sut.Password = "secret";
        sut.RememberMe = false;

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, store.ClearCount);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task TryLoginAsync_WhenLdapFails_DoesNotTouchStore()
    {
        var store = new FakeRememberedUserStore
        {
            Stored = new RememberedUser("preventsenior.local", "old.user")
        };
        var ldap = new FakeLdapValidator { Succeed = false };
        var sut = CreateSut(store, ldap);
        sut.UserName = "admin";
        sut.Password = "wrong";
        sut.RememberMe = true;

        var result = await sut.TryLoginAsync();

        Assert.False(result.Success);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.NotNull(store.Stored);
    }

    [Fact]
    public async Task TryLoginAsync_WithCustomSettingsStore_UsesCustomDomainAndLdapHost()
    {
        var store = new FakeRememberedUserStore();
        var session = new SessionContext();
        var ldap = new FakeLdapValidator();
        var mockSettings = new Mock<IAppSettingsStore>();
        mockSettings.Setup(s => s.Load())
            .Returns(new PrinterInstall.App.Models.AppSettings("custom.domain.local", "ldap.custom.local"));

        var sut = new LoginViewModel(ldap, session, mockSettings.Object, store);
        sut.UserName = "operador";
        sut.Password = "senha123";
        sut.RememberMe = true;

        var result = await sut.TryLoginAsync();

        Assert.True(result.Success);
        Assert.Equal("custom.domain.local", session.DomainName);
        Assert.Equal("custom.domain.local", store.Stored?.DomainName);
        Assert.Equal("operador", store.Stored?.UserName);
    }
}
