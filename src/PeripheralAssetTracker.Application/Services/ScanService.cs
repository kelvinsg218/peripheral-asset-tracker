using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Domain.Services;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>
/// Scans the connected peripherals, matches them with the inventory, records observations and
/// evaluates Expected vs Observed. A scan NEVER changes assignments.
/// </summary>
public sealed class ScanService(
    IAssetTrackerDbContext db,
    IDeviceDiscoveryService discovery,
    DeviceFingerprintService fingerprints,
    ObservationPolicy observationPolicy,
    TimeProvider clock,
    ILogger<ScanService> logger)
{
    public async Task<ScanReport> ScanAsync(int currentComputerId, bool includeBuiltIn, CancellationToken ct = default)
    {
        var computer = await db.Computers.FindAsync([currentComputerId], ct)
            ?? throw new NotFoundException("Computador atual não encontrado no banco.");

        var discoveryResult = await discovery.DiscoverAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;

        var visible = discoveryResult.Devices.Where(d => includeBuiltIn || !d.IsBuiltIn).ToList();
        var hiddenBuiltIn = discoveryResult.Devices.Count - visible.Count;

        var fingerprinted = visible.Select(d => (Device: d, Fingerprint: fingerprints.Compute(d))).ToList();
        var fingerprintCounts = fingerprinted
            .GroupBy(x => x.Fingerprint.Fingerprint)
            .ToDictionary(g => g.Key, g => g.Count());

        var keys = fingerprintCounts.Keys.ToList();
        var registered = await db.WithActiveAssignment()
            .Where(d => keys.Contains(d.Fingerprint))
            .ToDictionaryAsync(d => d.Fingerprint, ct);

        var observedNow = new HashSet<int>();
        foreach (var device in registered.Values)
        {
            var discovered = fingerprinted.First(x => x.Fingerprint.Fingerprint == device.Fingerprint).Device;
            await RecordObservationAsync(device, currentComputerId, discovered.PnpDeviceId, now, ct);
            observedNow.Add(device.Id);
        }

        await db.SaveChangesAsync(ct);

        var items = new List<ScanItem>();
        foreach (var (discovered, fingerprint) in fingerprinted)
        {
            var shared = fingerprintCounts[fingerprint.Fingerprint];
            DeviceSummary? summary = null;
            DeviceConditions conditions;
            if (registered.TryGetValue(fingerprint.Fingerprint, out var device))
            {
                // Make sure the navigation used for display points to the current computer.
                summary = DeviceReadModel.ToSummary(device, currentComputerId) with
                {
                    LastSeenComputerHostname = computer.Hostname
                };
                conditions = summary.Conditions;
            }
            else
            {
                conditions = DeviceStatusEvaluator.EvaluateUnregistered(fingerprint.Confidence);
            }

            if (shared > 1)
            {
                conditions |= DeviceConditions.AmbiguousFingerprint;
            }

            items.Add(new ScanItem(discovered, fingerprint, summary, conditions, shared));
        }

        var unexpected = items.Count(i => i.Conditions.HasFlag(DeviceConditions.UnexpectedComputer));
        logger.LogInformation(
            "Scan on {Hostname}: {Found} device(s), {Registered} registered, {Unexpected} on unexpected computer, {Hidden} built-in hidden",
            computer.Hostname, items.Count, observedNow.Count, unexpected, hiddenBuiltIn);

        return new ScanReport(computer.Id, computer.Hostname, now, items, hiddenBuiltIn, discoveryResult);
    }

    private async Task RecordObservationAsync(Device device, int computerId, string? pnpDeviceId, DateTime now, CancellationToken ct)
    {
        device.RecordSighting(computerId, pnpDeviceId, now);

        var latest = await db.DeviceObservations
            .Where(o => o.DeviceId == device.Id)
            .OrderByDescending(o => o.LastObservedAt).ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);

        if (observationPolicy.ShouldExtend(latest, computerId, now))
        {
            latest!.Extend(now);
        }
        else
        {
            db.DeviceObservations.Add(DeviceObservation.Create(device, computerId, now));
        }
    }
}
