using PrinterInstall.Core.Validation;

namespace PrinterInstall.Core.Tests.Validation;

public class PrinterHostValidatorTests
{
    [Theory]
    [InlineData("10.1.152.218")]
    [InlineData("192.168.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("  10.0.0.1  ")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void IsStrictIpAddress_ValidIPv4_ReturnsTrue(string ip)
    {
        Assert.True(PrinterHostValidator.IsStrictIpAddress(ip));
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    public void IsStrictIpAddress_ValidIPv6_ReturnsTrue(string ip)
    {
        Assert.True(PrinterHostValidator.IsStrictIpAddress(ip));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Multifuncional")]
    [InlineData("Consultório 6")]
    [InlineData("10.1.152.256")]
    [InlineData("10.1.152")]
    [InlineData("10.1.152.1.2")]
    [InlineData("10.1.152.abc")]
    [InlineData("printer-01")]
    public void IsStrictIpAddress_InvalidIp_ReturnsFalse(string? input)
    {
        Assert.False(PrinterHostValidator.IsStrictIpAddress(input));
    }

    [Theory]
    [InlineData("10.1.152.218")]
    [InlineData("192.168.1.1")]
    [InlineData("printer01")]
    [InlineData("printer-sec.prevent.local")]
    [InlineData("prt-01")]
    [InlineData("PRINTER-HQ")]
    public void IsValidHostAddress_ValidInputs_ReturnsTrue(string host)
    {
        Assert.True(PrinterHostValidator.IsValidHostAddress(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Consultório 6")]
    [InlineData("printer name with spaces")]
    [InlineData("10.1.152.256")]
    [InlineData("10.1.152.999")]
    [InlineData("-invalid-start")]
    [InlineData("invalid-end-")]
    [InlineData("invalid..double-dot")]
    [InlineData("printer@domain")]
    public void IsValidHostAddress_InvalidInputs_ReturnsFalse(string? host)
    {
        Assert.False(PrinterHostValidator.IsValidHostAddress(host));
    }

    [Theory]
    [InlineData("10.1.152.218", "Multifuncional", true)]
    [InlineData("10.1.152.11", "Consultório 6", true)]
    [InlineData("192.168.0.50", "Recepção", true)]
    [InlineData("Multifuncional", "10.1.152.218", false)]
    [InlineData("Consultório 6", "10.1.152.11", false)]
    [InlineData("Recepção", "192.168.0.50", false)]
    [InlineData("10.1.152.218", "10.1.152.218", false)]
    [InlineData("Printer1", "printer-srv01", false)]
    [InlineData("", "10.1.152.218", false)]
    [InlineData("10.1.152.218", "", false)]
    [InlineData(null, "10.1.152.218", false)]
    [InlineData("10.1.152.218", null, false)]
    [InlineData("   ", "10.1.152.218", false)]
    public void DetectProbableInversion_CorrectlyIdentifiesInversion(string? displayName, string? hostAddress, bool expected)
    {
        var result = PrinterHostValidator.DetectProbableInversion(displayName, hostAddress);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidHostAddress_HostnameExceeding253Chars_ReturnsFalse()
    {
        // Cria hostname válido sintaticamente mas com mais de 253 caracteres
        var longHostname = string.Join(".", Enumerable.Repeat("a", 130)); // 130 * 2 = 260 chars
        Assert.True(longHostname.Length > 253);
        Assert.False(PrinterHostValidator.IsValidHostAddress(longHostname));
    }

    [Fact]
    public void IsValidHostAddress_HostnameUpTo253Chars_ReturnsTrue()
    {
        // 63 chars por label, 4 labels = 252 + 3 = 255 chars. Criamos um de 250 chars.
        var label = new string('a', 50);
        var validHostname = $"{label}.{label}.{label}.{label}";
        Assert.True(validHostname.Length <= 253);
        Assert.True(PrinterHostValidator.IsValidHostAddress(validHostname));
    }
}
