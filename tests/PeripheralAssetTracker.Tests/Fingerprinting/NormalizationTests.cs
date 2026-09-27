using PeripheralAssetTracker.Application.Fingerprinting;

namespace PeripheralAssetTracker.Tests.Fingerprinting;

public class NormalizationTests
{
    [Theory]
    [InlineData("046D", "046D")]
    [InlineData("046d", "046D")]
    [InlineData(" 046d ", "046D")]
    [InlineData("0x046D", "046D")]
    [InlineData("VID_046D", "046D")]
    [InlineData("vid_046d", "046D")]
    [InlineData("PID&C084", "C084")]
    [InlineData("46d", "046D")]
    public void Hex_ids_are_normalized(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.NormalizeHexId(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("12345")]
    [InlineData("XYZ1")]
    public void Invalid_or_empty_hex_ids_become_null(string? input) =>
        Assert.Null(DeviceIdentifierNormalizer.NormalizeHexId(input));

    [Theory]
    [InlineData("abc123", "ABC123")]
    [InlineData("  ABC123  ", "ABC123")]
    [InlineData("ABC 123", "ABC123")]
    [InlineData("abc\t123", "ABC123")]
    public void Serials_ignore_case_and_whitespace(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.NormalizeSerial(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("None")]
    [InlineData("unknown")]
    [InlineData("N/A")]
    [InlineData("Default string")]
    public void Placeholder_serials_are_treated_as_absent(string? input) =>
        Assert.Null(DeviceIdentifierNormalizer.NormalizeSerial(input));

    [Theory]
    [InlineData(@"usb\vid_046d&pid_c084\abc123", @"USB\VID_046D&PID_C084\ABC123")]
    [InlineData(@"  USB\VID_046D & PID_C084\ABC123 ", @"USB\VID_046D&PID_C084\ABC123")]
    [InlineData("USB/VID_046D&PID_C084/ABC123", @"USB\VID_046D&PID_C084\ABC123")]
    public void Pnp_ids_are_normalized(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.NormalizePnpId(input));

    [Fact]
    public void Hardware_ids_are_normalized_deduplicated_and_keep_order()
    {
        var result = DeviceIdentifierNormalizer.NormalizeHardwareIds(
            [@"usb\vid_046d&pid_c084&rev_7200", "", null, @"USB\VID_046D&PID_C084", @"USB\vid_046d&pid_c084"]);

        Assert.Equal([@"USB\VID_046D&PID_C084&REV_7200", @"USB\VID_046D&PID_C084"], result);
    }

    [Fact]
    public void Revision_is_stripped_from_hardware_ids() =>
        Assert.Equal(@"USB\VID_046D&PID_C084", DeviceIdentifierNormalizer.StripRevision(@"USB\VID_046D&PID_C084&REV_7200"));

    [Theory]
    [InlineData("  Logitech   G203 ", "Logitech G203")]
    [InlineData("Logitech\tG203", "Logitech G203")]
    public void Text_is_trimmed_and_collapsed_keeping_case(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.CleanText(input));

    [Theory]
    [InlineData("logitech g203", "LOGITECH G203")]
    [InlineData("  LOGITECH    G203", "LOGITECH G203")]
    public void Text_comparison_form_ignores_case_and_spaces(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.NormalizeText(input));

    [Theory]
    [InlineData("(Standard system devices)")]
    [InlineData("(Dispositivos padrão do sistema)")]
    [InlineData("(teclados padrões)")]
    [InlineData("(Standard USB Host Controller)")]
    [InlineData("  ")]
    public void Generic_driver_manufacturers_are_discarded(string input) =>
        Assert.Null(DeviceIdentifierNormalizer.NormalizeManufacturer(input));

    [Fact]
    public void Real_manufacturer_is_kept_and_cleaned() =>
        Assert.Equal("Logitech", DeviceIdentifierNormalizer.NormalizeManufacturer("  Logitech "));

    [Theory]
    [InlineData("E1:C2:D3:A4:B5:C6", "E1C2D3A4B5C6")]
    [InlineData("e1-c2-d3-a4-b5-c6", "E1C2D3A4B5C6")]
    [InlineData("e1c2d3a4b5c6", "E1C2D3A4B5C6")]
    public void Bluetooth_addresses_are_normalized(string input, string expected) =>
        Assert.Equal(expected, DeviceIdentifierNormalizer.NormalizeBluetoothAddress(input));

    [Theory]
    [InlineData("000000000000")]
    [InlineData("E1C2D3")]
    [InlineData("ZZC2D3A4B5C6")]
    public void Invalid_bluetooth_addresses_are_rejected(string input) =>
        Assert.Null(DeviceIdentifierNormalizer.NormalizeBluetoothAddress(input));
}
