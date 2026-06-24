using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class LocalMachineIdentityTests
{
    private readonly LocalMachineIdentity _sut = new();

    [Fact]
    public void IsLocalMachine_EmptyOrWhitespace_ReturnsFalse()
    {
        Assert.False(_sut.IsLocalMachine(""));
        Assert.False(_sut.IsLocalMachine("   "));
    }

    [Fact]
    public void IsLocalMachine_MachineName_ReturnsTrue()
    {
        Assert.True(_sut.IsLocalMachine(Environment.MachineName));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData(".")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void IsLocalMachine_Literals_ReturnTrue(string name)
    {
        Assert.True(_sut.IsLocalMachine(name));
    }

    [Fact]
    public void IsLocalMachine_MachineNameCaseInsensitive_ReturnsTrue()
    {
        Assert.True(_sut.IsLocalMachine(Environment.MachineName.ToUpperInvariant()));
        Assert.True(_sut.IsLocalMachine(Environment.MachineName.ToLowerInvariant()));
    }

    [Fact]
    public void IsLocalMachine_UnknownRemoteName_ReturnsFalse()
    {
        Assert.False(_sut.IsLocalMachine("definitely-not-this-pc-xyz-99999"));
        Assert.False(_sut.IsLocalMachine("10.255.255.254"));
    }

    [Fact]
    public void GetPrimaryLocalName_ReturnsEnvironmentMachineName()
    {
        Assert.Equal(Environment.MachineName, _sut.GetPrimaryLocalName());
    }
}
