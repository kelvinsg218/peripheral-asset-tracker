using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Infrastructure;
using PeripheralAssetTracker.Infrastructure.Persistence;

namespace PeripheralAssetTracker.Desktop;

/// <summary>Command-line switches kept for demos and tests (the app normally starts without any).</summary>
internal sealed record StartupOptions(string? DataDirectory, string? FixturePath, string? SimulatedHost)
{
    public bool IsSimulation => FixturePath is not null || SimulatedHost is not null;

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? Value(string name)
        {
            var index = args.ToList().FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
        }

        return new StartupOptions(Value("--data-dir"), Value("--fixture"), Value("--host"));
    }
}

/// <summary>
/// Composition root of the desktop app. Data lives in %LOCALAPPDATA%\PeripheralAssetTracker (per-user, no
/// administrator rights). Operations are serialized and run off the UI thread, each in its own DI scope.
/// </summary>
internal sealed class AppHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppHost(AppPaths paths, StartupOptions options, ServiceProvider provider)
    {
        Paths = paths;
        Options = options;
        _provider = provider;
        Logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("PeripheralAssetTracker.Desktop");
    }

    public AppPaths Paths { get; }
    public StartupOptions Options { get; }
    public ILogger Logger { get; }
    public int ComputerId { get; private set; }
    public string Hostname { get; private set; } = string.Empty;
    public string? WindowsVersion { get; private set; }

    public static string DefaultDataRoot() =>
        Environment.GetEnvironmentVariable("PAT_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeripheralAssetTracker");

    /// <summary>Creates the data folders and the service container. Throws IOException/UnauthorizedAccessException.</summary>
    public static AppHost Create(StartupOptions options)
    {
        var paths = new AppPaths(options.DataDirectory ?? DefaultDataRoot());
        paths.EnsureDirectories();

        var provider = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(new InfrastructureOptions
            {
                Paths = paths,
                DiscoverySnapshotPath = options.FixturePath is null ? null : Path.GetFullPath(options.FixturePath),
                SimulatedHostname = options.SimulatedHost
            })
            .BuildServiceProvider();

        return new AppHost(paths, options, provider);
    }

    /// <summary>Creates/migrates the SQLite database and registers the current computer.</summary>
    public Task InitializeAsync() => Run(async sp =>
    {
        await sp.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var computer = await sp.GetRequiredService<ComputerService>().RegisterCurrentComputerAsync();
        ComputerId = computer.Id;
        Hostname = computer.Hostname;
        WindowsVersion = computer.WindowsVersion;
        Logger.LogInformation("Desktop app started (simulation: {Simulation})", Options.IsSimulation);
    });

    public async Task<T> Run<T>(Func<IServiceProvider, Task<T>> action)
    {
        // ConfigureAwait(false): the gate must be released even while the UI thread is blocked (application exit).
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                await using var scope = _provider.CreateAsyncScope();
                return await action(scope.ServiceProvider);
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task Run(Func<IServiceProvider, Task> action) => Run(async sp =>
    {
        await action(sp);
        return true;
    });

    /// <summary>Waits (bounded) for the running operation, so the database is not closed in the middle of a write.</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        await _provider.DisposeAsync();
        _gate.Dispose();
    }
}
