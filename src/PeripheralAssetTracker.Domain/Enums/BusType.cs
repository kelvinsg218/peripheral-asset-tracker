namespace PeripheralAssetTracker.Domain.Enums;

/// <summary>How the peripheral is attached to the computer.</summary>
public enum BusType
{
    Unknown,
    Usb,
    Bluetooth,
    BluetoothLowEnergy,

    /// <summary>Part of the computer itself (ACPI/I2C/PS2: laptop keyboard, touchpad).</summary>
    Internal
}
