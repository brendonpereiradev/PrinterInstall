using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Moq;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class CimRemotePrinterOperationsElevationTests
{
    private const string Host = "remote-pc";
    private static readonly NetworkCredential Credential = new("user", "pass", "DOMAIN");

    [Fact]
    public async Task ExecuteMutationAsync_WhenSessionRequiresElevation_RunsOnlyElevated()
    {
        var session = new RemoteHostSession(Host, requiresElevatedExecution: true);
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, session);
        var sut = CreateSut(sessionFactory);

        var directCalled = false;
        var elevatedCalled = false;

        await sut.ExecuteMutationAsync(
            Host,
            Credential,
            log: null,
            CancellationToken.None,
            direct: () =>
            {
                directCalled = true;
                return Task.CompletedTask;
            },
            elevated: () =>
            {
                elevatedCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(directCalled);
        Assert.True(elevatedCalled);
    }

    [Fact]
    public async Task ExecuteMutationAsync_WhenDirectThrowsUnauthorizedAccess_RetriesWithElevated()
    {
        var session = new RemoteHostSession(Host, requiresElevatedExecution: false);
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, session);
        var sut = CreateSut(sessionFactory);

        var directCalls = 0;
        var elevatedCalled = false;

        await sut.ExecuteMutationAsync(
            Host,
            Credential,
            log: null,
            CancellationToken.None,
            direct: () =>
            {
                directCalls++;
                throw new UnauthorizedAccessException("Access denied");
            },
            elevated: () =>
            {
                elevatedCalled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(1, directCalls);
        Assert.True(elevatedCalled);
        Assert.True(session.RequiresElevatedExecution);
    }

    private static CimRemotePrinterOperations CreateSut(RemoteHostSessionFactory sessionFactory)
    {
        var stager = new Mock<IRemoteDriverFileStager>();
        var wmiRunner = new Mock<IRemoteWmiProcessRunner>();
        var elevatedRunner = new ElevatedRemoteProcessRunner(wmiRunner.Object, stager.Object);
        return new CimRemotePrinterOperations(stager.Object, sessionFactory, wmiRunner.Object, elevatedRunner);
    }

    private static RemoteHostSessionFactory CreateSessionFactoryWithCachedSession(string host, RemoteHostSession session)
    {
        var wmiRunner = new Mock<IRemoteWmiProcessRunner>();
        var factory = new RemoteHostSessionFactory(wmiRunner.Object);
        var field = typeof(RemoteHostSessionFactory).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field _cache not found.");
        var cache = (ConcurrentDictionary<string, RemoteHostSession>)field.GetValue(factory)!;
        cache[RemoteHostSessionFactory.NormalizeHostKey(host)] = session;
        return factory;
    }
}
