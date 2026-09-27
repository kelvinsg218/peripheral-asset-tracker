namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// Where a device was ACTUALLY detected by a scan. Not to be confused with <see cref="Assignment"/>.
/// Consecutive sightings on the same computer are coalesced into one observation
/// (see <see cref="Services.ObservationPolicy"/>): ObservedAt is when the sighting started,
/// LastObservedAt the most recent confirmation and ScanCount how many scans confirmed it.
/// </summary>
public sealed class DeviceObservation
{
    private DeviceObservation()
    {
    }

    public int Id { get; private set; }
    public int DeviceId { get; private set; }
    public Device Device { get; private set; } = null!;
    public int ComputerId { get; private set; }
    public Computer Computer { get; private set; } = null!;
    public DateTime ObservedAt { get; private set; }
    public DateTime LastObservedAt { get; private set; }
    public int ScanCount { get; private set; }

    public static DeviceObservation Create(Device device, int computerId, DateTime now) => new()
    {
        Device = device,
        DeviceId = device.Id,
        ComputerId = computerId,
        ObservedAt = now,
        LastObservedAt = now,
        ScanCount = 1
    };

    public void Extend(DateTime now)
    {
        if (now > LastObservedAt)
        {
            LastObservedAt = now;
        }

        ScanCount++;
    }
}
