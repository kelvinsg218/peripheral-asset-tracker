using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Models;

/// <summary>Read model of a registered device with its current official and observed situation.</summary>
public sealed record DeviceSummary
{
    public required int Id { get; init; }
    public required string Fingerprint { get; init; }
    public required FingerprintConfidence Confidence { get; init; }
    public required string FingerprintBasis { get; init; }
    public required DeviceType DeviceType { get; init; }
    public required string DeviceName { get; init; }
    public string? Manufacturer { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }
    public string? SerialNumber { get; init; }
    public string? BluetoothAddress { get; init; }
    public BusType BusType { get; init; }
    public string? PnpDeviceId { get; init; }
    public int? ResponsiblePersonId { get; init; }
    public string? ResponsiblePersonName { get; init; }
    public string? ResponsibleDepartment { get; init; }
    public int? ExpectedComputerId { get; init; }
    public string? ExpectedComputerHostname { get; init; }
    public DateTime? AssignedAt { get; init; }
    public int? LastSeenComputerId { get; init; }
    public string? LastSeenComputerHostname { get; init; }
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public required DeviceConditions Conditions { get; init; }

    public bool HasActiveAssignment => ResponsiblePersonId is not null || ExpectedComputerId is not null;
}

public sealed record DeviceFilter
{
    public static DeviceFilter All { get; } = new();

    public DeviceType? Type { get; init; }
    public int? ResponsiblePersonId { get; init; }

    /// <summary>Devices expected on OR last seen on this computer.</summary>
    public int? ComputerId { get; init; }

    public bool OnlyUnassigned { get; init; }
    public bool OnlyLowConfidence { get; init; }
    public bool OnlyUnexpectedComputer { get; init; }
}
