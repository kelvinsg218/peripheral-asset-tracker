using Microsoft.Extensions.DependencyInjection;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Services;

namespace PeripheralAssetTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, TimeSpan? observationWindow = null)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DeviceFingerprintService>();
        services.AddSingleton(new ObservationPolicy(observationWindow ?? ObservationPolicy.DefaultWindow));

        services.AddScoped<ComputerService>();
        services.AddScoped<PeopleService>();
        services.AddScoped<ScanService>();
        services.AddScoped<DeviceRegistrationService>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<InventoryQueryService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<InventoryExportService>();
        return services;
    }
}
