namespace PeripheralAssetTracker.Infrastructure;

/// <summary>
/// Local folders used by the application, all under a single root (default: current directory):
/// data/peripheral-assets.db, logs/, reports/.
/// </summary>
public sealed class AppPaths
{
    public const string DatabaseFileName = "peripheral-assets.db";

    public AppPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        ReportsDirectory = Path.Combine(RootDirectory, "reports");
        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }
    public string ReportsDirectory { get; }
    public string DatabasePath { get; }

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True";

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ReportsDirectory);
    }
}
