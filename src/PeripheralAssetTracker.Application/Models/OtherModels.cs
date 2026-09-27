using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Models;

public sealed record ComputerSummary(
    int Id,
    string Hostname,
    string? WindowsVersion,
    DateTime? FirstSeenAt,
    DateTime? LastSeenAt,
    int AssignedDeviceCount,
    int RecentlyObservedDeviceCount);

public sealed record PersonSummary(int Id, string Name, string? Department, DateTime CreatedAt, int ActiveDeviceCount);

public sealed record ScanItem(
    DiscoveredDevice Discovered,
    FingerprintResult Fingerprint,
    DeviceSummary? Registered,
    DeviceConditions Conditions,
    int SharedFingerprintCount)
{
    public bool IsRegistered => Registered is not null;
}

public sealed record ScanReport(
    int ComputerId,
    string Hostname,
    DateTime ScannedAt,
    IReadOnlyList<ScanItem> Items,
    int HiddenBuiltInCount,
    DeviceDiscoveryResult Discovery);

public enum HistoryEntryKind
{
    Registered,
    Assigned,
    Transferred,
    Observed
}

public sealed record HistoryEntry(
    DateTime At,
    HistoryEntryKind Kind,
    string? FromPerson,
    string? FromComputer,
    string? ToPerson,
    string? ToComputer,
    string? Reason,
    DateTime? ObservedUntil = null,
    int ScanCount = 0,
    bool UnexpectedComputer = false);

public sealed record DeviceHistory(DeviceSummary Device, IReadOnlyList<HistoryEntry> Entries);

public sealed record MovementSummary(
    int Id,
    int DeviceId,
    string DeviceName,
    MovementType Type,
    string? FromPerson,
    string? FromComputer,
    string? ToPerson,
    string? ToComputer,
    DateTime ChangedAt,
    string Reason);

public sealed record RegisterDeviceRequest(
    DeviceType DeviceType,
    string? FriendlyName,
    int? ResponsiblePersonId,
    int? ExpectedComputerId);

public sealed record TransferRequest(
    int DeviceId,
    int? NewResponsiblePersonId,
    int? NewExpectedComputerId,
    string Reason);
