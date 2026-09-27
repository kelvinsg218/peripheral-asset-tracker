using Microsoft.Extensions.DependencyInjection;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Infrastructure;

namespace PeripheralAssetTracker.Cli;

/// <summary>
/// State shared by the CLI during one run. Every operation runs in its own DI scope (fresh DbContext),
/// so a failed operation never leaves pending changes that a later operation could save by accident.
/// </summary>
internal sealed class CliSession(IServiceProvider services, ConsoleUi ui, AppPaths paths, int computerId, string hostname)
{
    public ConsoleUi Ui { get; } = ui;
    public AppPaths Paths { get; } = paths;
    public int CurrentComputerId { get; } = computerId;
    public string CurrentHostname { get; } = hostname;

    /// <summary>Result of the latest scan, used to register devices from it.</summary>
    public ScanReport? LastScan { get; set; }

    public async Task<T> Use<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task Use(Func<IServiceProvider, Task> action)
    {
        await using var scope = services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }
}
