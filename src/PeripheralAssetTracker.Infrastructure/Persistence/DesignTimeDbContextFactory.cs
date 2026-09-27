using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PeripheralAssetTracker.Infrastructure.Persistence;

/// <summary>Used only by the dotnet-ef tool to create migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AssetTrackerDbContext>
{
    public AssetTrackerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssetTrackerDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AssetTrackerDbContext(options);
    }
}
