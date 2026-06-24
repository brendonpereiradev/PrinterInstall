using PrinterInstall.Core.Remote;

namespace PrinterInstall.Core.Tests.Remote;

public class RemoteHostSessionFactoryTests
{
    [Theory]
    [InlineData("ELEVATION_PROBE>> TRUE", false)]
    [InlineData("ELEVATION_PROBE>> FALSE", true)]
    [InlineData("noise\nELEVATION_PROBE>> FALSE\n", true)]
    public void ParseElevationProbeOutput_DetectsFilteredToken(string output, bool requiresElevated)
    {
        var result = RemoteHostSessionFactory.ParseElevationProbeOutput(output);
        Assert.Equal(requiresElevated, result);
    }

    [Theory]
    [InlineData("PC01", "PC01")]
    [InlineData("  PC01  ", "PC01")]
    public void NormalizeHostKey_IsCaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, RemoteHostSessionFactory.NormalizeHostKey(input));
    }

    [Fact]
    public void ParseElevationProbeOutput_EmptyOutput_AssumesFiltered()
    {
        Assert.True(RemoteHostSessionFactory.ParseElevationProbeOutput(string.Empty));
    }
}
