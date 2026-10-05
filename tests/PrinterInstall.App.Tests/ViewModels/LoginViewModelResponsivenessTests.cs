using System.Collections.Concurrent;
using System.Net;
using System.Windows.Threading;
using Moq;
using PrinterInstall.App.Models;
using PrinterInstall.App.Services;
using PrinterInstall.App.ViewModels;
using PrinterInstall.Core.Auth;

namespace PrinterInstall.App.Tests.ViewModels;

public class LoginViewModelResponsivenessTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("Negotiate")]
    [InlineData("Ntlm")]
    [InlineData("SMB")]
    [InlineData("LogonUser")]
    public async Task TryLoginAsync_SlowAuthentication_KeepsDispatcherResponsive(string slowAttempt)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeThreads = new ConcurrentQueue<int>();

        LdapValidationResult Attempt(string name)
        {
            nativeThreads.Enqueue(Environment.CurrentManagedThreadId);
            if (name != slowAttempt)
                return LdapValidationResult.Failure("Try next method.");

            entered.TrySetResult();
            if (!release.Wait(TestTimeout))
                throw new TimeoutException("Dispatcher did not release the authentication attempt.");

            return LdapValidationResult.Success();
        }

        var validator = new LdapCredentialValidator(
            (_, _, authType) => Attempt(authType.ToString()),
            (_, _) => Attempt("SMB"),
            (_, _) => Attempt("LogonUser"));

        try
        {
            await RunOnDispatcherAsync(async () =>
            {
                var uiThread = Environment.CurrentManagedThreadId;
                var remembered = new Mock<IRememberedUserStore>();
                var savedOnThread = 0;
                remembered.Setup(s => s.Save(It.IsAny<RememberedUser>()))
                    .Callback(() => savedOnThread = Environment.CurrentManagedThreadId);
                var session = new SessionContext();
                var sut = CreateSut(validator, session, remembered.Object);
                sut.RememberMe = true;
                var notificationThreads = new ConcurrentQueue<int>();
                sut.PropertyChanged += (_, _) => notificationThreads.Enqueue(Environment.CurrentManagedThreadId);

                var loginTask = sut.TryLoginAsync();
                await entered.Task.WaitAsync(TestTimeout);

                // Esta mensagem precisa ser processada enquanto a chamada nativa ainda espera.
                await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    Assert.True(sut.IsAuthenticating);
                    Assert.False(loginTask.IsCompleted);
                    Assert.Null(session.Credential);
                    Assert.All(nativeThreads, id => Assert.NotEqual(uiThread, id));
                    release.Set();
                });

                var result = await loginTask.WaitAsync(TestTimeout);
                Assert.True(result.Success);
                Assert.False(sut.IsAuthenticating);
                Assert.NotNull(session.Credential);
                Assert.Equal(uiThread, savedOnThread);
                Assert.All(notificationThreads, id => Assert.Equal(uiThread, id));
                remembered.Verify(s => s.Save(It.IsAny<RememberedUser>()), Times.Once);
            });
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryLoginAsync_AsyncFailure_RestoresUiStateOnDispatcher(bool throws)
    {
        await RunOnDispatcherAsync(async () =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var completion = new TaskCompletionSource<LdapValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var validator = new Mock<ILdapCredentialValidator>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), It.IsAny<CancellationToken>()))
                .Returns(completion.Task);
            var remembered = new Mock<IRememberedUserStore>(MockBehavior.Strict);
            var session = new SessionContext();
            var sut = CreateSut(validator.Object, session, remembered.Object);
            var notificationThreads = new ConcurrentQueue<int>();
            sut.PropertyChanged += (_, _) => notificationThreads.Enqueue(Environment.CurrentManagedThreadId);

            var loginTask = sut.TryLoginAsync();
            Assert.True(sut.IsAuthenticating);
            await Task.Run(() =>
            {
                if (throws)
                    completion.SetException(new InvalidOperationException("Authentication failed."));
                else
                    completion.SetResult(LdapValidationResult.Failure("Authentication failed."));
            });

            if (throws)
                await Assert.ThrowsAsync<InvalidOperationException>(() => loginTask);
            else
            {
                Assert.False((await loginTask).Success);
                Assert.Equal("Authentication failed.", sut.ErrorMessage);
            }

            Assert.False(sut.IsAuthenticating);
            Assert.Null(session.Credential);
            Assert.All(notificationThreads, id => Assert.Equal(uiThread, id));
        });
    }

    [Fact]
    public async Task TryLoginAsync_CanceledBeforeCompletion_DoesNotCreateSessionOrRememberUser()
    {
        await RunOnDispatcherAsync(async () =>
        {
            using var cts = new CancellationTokenSource();
            var completion = new TaskCompletionSource<LdapValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var validator = new Mock<ILdapCredentialValidator>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<NetworkCredential>(), cts.Token))
                .Returns(completion.Task);
            var remembered = new Mock<IRememberedUserStore>(MockBehavior.Strict);
            var session = new SessionContext();
            var sut = CreateSut(validator.Object, session, remembered.Object);
            sut.RememberMe = true;

            var loginTask = sut.TryLoginAsync(cts.Token);
            cts.Cancel();
            completion.SetResult(LdapValidationResult.Success());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loginTask);
            Assert.False(sut.IsAuthenticating);
            Assert.Null(session.Credential);
            Assert.Null(sut.ErrorMessage);
        });
    }

    private static LoginViewModel CreateSut(
        ILdapCredentialValidator validator, SessionContext session, IRememberedUserStore remembered)
    {
        var settings = new Mock<IAppSettingsStore>();
        settings.Setup(s => s.Load()).Returns(new AppSettings("test.example", "ldap.test.example"));
        return new LoginViewModel(validator, session, settings.Object, remembered)
        {
            UserName = "operator",
            Password = "test-password"
        };
    }

    private static Task RunOnDispatcherAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await test();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
