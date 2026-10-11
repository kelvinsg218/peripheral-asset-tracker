using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>Whether a registered device is plugged into the CURRENT computer, according to the latest scan.</summary>
public enum DevicePresence
{
    /// <summary>No scan yet, or the device belongs to another computer: this computer cannot confirm either way.</summary>
    Unknown,

    /// <summary>Detected by the latest scan of this computer.</summary>
    Connected,

    /// <summary>Expected on this computer (or, without an expected computer, last seen here) but absent from the latest scan.</summary>
    Disconnected
}

public sealed record InventoryOverview(
    int Total,
    int Connected,
    int Disconnected,
    int Unknown,
    int NewDetected,
    int RecentlyRegistered,
    int Mice,
    int Keyboards,
    int Others,
    int Unassigned,
    int LowConfidence,
    int UnexpectedComputer,
    int AmbiguousFingerprint);

/// <summary>A unit that appeared (Connected = true) or disappeared between two scans of the same computer.</summary>
public sealed record ScanChange(ScanItem Item, bool Connected);

/// <summary>
/// Dashboard numbers derived from the inventory and the latest scan. Presence is only claimed when a scan of
/// this computer can confirm it; a device that belongs to another computer is never reported as disconnected.
/// </summary>
public static class InventoryOverviewCalculator
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    public static DevicePresence EvaluatePresence(DeviceSummary device, ScanReport? latestScan, int currentComputerId) =>
        EvaluatePresence(device, latestScan, currentComputerId, ConnectedIds(latestScan));

    public static InventoryOverview Calculate(
        IReadOnlyList<DeviceSummary> devices, ScanReport? latestScan, int currentComputerId, DateTime nowUtc)
    {
        var connectedIds = ConnectedIds(latestScan);
        var presence = devices.Select(d => EvaluatePresence(d, latestScan, currentComputerId, connectedIds)).ToList();
        var scanItems = latestScan?.Items ?? [];

        return new InventoryOverview(
            Total: devices.Count,
            Connected: presence.Count(p => p == DevicePresence.Connected),
            Disconnected: presence.Count(p => p == DevicePresence.Disconnected),
            Unknown: presence.Count(p => p == DevicePresence.Unknown),
            // Built-in parts (laptop keyboard/touchpad) are not new peripherals.
            NewDetected: scanItems.Count(i => !i.IsRegistered && !i.Discovered.IsBuiltIn),
            RecentlyRegistered: devices.Count(d => d.CreatedAt >= nowUtc - RecentWindow),
            Mice: devices.Count(d => d.DeviceType == DeviceType.Mouse),
            Keyboards: devices.Count(d => d.DeviceType == DeviceType.Keyboard),
            Others: devices.Count(d => d.DeviceType == DeviceType.Other),
            Unassigned: devices.Count(d => d.Conditions.HasFlag(DeviceConditions.Unassigned)),
            LowConfidence: devices.Count(d => d.Confidence == FingerprintConfidence.Low),
            UnexpectedComputer: devices.Count(d => d.Conditions.HasFlag(DeviceConditions.UnexpectedComputer)),
            AmbiguousFingerprint: scanItems.Count(i => i.Conditions.HasFlag(DeviceConditions.AmbiguousFingerprint)));
    }

    /// <summary>
    /// Units connected or disconnected between two scans. Units are matched by fingerprint and counted, so two
    /// identical LOW-confidence units are still reported when only one of them is unplugged.
    /// </summary>
    public static IReadOnlyList<ScanChange> CompareScans(ScanReport previous, ScanReport current)
    {
        var before = CountByFingerprint(previous);
        var after = CountByFingerprint(current);
        var changes = new List<ScanChange>();

        foreach (var (fingerprint, (item, count)) in after)
        {
            var added = count - (before.TryGetValue(fingerprint, out var old) ? old.Count : 0);
            changes.AddRange(Enumerable.Repeat(new ScanChange(item, Connected: true), Math.Max(0, added)));
        }

        foreach (var (fingerprint, (item, count)) in before)
        {
            var removed = count - (after.TryGetValue(fingerprint, out var now) ? now.Count : 0);
            changes.AddRange(Enumerable.Repeat(new ScanChange(item, Connected: false), Math.Max(0, removed)));
        }

        return changes;
    }

    private static DevicePresence EvaluatePresence(
        DeviceSummary device, ScanReport? latestScan, int currentComputerId, IReadOnlySet<int> connectedIds)
    {
        if (latestScan is null || latestScan.ComputerId != currentComputerId)
        {
            return DevicePresence.Unknown;
        }

        if (connectedIds.Contains(device.Id))
        {
            return DevicePresence.Connected;
        }

        var belongsHere = device.ExpectedComputerId is { } expected
            ? expected == currentComputerId
            : device.LastSeenComputerId == currentComputerId;
        return belongsHere ? DevicePresence.Disconnected : DevicePresence.Unknown;
    }

    private static HashSet<int> ConnectedIds(ScanReport? scan) =>
        scan?.Items.Where(i => i.Registered is not null).Select(i => i.Registered!.Id).ToHashSet() ?? [];

    private static Dictionary<string, (ScanItem Item, int Count)> CountByFingerprint(ScanReport scan) =>
        scan.Items
            .GroupBy(i => i.Fingerprint.Fingerprint)
            .ToDictionary(g => g.Key, g => (g.First(), g.Count()));
}
