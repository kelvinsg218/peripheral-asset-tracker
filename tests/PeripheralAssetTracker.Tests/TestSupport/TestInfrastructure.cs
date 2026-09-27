using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Domain.Services;
using PeripheralAssetTracker.Infrastructure.Discovery;
using PeripheralAssetTracker.Infrastructure.Persistence;

namespace PeripheralAssetTracker.Tests.TestSupport;

/// <summary>Deterministic clock.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Now += by;
}

public sealed class FakeDiscovery : IDeviceDiscoveryService
{
    public List<DiscoveredDevice> Devices { get; } = [];

    public Exception? Failure { get; set; }

    public Task<DeviceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Failure is not null
            ? Task.FromException<DeviceDiscoveryResult>(Failure)
            : Task.FromResult(new DeviceDiscoveryResult(Devices.ToList(), Devices.Count, 0, []));
}

public sealed class FakeMachineInfo(string hostname, string? windowsVersion = "Windows 11 Pro 24H2 (build 26100.1)")
    : IMachineInfoProvider
{
    public string Hostname { get; set; } = hostname;

    public string GetHostname() => Hostname;

    public string? GetWindowsVersion() => windowsVersion;
}

/// <summary>Values from the specification — test data only, not real devices.</summary>
public static class DeviceFixtures
{
    public static DiscoveredDevice LogitechWithSerial(string serial = "ABC123") => new()
    {
        SuggestedType = DeviceType.Mouse,
        BusType = BusType.Usb,
        DeviceName = "Logitech G203",
        Manufacturer = "Logitech",
        VendorId = "046D",
        ProductId = "C084",
        SerialNumber = serial,
        PnpDeviceId = $@"USB\VID_046D&PID_C084\{serial}",
        HardwareIds = [@"USB\VID_046D&PID_C084&REV_7200", @"USB\VID_046D&PID_C084"]
    };

    public static DiscoveredDevice DellWithoutSerial(string portInstance = "5&1A2B3C4D&0&2") => new()
    {
        SuggestedType = DeviceType.Keyboard,
        BusType = BusType.Usb,
        DeviceName = "Dell KB216",
        Manufacturer = "Dell",
        VendorId = "413C",
        ProductId = "2113",
        SerialNumber = null,
        PnpDeviceId = $@"USB\VID_413C&PID_2113\{portInstance}",
        HardwareIds = [@"USB\VID_413C&PID_2113&REV_0110", @"USB\VID_413C&PID_2113"]
    };

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static PnpSnapshot LoadSnapshot(string name) => PnpSnapshot.FromJson(File.ReadAllText(FixturePath(name)));
}

/// <summary>
/// Isolated SQLite database in memory with the REAL migrations applied (no real database file).
/// The connection stays open for the lifetime of the test so the in-memory database survives.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase(DateTimeOffset? start = null)
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();
        using var db = NewContext();
        db.Database.Migrate();
        Clock = new ManualClock(start ?? new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    }

    public ManualClock Clock { get; }
    public FakeDiscovery Discovery { get; } = new();
    public DeviceFingerprintService Fingerprints { get; } = new();

    public AssetTrackerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AssetTrackerDbContext>().UseSqlite(_connection).Options);

    public ComputerService Computers(IAssetTrackerDbContext db, string hostname) =>
        new(db, new FakeMachineInfo(hostname), Clock, NullLogger<ComputerService>.Instance);

    public async Task<Computer> AddComputerAsync(string hostname)
    {
        await using var db = NewContext();
        return await Computers(db, hostname).RegisterCurrentComputerAsync();
    }

    public async Task<ResponsiblePerson> AddPersonAsync(string name, string? department = null)
    {
        await using var db = NewContext();
        return await new PeopleService(db, Clock, NullLogger<PeopleService>.Instance).CreateAsync(name, department);
    }

    public ScanService Scan(IAssetTrackerDbContext db, TimeSpan? window = null) =>
        new(db, Discovery, Fingerprints, new ObservationPolicy(window ?? ObservationPolicy.DefaultWindow), Clock,
            NullLogger<ScanService>.Instance);

    public DeviceRegistrationService Registration(IAssetTrackerDbContext db) =>
        new(db, Fingerprints, Clock, NullLogger<DeviceRegistrationService>.Instance);

    public AssignmentService Assignments(IAssetTrackerDbContext db) =>
        new(db, Clock, NullLogger<AssignmentService>.Instance);

    public async Task<Device> RegisterAsync(
        DiscoveredDevice device, int observedComputerId, int? personId = null, int? computerId = null,
        DeviceType? type = null)
    {
        await using var db = NewContext();
        return await Registration(db).RegisterAsync(
            device,
            new RegisterDeviceRequest(type ?? device.SuggestedType, null, personId, computerId),
            observedComputerId);
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>Wraps a real context and fails on the N-th SaveChanges, to test transaction rollback.</summary>
public sealed class FailingDbContext(AssetTrackerDbContext inner, int failOnSaveNumber) : IAssetTrackerDbContext
{
    private int _saves;

    public DbSet<Device> Devices => inner.Devices;
    public DbSet<Computer> Computers => inner.Computers;
    public DbSet<ResponsiblePerson> ResponsiblePeople => inner.ResponsiblePeople;
    public DbSet<Assignment> Assignments => inner.Assignments;
    public DbSet<DeviceObservation> DeviceObservations => inner.DeviceObservations;
    public DbSet<DeviceMovement> DeviceMovements => inner.DeviceMovements;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _saves++;
        return _saves == failOnSaveNumber
            ? throw new InvalidOperationException("Simulated failure during save")
            : inner.SaveChangesAsync(cancellationToken);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        inner.BeginTransactionAsync(cancellationToken);
}
