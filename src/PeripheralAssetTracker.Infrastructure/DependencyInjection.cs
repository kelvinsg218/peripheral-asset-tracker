using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Infrastructure.Discovery;
using PeripheralAssetTracker.Infrastructure.Logging;
using PeripheralAssetTracker.Infrastructure.Persistence;
using PeripheralAssetTracker.Infrastructure.Platform;
using PeripheralAssetTracker.Infrastructure.Reports;

namespace PeripheralAssetTracker.Infrastructure;

public sealed record InfrastructureOptions
{
    public required AppPaths Paths { get; init; }

    /// <summary>Replay a PnP snapshot instead of reading real hardware.</summary>
    public string? DiscoverySnapshotPath { get; init; }

    /// <summary>Simulated host name (testing Expected vs Observed without a second computer).</summary>
    public string? SimulatedHostname { get; init; }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions options)
    {
        services.AddSingleton(options.Paths);

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new FileLoggerProvider(options.Paths.LogsDirectory));
            logging.SetMinimumLevel(LogLevel.Information);
            // EF Core logs every SQL command at Information level; only problems are relevant here.
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        });

        services.AddDbContext<AssetTrackerDbContext>(db => db.UseSqlite(options.Paths.ConnectionString));
        services.AddScoped<IAssetTrackerDbContext>(sp => sp.GetRequiredService<AssetTrackerDbContext>());
        services.AddScoped<DatabaseInitializer>();

        services.AddSingleton<IReportFileWriter, ReportFileWriter>();

        var realMachine = new WindowsMachineInfoProvider();
        services.AddSingleton<IMachineInfoProvider>(options.SimulatedHostname is null
            ? realMachine
            : new SimulatedMachineInfoProvider(options.SimulatedHostname, realMachine));

        if (options.DiscoverySnapshotPath is not null)
        {
            services.AddSingleton<IDeviceDiscoveryService>(sp => new SnapshotDeviceDiscoveryService(
                options.DiscoverySnapshotPath, sp.GetRequiredService<ILogger<SnapshotDeviceDiscoveryService>>()));
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IPnpNodeSource, CfgMgrPnpNodeSource>();
            services.AddSingleton<IDeviceDiscoveryService, WindowsDeviceDiscoveryService>();
        }
        else
        {
            services.AddSingleton<IDeviceDiscoveryService, UnsupportedPlatformDiscoveryService>();
        }

        return services;
    }
}
