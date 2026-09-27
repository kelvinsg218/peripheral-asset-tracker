using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Domain.Services;

/// <summary>
/// Anti-spam strategy for observations. A new observation row is created when:
///  - the device has never been observed; or
///  - it is now on a DIFFERENT computer than its latest observation (location changes are always kept); or
///  - more than <see cref="CoalescingWindow"/> passed since the latest confirmation on the same computer.
/// Otherwise the latest observation is extended (LastObservedAt/ScanCount). Device.LastSeenAt is always updated.
/// </summary>
public sealed class ObservationPolicy(TimeSpan coalescingWindow)
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(1);

    public TimeSpan CoalescingWindow { get; } = coalescingWindow > TimeSpan.Zero
        ? coalescingWindow
        : throw new ArgumentOutOfRangeException(nameof(coalescingWindow));

    public bool ShouldExtend(DeviceObservation? latest, int computerId, DateTime now) =>
        latest is not null &&
        latest.ComputerId == computerId &&
        now >= latest.LastObservedAt &&
        now - latest.LastObservedAt <= CoalescingWindow;
}
