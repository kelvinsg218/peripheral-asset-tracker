using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Discovery;

namespace PeripheralAssetTracker.Infrastructure.Discovery;

/// <summary>Discovers HID peripherals on the local Windows machine (read-only).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceDiscoveryService(IPnpNodeSource source, ILogger<WindowsDeviceDiscoveryService> logger)
    : IDeviceDiscoveryService
{
    public Task<DeviceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var nodes = source.GetPresentNodes();
            var result = HidDeviceGrouper.Group(nodes);
            logger.LogInformation(
                "Device discovery: {Nodes} PnP nodes, {Entries} HID/input entries grouped into {Devices} device(s), {Virtual} virtual ignored",
                nodes.Count, result.RawEntryCount, result.Devices.Count, result.IgnoredVirtualEntryCount);
            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Device discovery failed");
            throw new DeviceDiscoveryException("Não foi possível consultar os dispositivos conectados.", ex);
        }
    }
}

/// <summary>
/// Replays a captured PnP snapshot through the same grouping logic. Used for tests and for
/// functional validation without real hardware (--fixture).
/// </summary>
public sealed class SnapshotDeviceDiscoveryService(string snapshotPath, ILogger<SnapshotDeviceDiscoveryService> logger)
    : IDeviceDiscoveryService
{
    public async Task<DeviceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
            var snapshot = PnpSnapshot.FromJson(json);
            var result = HidDeviceGrouper.Group(snapshot.Nodes);
            logger.LogInformation("Device discovery from snapshot {File}: {Devices} device(s)", Path.GetFileName(snapshotPath), result.Devices.Count);
            return result with { Warnings = [.. result.Warnings, $"Dados simulados carregados de '{Path.GetFileName(snapshotPath)}'."] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            logger.LogError(ex, "Could not read discovery snapshot {File}", snapshotPath);
            throw new DeviceDiscoveryException($"Não foi possível ler o arquivo de dispositivos simulados '{snapshotPath}'.", ex);
        }
    }
}

/// <summary>Used on non-Windows platforms: the application still works for inventory management.</summary>
public sealed class UnsupportedPlatformDiscoveryService : IDeviceDiscoveryService
{
    public Task<DeviceDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
        throw new DeviceDiscoveryException("A descoberta de dispositivos só é suportada no Windows 10/11.");
}
