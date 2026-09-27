using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application;

namespace PeripheralAssetTracker.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations in a controlled way at startup.</summary>
public sealed class DatabaseInitializer(AssetTrackerDbContext db, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
            }

            // Defensive check: foreign keys must be enforced on this connection.
            var foreignKeys = await db.Database.SqlQueryRaw<long>("SELECT foreign_keys AS \"Value\" FROM pragma_foreign_keys").SingleAsync(ct);
            if (foreignKeys != 1)
            {
                throw new InvalidOperationException("SQLite foreign keys are not enabled.");
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or DbUpdateException)
        {
            logger.LogError(ex, "Database initialization failed");
            throw new AssetTrackerException(
                "Não foi possível inicializar o banco de dados local. Verifique permissões da pasta 'data' e o log.", ex);
        }
    }
}
