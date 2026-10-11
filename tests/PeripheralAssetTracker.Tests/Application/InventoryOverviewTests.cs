using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Tests.TestSupport;

namespace PeripheralAssetTracker.Tests.Application;

public class InventoryOverviewTests
{
    private const int Here = 1;
    private const int Elsewhere = 2;
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Presence_is_unknown_before_any_scan()
    {
        var device = Summary(1, expected: Here);

        Assert.Equal(DevicePresence.Unknown, InventoryOverviewCalculator.EvaluatePresence(device, null, Here));
    }

    [Fact]
    public void Presence_is_connected_when_the_latest_scan_found_the_device()
    {
        var device = Summary(1, expected: Elsewhere);
        var scan = Scan(Here, Item("A", registered: device));

        Assert.Equal(DevicePresence.Connected, InventoryOverviewCalculator.EvaluatePresence(device, scan, Here));
    }

    [Fact]
    public void Presence_is_disconnected_only_for_devices_that_belong_to_this_computer()
    {
        var scan = Scan(Here);

        Assert.Equal(DevicePresence.Disconnected,
            InventoryOverviewCalculator.EvaluatePresence(Summary(1, expected: Here), scan, Here));
        Assert.Equal(DevicePresence.Disconnected,
            InventoryOverviewCalculator.EvaluatePresence(Summary(2, expected: null, lastSeen: Here), scan, Here));
    }

    [Fact]
    public void Device_of_another_computer_is_never_reported_as_disconnected()
    {
        var scan = Scan(Here);

        // Expected elsewhere wins over "last seen here": it was probably moved on purpose.
        Assert.Equal(DevicePresence.Unknown,
            InventoryOverviewCalculator.EvaluatePresence(Summary(1, expected: Elsewhere, lastSeen: Here), scan, Here));
        Assert.Equal(DevicePresence.Unknown,
            InventoryOverviewCalculator.EvaluatePresence(Summary(2, expected: null, lastSeen: Elsewhere), scan, Here));
    }

    [Fact]
    public void Presence_is_unknown_when_the_scan_belongs_to_another_computer()
    {
        var device = Summary(1, expected: Here);

        Assert.Equal(DevicePresence.Unknown, InventoryOverviewCalculator.EvaluatePresence(device, Scan(Elsewhere), Here));
    }

    [Fact]
    public void Overview_counts_presence_categories_conditions_and_new_units()
    {
        var connected = Summary(1, expected: Here, type: DeviceType.Mouse, created: Now.AddDays(-1));
        var disconnected = Summary(2, expected: Here, type: DeviceType.Keyboard, created: Now.AddDays(-30),
            conditions: DeviceConditions.Unassigned);
        var elsewhere = Summary(3, expected: Elsewhere, type: DeviceType.Other, created: Now.AddDays(-2),
            confidence: FingerprintConfidence.Low, conditions: DeviceConditions.LowConfidence | DeviceConditions.UnexpectedComputer);
        var scan = Scan(Here,
            Item("A", registered: connected),
            Item("NEW"),
            Item("BUILTIN", builtIn: true),
            Item("TWIN", conditions: DeviceConditions.AmbiguousFingerprint),
            Item("TWIN", conditions: DeviceConditions.AmbiguousFingerprint));

        var overview = InventoryOverviewCalculator.Calculate([connected, disconnected, elsewhere], scan, Here, Now);

        Assert.Equal(new InventoryOverview(
            Total: 3, Connected: 1, Disconnected: 1, Unknown: 1,
            NewDetected: 3, RecentlyRegistered: 2,
            Mice: 1, Keyboards: 1, Others: 1,
            Unassigned: 1, LowConfidence: 1, UnexpectedComputer: 1, AmbiguousFingerprint: 2), overview);
    }

    [Fact]
    public void Compare_scans_reports_connections_and_disconnections()
    {
        var previous = Scan(Here, Item("KEEP"), Item("GONE"));
        var current = Scan(Here, Item("KEEP"), Item("ADDED"));

        var changes = InventoryOverviewCalculator.CompareScans(previous, current);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Connected && c.Item.Fingerprint.Fingerprint == "ADDED");
        Assert.Contains(changes, c => !c.Connected && c.Item.Fingerprint.Fingerprint == "GONE");
    }

    [Fact]
    public void Compare_scans_counts_identical_units()
    {
        var previous = Scan(Here, Item("TWIN"), Item("TWIN"));
        var current = Scan(Here, Item("TWIN"));

        var change = Assert.Single(InventoryOverviewCalculator.CompareScans(previous, current));

        Assert.False(change.Connected);
        Assert.Empty(InventoryOverviewCalculator.CompareScans(current, current));
    }

    internal static DeviceSummary Summary(
        int id,
        int? expected,
        int? lastSeen = null,
        DeviceType type = DeviceType.Mouse,
        DateTime? created = null,
        FingerprintConfidence confidence = FingerprintConfidence.High,
        DeviceConditions conditions = DeviceConditions.None) => new()
    {
        Id = id,
        Fingerprint = $"FP{id}",
        Confidence = confidence,
        FingerprintBasis = "test",
        DeviceType = type,
        DeviceName = $"Device {id}",
        ExpectedComputerId = expected,
        LastSeenComputerId = lastSeen ?? expected,
        CreatedAt = created ?? Now,
        FirstSeenAt = created ?? Now,
        LastSeenAt = Now,
        Conditions = conditions
    };

    internal static ScanItem Item(
        string fingerprint, DeviceSummary? registered = null, bool builtIn = false, DeviceConditions conditions = DeviceConditions.None) =>
        new(
            DeviceFixtures.LogitechWithSerial() with { IsBuiltIn = builtIn, DeviceName = $"Unit {fingerprint}" },
            new FingerprintResult(registered?.Fingerprint ?? fingerprint, FingerprintConfidence.High, "test", "test"),
            registered,
            registered is null ? DeviceConditions.NotRegistered | conditions : conditions,
            1);

    internal static ScanReport Scan(int computerId, params ScanItem[] items) =>
        new(computerId, "PC", Now, items, 0, new DeviceDiscoveryResult([], 0, 0, []));
}
