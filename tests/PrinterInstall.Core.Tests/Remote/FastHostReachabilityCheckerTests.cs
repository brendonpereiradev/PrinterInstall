using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class FastHostReachabilityCheckerTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData(".")]
    public async Task CheckReachabilityAsync_LocalHost_ReturnsTrueImmediately(string localName)
    {
        var sut = new FastHostReachabilityChecker(TimeSpan.FromMilliseconds(500));
        var (isReachable, error) = await sut.CheckReachabilityAsync(localName);

        Assert.True(isReachable);
        Assert.Null(error);
    }

    [Fact]
    public async Task CheckReachabilityAsync_EmptyHost_ReturnsFalse()
    {
        var sut = new FastHostReachabilityChecker(TimeSpan.FromMilliseconds(500));
        var (isReachable, error) = await sut.CheckReachabilityAsync("   ");

        Assert.False(isReachable);
        Assert.Contains("vazio", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckReachabilityAsync_UnreachableHost_ReturnsFalseWithDetail()
    {
        // IP de documentação/não roteável RFC 5737
        var sut = new FastHostReachabilityChecker(TimeSpan.FromMilliseconds(100));
        var (isReachable, error) = await sut.CheckReachabilityAsync("192.0.2.1");

        Assert.False(isReachable);
        Assert.Contains("inacessível", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NullChecker_AlwaysReturnsTrue()
    {
        var sut = new NullFastHostReachabilityChecker();
        var (isReachable, error) = await sut.CheckReachabilityAsync("any-host");

        Assert.True(isReachable);
        Assert.Null(error);
    }
}
