namespace PrinterInstall.Core.Gainscha;

internal static class GainschaLabelUserFormBinary
{
    internal static bool TryFindUserFormDimensionsMm(
        ReadOnlySpan<byte> buffer,
        int expectedWidthMm,
        int expectedHeightMm)
    {
        if (buffer.Length < 8)
            return false;

        var widthBytes = BitConverter.GetBytes((uint)(expectedWidthMm * 1000));
        var heightBytes = BitConverter.GetBytes((uint)(expectedHeightMm * 1000));

        for (var i = 0; i <= buffer.Length - 8; i++)
        {
            if (buffer[i] != widthBytes[0] || buffer[i + 1] != widthBytes[1] ||
                buffer[i + 2] != widthBytes[2] || buffer[i + 3] != widthBytes[3])
            {
                continue;
            }

            if (buffer[i + 4] == heightBytes[0] && buffer[i + 5] == heightBytes[1] &&
                buffer[i + 6] == heightBytes[2] && buffer[i + 7] == heightBytes[3])
            {
                return true;
            }
        }

        return false;
    }
}
