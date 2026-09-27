using PeripheralAssetTracker.Application.Discovery;

namespace PeripheralAssetTracker.Application.Abstractions;

/// <summary>
/// Discovers peripherals currently connected to this computer (read-only hardware inventory).
/// Implementations must never capture input, keystrokes, pointer movement or any personal activity.
/// </summary>
public interface IDeviceDiscoveryService
{
    Task<DeviceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>Raised when the platform cannot be queried (the message is user-facing).</summary>
public sealed class DeviceDiscoveryException(string message, Exception? innerException = null)
    : AssetTrackerException(message, innerException);
