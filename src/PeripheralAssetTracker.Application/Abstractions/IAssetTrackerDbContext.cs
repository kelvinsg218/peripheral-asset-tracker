using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Application.Abstractions;

/// <summary>
/// Persistence port used by the use cases. It exposes EF Core sets (provider-agnostic API) to keep
/// the application layer simple; the SQLite specifics live in Infrastructure.
/// </summary>
public interface IAssetTrackerDbContext
{
    DbSet<Device> Devices { get; }
    DbSet<Computer> Computers { get; }
    DbSet<ResponsiblePerson> ResponsiblePeople { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<DeviceObservation> DeviceObservations { get; }
    DbSet<DeviceMovement> DeviceMovements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
