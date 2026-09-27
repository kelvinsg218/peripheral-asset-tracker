using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Infrastructure.Persistence;
using PeripheralAssetTracker.Tests.TestSupport;

namespace PeripheralAssetTracker.Tests.Persistence;

public class PersistenceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Migrations_are_applied_and_nothing_is_pending()
    {
        await using var db = _db.NewContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_InitialCreate"));
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_migrations()
    {
        await using var db = _db.NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Foreign_keys_are_enforced()
    {
        await using var db = _db.NewContext();
        var foreignKeys = await db.Database.SqlQueryRaw<long>("SELECT foreign_keys AS \"Value\" FROM pragma_foreign_keys").SingleAsync();
        Assert.Equal(1, foreignKeys);

        var computer = await _db.AddComputerAsync("TI-04");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), computer.Id);

        await using var raw = _db.NewContext();
        var ex = await Assert.ThrowsAsync<SqliteException>(() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO DeviceObservations (DeviceId, ComputerId, ObservedAt, LastObservedAt, ScanCount) VALUES ({0}, 9999, '2026-01-01', '2026-01-01', 1)",
            device.Id));
        Assert.Contains("FOREIGN KEY", ex.Message);
    }

    [Fact]
    public async Task Deleting_a_computer_referenced_by_history_is_restricted()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), computer.Id);

        await using var db = _db.NewContext();
        var ex = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM Computers WHERE Id = {0}", computer.Id));
        Assert.Contains("FOREIGN KEY", ex.Message);
    }

    [Fact]
    public async Task Fingerprint_is_unique_in_the_database()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        var fp = _db.Fingerprints.Compute(DeviceFixtures.LogitechWithSerial());
        var data = new DeviceRegistrationData(fp.Fingerprint, fp.Confidence, fp.Basis, DeviceType.Mouse, "A", null, "046D", "C084", "ABC123", null, BusType.Usb, null);

        await using (var db = _db.NewContext())
        {
            db.Devices.Add(Device.Register(data, computer.Id, _db.Clock.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync();
        }

        await using var second = _db.NewContext();
        second.Devices.Add(Device.Register(data with { DeviceName = "B" }, computer.Id, _db.Clock.GetUtcNow().UtcDateTime));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Contains("UNIQUE", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Database_rejects_a_second_active_assignment_even_bypassing_the_domain()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        var person = await _db.AddPersonAsync("Kelvin");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), computer.Id, person.Id, computer.Id);

        await using var db = _db.NewContext();
        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Assignments (DeviceId, ResponsiblePersonId, ExpectedComputerId, AssignedAt, IsActive) VALUES ({0}, {1}, {2}, '2026-09-02 00:00:00', 1)",
            device.Id, person.Id, computer.Id));
        Assert.Contains("UNIQUE", ex.Message);

        // Inactive (historical) assignments are unrestricted.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Assignments (DeviceId, ResponsiblePersonId, ExpectedComputerId, AssignedAt, EndedAt, IsActive) VALUES ({0}, {1}, {2}, '2026-01-01 00:00:00', '2026-02-01 00:00:00', 0)",
            device.Id, person.Id, computer.Id);
    }

    [Fact]
    public async Task Check_constraints_protect_assignment_state()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), computer.Id);

        await using var db = _db.NewContext();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Assignments (DeviceId, AssignedAt, IsActive) VALUES ({0}, '2026-09-02', 1)", device.Id));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Assignments (DeviceId, ExpectedComputerId, AssignedAt, IsActive) VALUES ({0}, {1}, '2026-09-02', 0)",
            device.Id, computer.Id));
    }

    [Fact]
    public async Task Timestamps_round_trip_as_utc()
    {
        var computer = await _db.AddComputerAsync("TI-04");

        await using var db = _db.NewContext();
        var loaded = await db.Computers.SingleAsync(c => c.Id == computer.Id);
        Assert.Equal(DateTimeKind.Utc, loaded.FirstSeenAt!.Value.Kind);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, loaded.FirstSeenAt);
    }

    [Fact]
    public async Task Transfer_is_atomic_and_rolls_back_when_a_step_fails()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var fin03 = await _db.AddComputerAsync("FINANCEIRO-03");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var joao = await _db.AddPersonAsync("João");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, ti04.Id);

        // The 2nd save (new assignment + movement) fails after the old assignment was already ended in the transaction.
        await using (var inner = _db.NewContext())
        {
            var failing = new FailingDbContext(inner, failOnSaveNumber: 2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => _db.Assignments(failing)
                .TransferAsync(new TransferRequest(device.Id, joao.Id, fin03.Id, "Equipment relocation")));
        }

        await using var check = _db.NewContext();
        var assignments = await check.Assignments.Where(a => a.DeviceId == device.Id).ToListAsync();
        var active = Assert.Single(assignments);
        Assert.True(active.IsActive);
        Assert.Null(active.EndedAt);
        Assert.Equal(kelvin.Id, active.ResponsiblePersonId);
        Assert.Equal(1, await check.DeviceMovements.CountAsync(m => m.DeviceId == device.Id)); // only the initial assignment
    }

    [Fact]
    public async Task Successful_transfer_persists_everything()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var fin03 = await _db.AddComputerAsync("FINANCEIRO-03");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var joao = await _db.AddPersonAsync("João");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, ti04.Id);
        _db.Clock.Advance(TimeSpan.FromDays(20));

        await using (var db = _db.NewContext())
        {
            await _db.Assignments(db).TransferAsync(new TransferRequest(device.Id, joao.Id, fin03.Id, "Equipment relocation"));
        }

        await using var check = _db.NewContext();
        var assignments = await check.Assignments.Where(a => a.DeviceId == device.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.False(assignments[0].IsActive);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, assignments[0].EndedAt);
        Assert.True(assignments[1].IsActive);
        Assert.Equal((joao.Id, fin03.Id), (assignments[1].ResponsiblePersonId!.Value, assignments[1].ExpectedComputerId!.Value));

        var movement = await check.DeviceMovements.SingleAsync(m => m.MovementType == MovementType.Transfer);
        Assert.Equal((kelvin.Id, ti04.Id, joao.Id, fin03.Id),
            (movement.FromResponsiblePersonId!.Value, movement.FromComputerId!.Value, movement.ToResponsiblePersonId!.Value, movement.ToComputerId!.Value));
        Assert.Equal("Equipment relocation", movement.Reason);
    }

    [Fact]
    public async Task Database_initializer_applies_migrations_to_a_new_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pat-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new DbContextOptionsBuilder<AssetTrackerDbContext>()
                .UseSqlite($"Data Source={Path.Combine(dir, "t.db")};Foreign Keys=True;Pooling=False").Options;
            await using (var db = new AssetTrackerDbContext(options))
            {
                await new DatabaseInitializer(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }

            // Second start: idempotent.
            await using (var db = new AssetTrackerDbContext(options))
            {
                await new DatabaseInitializer(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
