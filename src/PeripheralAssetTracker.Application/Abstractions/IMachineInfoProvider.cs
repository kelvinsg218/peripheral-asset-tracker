namespace PeripheralAssetTracker.Application.Abstractions;

/// <summary>Identifies the computer the application is running on.</summary>
public interface IMachineInfoProvider
{
    string GetHostname();

    /// <summary>Human readable Windows version, or null when unavailable.</summary>
    string? GetWindowsVersion();
}
