using System;
using Moq;
using PrinterInstall.App.Services;
using Xunit;

namespace PrinterInstall.App.Tests.Services;

public class DeploymentNotificationServiceTests
{
    [Fact]
    public void NotifySuccess_DoesNotThrow()
    {
        var sut = new DeploymentNotificationService();
        var ex = Record.Exception(() => sut.NotifySuccess());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyWarning_DoesNotThrow()
    {
        var sut = new DeploymentNotificationService();
        var ex = Record.Exception(() => sut.NotifyWarning());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyError_DoesNotThrow()
    {
        var sut = new DeploymentNotificationService();
        var ex = Record.Exception(() => sut.NotifyError());
        Assert.Null(ex);
    }

    [Fact]
    public void Implements_IDeploymentNotificationService()
    {
        var sut = new DeploymentNotificationService();
        Assert.IsAssignableFrom<IDeploymentNotificationService>(sut);
    }
}
