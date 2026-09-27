using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Tests.TestSupport;

namespace PeripheralAssetTracker.Tests.Fingerprinting;

public class DeviceFingerprintServiceTests
{
    private readonly DeviceFingerprintService _service = new();

    [Fact]
    public void Same_input_produces_same_fingerprint()
    {
        var a = _service.Compute(DeviceFixtures.LogitechWithSerial());
        var b = _service.Compute(DeviceFixtures.LogitechWithSerial());

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(64, a.Fingerprint.Length);
        Assert.Matches("^[0-9A-F]{64}$", a.Fingerprint);
    }

    [Fact]
    public void Fingerprint_is_sha256_of_the_documented_canonical_form()
    {
        var result = _service.Compute(DeviceFixtures.LogitechWithSerial());

        Assert.Equal("v1|bus=USB|vid=046D|pid=C084|serial=ABC123", result.CanonicalForm);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(result.CanonicalForm)));
        Assert.Equal(expected, result.Fingerprint);
    }

    [Fact]
    public void Fingerprint_is_stable_across_runs()
    {
        // Regression guard: this value must never change for algorithm v1.
        var result = _service.Compute(DeviceFixtures.LogitechWithSerial());
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("v1|bus=USB|vid=046D|pid=C084|serial=ABC123"u8)),
            result.Fingerprint);
    }

    [Fact]
    public void Different_serial_produces_different_fingerprint() =>
        Assert.NotEqual(
            _service.Compute(DeviceFixtures.LogitechWithSerial("ABC123")).Fingerprint,
            _service.Compute(DeviceFixtures.LogitechWithSerial("ABC124")).Fingerprint);

    [Fact]
    public void Different_product_id_produces_different_fingerprint() =>
        Assert.NotEqual(
            _service.Compute(DeviceFixtures.DellWithoutSerial()).Fingerprint,
            _service.Compute(DeviceFixtures.DellWithoutSerial() with { ProductId = "2107" }).Fingerprint);

    [Fact]
    public void Different_vendor_id_produces_different_fingerprint() =>
        Assert.NotEqual(
            _service.Compute(DeviceFixtures.LogitechWithSerial()).Fingerprint,
            _service.Compute(DeviceFixtures.LogitechWithSerial() with { VendorId = "046E" }).Fingerprint);

    [Fact]
    public void Case_differences_do_not_change_fingerprint()
    {
        var upper = DeviceFixtures.LogitechWithSerial("ABC123");
        var lower = upper with { VendorId = "046d", ProductId = "c084", SerialNumber = "abc123" };

        Assert.Equal(_service.Compute(upper).Fingerprint, _service.Compute(lower).Fingerprint);
    }

    [Fact]
    public void Irrelevant_spaces_and_prefixes_do_not_change_fingerprint()
    {
        var clean = DeviceFixtures.LogitechWithSerial("ABC123");
        var messy = clean with { VendorId = " 0x046D ", ProductId = "PID_C084 ", SerialNumber = "  ABC123 " };

        Assert.Equal(_service.Compute(clean).Fingerprint, _service.Compute(messy).Fingerprint);
    }

    [Fact]
    public void Localized_names_manufacturer_and_port_do_not_change_fingerprint()
    {
        var ptBr = DeviceFixtures.DellWithoutSerial("5&1A2B3C4D&0&2") with { DeviceName = "Teclado HID", Manufacturer = "(Dispositivos padrão do sistema)" };
        var enUs = DeviceFixtures.DellWithoutSerial("5&99999999&0&7") with { DeviceName = "HID Keyboard Device", Manufacturer = "(Standard keyboards)" };

        Assert.Equal(_service.Compute(ptBr).Fingerprint, _service.Compute(enUs).Fingerprint);
    }

    [Fact]
    public void Serial_present_gives_high_confidence()
    {
        var result = _service.Compute(DeviceFixtures.LogitechWithSerial());

        Assert.Equal(FingerprintConfidence.High, result.Confidence);
    }

    [Fact]
    public void Serial_absent_gives_low_confidence_and_vid_pid_is_not_a_unique_identity()
    {
        var unitA = DeviceFixtures.DellWithoutSerial("5&1A2B3C4D&0&2");
        var unitB = DeviceFixtures.DellWithoutSerial("5&1A2B3C4D&0&3");

        var a = _service.Compute(unitA);
        var b = _service.Compute(unitB);

        Assert.Equal(FingerprintConfidence.Low, a.Confidence);
        Assert.Equal(a.Fingerprint, b.Fingerprint); // identical units cannot be told apart — and that is reported, not hidden
        Assert.Equal("v1|bus=USB|vid=413C|pid=2113", a.CanonicalForm);
    }

    [Theory]
    [InlineData("0000000000")]
    [InlineData("FFFFFFFF")]
    [InlineData("123456789")]
    [InlineData("0123456789")]
    [InlineData("1")]
    [InlineData("A1")]
    public void Generic_serials_give_medium_confidence(string serial)
    {
        var result = _service.Compute(DeviceFixtures.LogitechWithSerial(serial));

        Assert.Equal(FingerprintConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Classic_bluetooth_address_gives_high_confidence()
    {
        var device = new DiscoveredDevice
        {
            SuggestedType = DeviceType.Mouse,
            BusType = BusType.Bluetooth,
            VendorId = "046D",
            ProductId = "B016",
            BluetoothAddress = "E1:C2:D3:A4:B5:C6"
        };

        var result = _service.Compute(device);

        Assert.Equal(FingerprintConfidence.High, result.Confidence);
        Assert.Contains("btaddr=E1C2D3A4B5C6", result.CanonicalForm);
    }

    [Fact]
    public void Bluetooth_le_address_gives_medium_confidence()
    {
        var device = new DiscoveredDevice
        {
            SuggestedType = DeviceType.Keyboard,
            BusType = BusType.BluetoothLowEnergy,
            VendorId = "046D",
            ProductId = "B35B",
            BluetoothAddress = "D1E2F3A4B5C6"
        };

        Assert.Equal(FingerprintConfidence.Medium, _service.Compute(device).Confidence);
    }

    [Fact]
    public void Device_without_vid_pid_uses_hardware_id_without_revision_and_is_low()
    {
        var device = new DiscoveredDevice
        {
            SuggestedType = DeviceType.Keyboard,
            BusType = BusType.Internal,
            HardwareIds = [@"acpi\ven_fuj&dev_7401", @"ACPI\FUJ7401"]
        };

        var result = _service.Compute(device);

        Assert.Equal(FingerprintConfidence.Low, result.Confidence);
        Assert.Equal(@"v1|bus=INTERNAL|hwid=ACPI\VEN_FUJ&DEV_7401", result.CanonicalForm);
    }

    [Fact]
    public void Name_and_manufacturer_are_last_resort_and_normalized()
    {
        var a = new DiscoveredDevice { SuggestedType = DeviceType.Other, BusType = BusType.Unknown, DeviceName = " Some   Device ", Manufacturer = "acme" };
        var b = a with { DeviceName = "SOME DEVICE", Manufacturer = " ACME " };

        var result = _service.Compute(a);

        Assert.Equal(FingerprintConfidence.Low, result.Confidence);
        Assert.Equal(result.Fingerprint, _service.Compute(b).Fingerprint);
    }

    [Fact]
    public void Device_without_any_usable_property_is_rejected() =>
        Assert.Throws<AssetTrackerException>(() =>
            _service.Compute(new DiscoveredDevice { SuggestedType = DeviceType.Other, BusType = BusType.Unknown }));

    [Fact]
    public void Structural_characters_cannot_forge_another_canonical_form()
    {
        var injected = DeviceFixtures.LogitechWithSerial("X|pid=0000");
        Assert.Equal("v1|bus=USB|vid=046D|pid=C084|serial=X%7CPID%3D0000", _service.Compute(injected).CanonicalForm);
    }
}
