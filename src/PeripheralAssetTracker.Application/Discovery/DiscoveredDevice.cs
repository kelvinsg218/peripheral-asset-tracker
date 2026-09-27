using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Discovery;

/// <summary>
/// One PHYSICAL peripheral as reconstructed from the (possibly many) Windows PnP/HID entries it exposes.
/// Values the platform did not provide are null — they are never invented.
/// </summary>
public sealed record DiscoveredDevice
{
    public required DeviceType SuggestedType { get; init; }
    public required BusType BusType { get; init; }

    /// <summary>Best available name (USB product string when available; otherwise the Windows description).</summary>
    public string? DeviceName { get; init; }

    public string? Manufacturer { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }

    /// <summary>USB iSerialNumber as exposed by Windows in the device instance id. Null when absent.</summary>
    public string? SerialNumber { get; init; }

    public string? BluetoothAddress { get; init; }

    /// <summary>Device instance id (== PNP Device ID) of the physical node.</summary>
    public string? PnpDeviceId { get; init; }

    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    /// <summary>Instance ids of every PnP/HID entry merged into this device.</summary>
    public IReadOnlyList<string> InterfaceInstanceIds { get; init; } = [];

    /// <summary>HID functions exposed (Mouse, Keyboard, ConsumerControl, ...).</summary>
    public IReadOnlyList<string> Functions { get; init; } = [];

    public string? ContainerId { get; init; }

    /// <summary>Part of the computer itself (laptop keyboard/touchpad).</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Discovery remarks (ambiguities, limitations) to be shown to the technician.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record DeviceDiscoveryResult(
    IReadOnlyList<DiscoveredDevice> Devices,
    int RawEntryCount,
    int IgnoredVirtualEntryCount,
    IReadOnlyList<string> Warnings)
{
    public static DeviceDiscoveryResult Empty { get; } = new([], 0, 0, []);
}
