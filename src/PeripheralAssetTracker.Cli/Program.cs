using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Cli;
using PeripheralAssetTracker.Domain;
using PeripheralAssetTracker.Infrastructure;
using PeripheralAssetTracker.Infrastructure.Persistence;

Console.OutputEncoding = Encoding.UTF8;
try
{
    Console.InputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Some hosts do not allow changing the input encoding; the default is kept.
}

var ui = new ConsoleUi(Console.In, Console.Out);

CliArguments arguments;
try
{
    arguments = CliArguments.Parse(args);
}
catch (ArgumentException ex)
{
    ui.Error(ex.Message);
    HelpText.Print(ui);
    return 2;
}

if (arguments.Has("help") || arguments.Command == "help")
{
    HelpText.Print(ui);
    return 0;
}

// 1. Directories
var root = arguments.DataDirectory ?? Environment.GetEnvironmentVariable("PAT_DATA_DIR") ?? Directory.GetCurrentDirectory();
var paths = new AppPaths(root);
try
{
    paths.EnsureDirectories();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    ui.Error($"Não foi possível criar as pastas de dados em '{paths.RootDirectory}': {ex.Message}");
    return 1;
}

var services = new ServiceCollection()
    .AddApplication()
    .AddInfrastructure(new InfrastructureOptions
    {
        Paths = paths,
        DiscoverySnapshotPath = arguments.FixturePath is null ? null : Path.GetFullPath(arguments.FixturePath),
        SimulatedHostname = arguments.SimulatedHost
    });

await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("PeripheralAssetTracker.Cli");

try
{
    logger.LogInformation("Startup (command: {Command}, data: {Root}, simulated discovery: {Fixture}, simulated host: {Host})",
        arguments.Command ?? "interactive", paths.RootDirectory, arguments.FixturePath is not null, arguments.SimulatedHost is not null);

    // 2-3. SQLite + migrations; 4-6. hostname, Windows version, Computer registration.
    int computerId;
    string hostname;
    await using (var scope = provider.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var computer = await scope.ServiceProvider.GetRequiredService<ComputerService>().RegisterCurrentComputerAsync();
        computerId = computer.Id;
        hostname = computer.Hostname;
    }

    if (arguments.SimulatedHost is not null)
    {
        ui.Warning($"Modo de simulação: host '{hostname}' informado via --host.");
    }

    if (arguments.FixturePath is not null)
    {
        ui.Warning($"Modo de simulação: dispositivos lidos de '{arguments.FixturePath}'.");
    }

    // 7. Open the application.
    var session = new CliSession(provider, ui, paths, computerId, hostname);
    return arguments.Command is null
        ? await new InteractiveShell(session, logger).RunAsync()
        : await new CommandRunner(session, arguments).RunAsync();
}
catch (Exception ex) when (ex is AssetTrackerException or DomainException or ArgumentException)
{
    if (ex.InnerException is not null)
    {
        logger.LogWarning(ex, "Operation failed");
    }

    ui.Error(ex.Message);
    return 1;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Unexpected failure");
    ui.Error($"Ocorreu um erro inesperado. Os detalhes técnicos foram registrados em '{paths.LogsDirectory}'.");
    return 1;
}
