using System.Management;
using System.Runtime.InteropServices;
using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class AccessDeniedDetectorTests
{
    [Fact]
    public void IsAccessDenied_UnauthorizedAccessException_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new UnauthorizedAccessException()));
    }

    [Fact]
    public void IsAccessDenied_InnerUnauthorizedAccess_ReturnsTrue()
    {
        var ex = new InvalidOperationException("wrap", new UnauthorizedAccessException());
        Assert.True(AccessDeniedDetector.IsAccessDenied(ex));
    }

    [Fact]
    public void IsAccessDenied_MessageAcessoNegado_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new Exception("Falha: Acesso negado.")));
    }

    [Fact]
    public void IsAccessDenied_MessageAccessIsDenied_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsAccessDenied(new Exception("Access is denied.")));
    }

    [Fact]
    public void IsAccessDenied_WmiReturnValue5_ReturnsTrue()
    {
        Assert.True(AccessDeniedDetector.IsWmiAccessDeniedReturnValue(5));
        Assert.False(AccessDeniedDetector.IsWmiAccessDeniedReturnValue(0));
    }

    [Fact]
    public void IsAccessDenied_UnrelatedException_ReturnsFalse()
    {
        Assert.False(AccessDeniedDetector.IsAccessDenied(new InvalidOperationException("timeout")));
    }
}
