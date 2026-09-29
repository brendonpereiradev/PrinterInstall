using System.Collections.Concurrent;
using System.Management;
using System.Net;
using System.Reflection;
using Moq;
using PrinterInstall.Core.Drivers;
using PrinterInstall.Core.Models;
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

    [Fact]
    public async Task ExecuteMutationAsync_WhenDirectThrowsManagementGenericFailure_FallsBackToElevated()
    {
        var session = new RemoteHostSession(Host, requiresElevatedExecution: false);
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, session);
        var sut = CreateSut(sessionFactory);

        var directCalls = 0;
        var elevatedCalled = false;
        var logReported = new List<string>();
        var progressMock = new Mock<IProgress<string>>();
        progressMock.Setup(p => p.Report(It.IsAny<string>()))
            .Callback<string>(logReported.Add);

        var mgmtEx = CreateManagementException(ManagementStatus.Failed, "Generic failure");

        await sut.ExecuteMutationAsync(
            Host,
            Credential,
            log: progressMock.Object,
            CancellationToken.None,
            direct: () =>
            {
                directCalls++;
                throw mgmtEx;
            },
            elevated: () =>
            {
                elevatedCalled = true;
                return Task.CompletedTask;
            });

        Assert.Equal(1, directCalls);
        Assert.True(elevatedCalled);
        Assert.Contains(logReported, l => l.Contains("Operação direta WMI falhou", StringComparison.OrdinalIgnoreCase) && l.Contains("Generic failure"));
    }

    [Fact]
    public async Task InstallPrinterDriverAsync_WhenDirectWmiProcessReturnsError_FallsBackToElevatedRunner()
    {
        var session = new RemoteHostSession(Host, requiresElevatedExecution: false);
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, session);

        var stager = new Mock<IRemoteDriverFileStager>();
        var wmiRunner = new Mock<IRemoteWmiProcessRunner>();
        var fallbackRunner = new Mock<ISchtasksFallbackRunner>();

        var paths = RemoteDriverStagingPaths.Create(Host);
        stager.Setup(x => x.StageAsync(Host, Credential, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paths);
        stager.Setup(x => x.WriteTextFileAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stager.Setup(x => x.ReadLogAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> OK");
        stager.Setup(x => x.CleanupAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // O runner WMI direto falha com código 8
        wmiRunner.Setup(x => x.RunAsync(Host, Credential, It.Is<string>(s => s.Contains("install.ps1")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(8, null, TimedOut: false));

        // Na execução elevada, WMI também falha com 8
        wmiRunner.Setup(x => x.RunAsync(Host, Credential, It.Is<string>(s => s.Contains("schtasks")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(8, null, TimedOut: false));

        // O fallback RPC remoto funciona com sucesso
        var fallbackArgs = new List<string>();
        fallbackRunner.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, TimeSpan, CancellationToken>((a, _, _) => fallbackArgs.Add(a))
            .ReturnsAsync(new LocalProcessOutput(new RemoteProcessResult(0, 123, TimedOut: false), "", ""));

        var elevatedRunner = new ElevatedRemoteProcessRunner(wmiRunner.Object, stager.Object, fallbackRunner.Object);
        var sut = new CimRemotePrinterOperations(stager.Object, sessionFactory, wmiRunner.Object, elevatedRunner);

        var package = new LocalDriverPackage(PrinterBrand.Lexmark, "C:\\Fake\\Driver", "fake.inf", "Fake Driver Name");

        await sut.InstallPrinterDriverAsync(Host, Credential, package, log: null, CancellationToken.None);

        Assert.True(session.RequiresElevatedExecution);
        Assert.Contains(fallbackArgs, a => a.Contains("/Create /S \"remote-pc\"", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fallbackArgs, a => a.Contains("/Run /S \"remote-pc\"", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallPrinterDriverAsync_ElevatedFailure_ReadsLogBeforeCleanupAndPreservesError(bool logUnreadable)
    {
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, new RemoteHostSession(Host, true));
        var stager = new Mock<IRemoteDriverFileStager>();
        var runner = new Mock<IRemoteWmiProcessRunner>();
        var paths = RemoteDriverStagingPaths.Create(Host);
        var actions = new List<string>();
        var messages = new List<string>();
        stager.Setup(x => x.StageAsync(Host, Credential, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(paths);
        stager.Setup(x => x.ReadLogAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> FAIL pnputil: erro original");
        stager.Setup(x => x.ReadLogAsync(Host, Credential, paths, "install.log", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                actions.Add("read");
                return logUnreadable ? Task.FromException<string>(new IOException("log ocupado"))
                    : Task.FromResult("PNPUTIL>> motivo completo");
            });
        stager.Setup(x => x.CleanupAsync(Host, Credential, paths, It.IsAny<CancellationToken>()))
            .Callback(() => actions.Add("cleanup")).Returns(Task.CompletedTask);
        runner.Setup(x => x.RunAsync(Host, Credential, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(0, 123, false));
        var sut = new CimRemotePrinterOperations(stager.Object, sessionFactory, runner.Object,
            new ElevatedRemoteProcessRunner(runner.Object, stager.Object));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallPrinterDriverAsync(
            Host, Credential, new LocalDriverPackage(PrinterBrand.Lexmark, @"C:\Fake", "fake.inf", "Driver"),
            new PrinterInstall.Core.Tests.TestSupport.InlineProgress<string>(messages.Add)));

        Assert.Contains("erro original", error.Message);
        Assert.Equal(new[] { "read", "cleanup" }, actions);
        Assert.Contains(messages, message => message.Contains(logUnreadable ? "log ocupado" : "motivo completo"));
    }

    [Fact]
    public async Task RenamePrinterQueueAsync_WhenSessionRequiresElevation_RunsElevatedScript()
    {
        var session = new RemoteHostSession(Host, requiresElevatedExecution: true);
        var sessionFactory = CreateSessionFactoryWithCachedSession(Host, session);

        var stager = new Mock<IRemoteDriverFileStager>();
        var wmiRunner = new Mock<IRemoteWmiProcessRunner>();

        string? writtenScript = null;
        stager.Setup(x => x.WriteTextFileAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), "task.ps1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, NetworkCredential, RemoteDriverStagingPaths, string, string, CancellationToken>((_, _, _, _, script, _) => writtenScript = script)
            .Returns(Task.CompletedTask);
        stager.Setup(x => x.ReadLogAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), "task.result", It.IsAny<CancellationToken>()))
            .ReturnsAsync("RESULT>> OK");
        stager.Setup(x => x.CleanupAsync(Host, Credential, It.IsAny<RemoteDriverStagingPaths>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        wmiRunner.Setup(x => x.RunAsync(Host, Credential, It.Is<string>(s => s.Contains("schtasks")), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteProcessResult(0, 123, TimedOut: false));

        var elevatedRunner = new ElevatedRemoteProcessRunner(wmiRunner.Object, stager.Object);
        var sut = new CimRemotePrinterOperations(stager.Object, sessionFactory, wmiRunner.Object, elevatedRunner);

        await sut.RenamePrinterQueueAsync(Host, Credential, "OldQueue", "NewQueue", CancellationToken.None);

        Assert.NotNull(writtenScript);
        Assert.Contains("Rename-Printer -Name 'OldQueue' -NewName 'NewQueue'", writtenScript);
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

    private static ManagementException CreateManagementException(ManagementStatus status, string message)
    {
        var ctor = typeof(ManagementException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(ManagementStatus), typeof(string), typeof(ManagementBaseObject) },
            null);
        return (ManagementException)ctor!.Invoke(new object?[] { status, message, null });
    }
}
