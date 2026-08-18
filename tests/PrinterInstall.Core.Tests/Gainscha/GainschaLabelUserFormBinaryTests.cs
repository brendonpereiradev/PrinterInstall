using PrinterInstall.Core.Gainscha;

namespace PrinterInstall.Core.Tests.Gainscha;

public class GainschaLabelUserFormBinaryTests
{
    [Fact]
    public void TryFindUserFormDimensionsMm_FindsPacientePattern()
    {
        var width = BitConverter.GetBytes((uint)89000);
        var height = BitConverter.GetBytes((uint)36000);
        var buffer = width.Concat(height).ToArray();

        Assert.True(GainschaLabelUserFormBinary.TryFindUserFormDimensionsMm(buffer, 89, 36));
    }

    [Fact]
    public void TryFindUserFormDimensionsMm_RejectsWrongDimensions()
    {
        var width = BitConverter.GetBytes((uint)101600);
        var height = BitConverter.GetBytes((uint)101600);
        var buffer = new byte[] { width[0], width[1], width[2], width[3], height[0], height[1], height[2], height[3] };

        Assert.False(GainschaLabelUserFormBinary.TryFindUserFormDimensionsMm(buffer, 89, 36));
    }
}
