using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Infrastructure.Persistence;

public sealed class AssetTrackerDbContext(DbContextOptions<AssetTrackerDbContext> options)
    : DbContext(options), IAssetTrackerDbContext
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ResponsiblePerson> ResponsiblePeople => Set<ResponsiblePerson>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<DeviceObservation> DeviceObservations => Set<DeviceObservation>();
    public DbSet<DeviceMovement> DeviceMovements => Set<DeviceMovement>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssetTrackerDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are stored in UTC; SQLite loses DateTimeKind, so it is restored on read.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime(),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
