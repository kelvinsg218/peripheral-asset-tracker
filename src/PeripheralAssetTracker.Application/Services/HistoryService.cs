using Microsoft.EntityFrameworkCore;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Domain.Services;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>
/// Builds the device timeline: registration, official movements (assignment/transfer) and observations.
/// Each observation is compared with the assignment that was active AT THAT TIME, so past
/// "unexpected computer" situations remain visible after a later transfer.
/// </summary>
public sealed class HistoryService(IAssetTrackerDbContext db, InventoryQueryService inventory)
{
    public async Task<DeviceHistory> GetDeviceHistoryAsync(int deviceId, CancellationToken ct = default)
    {
        var summary = await inventory.GetAsync(deviceId, ct)
            ?? throw new NotFoundException($"Dispositivo #{deviceId} não encontrado.");

        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.DeviceId == deviceId)
            .ToListAsync(ct);

        var movements = await db.DeviceMovements.AsNoTracking()
            .Include(m => m.FromResponsiblePerson).Include(m => m.ToResponsiblePerson)
            .Include(m => m.FromComputer).Include(m => m.ToComputer)
            .Where(m => m.DeviceId == deviceId)
            .ToListAsync(ct);

        var observations = await db.DeviceObservations.AsNoTracking()
            .Include(o => o.Computer)
            .Where(o => o.DeviceId == deviceId)
            .ToListAsync(ct);

        var entries = new List<HistoryEntry>
        {
            new(summary.CreatedAt, HistoryEntryKind.Registered, null, null, null, null, null)
        };

        entries.AddRange(movements.Select(m => new HistoryEntry(
            m.ChangedAt,
            m.MovementType == MovementType.Transfer ? HistoryEntryKind.Transferred : HistoryEntryKind.Assigned,
            m.FromResponsiblePerson?.Name,
            m.FromComputer?.Hostname,
            m.ToResponsiblePerson?.Name,
            m.ToComputer?.Hostname,
            m.Reason)));

        foreach (var observation in observations)
        {
            var activeThen = assignments.FirstOrDefault(a => a.WasActiveAt(observation.ObservedAt));
            var unexpected = DeviceStatusEvaluator.IsUnexpectedComputer(activeThen?.ExpectedComputerId, observation.ComputerId);
            entries.Add(new HistoryEntry(
                observation.ObservedAt,
                HistoryEntryKind.Observed,
                null,
                null,
                null,
                observation.Computer.Hostname,
                null,
                observation.LastObservedAt,
                observation.ScanCount,
                unexpected));
        }

        // Stable ordering: time, then registration → movement → observation for equal timestamps.
        var ordered = entries.OrderBy(e => e.At).ThenBy(e => e.Kind).ToList();
        return new DeviceHistory(summary, ordered);
    }

    public async Task<IReadOnlyList<MovementSummary>> GetRecentMovementsAsync(int limit = 50, CancellationToken ct = default)
    {
        var movements = await db.DeviceMovements.AsNoTracking()
            .Include(m => m.Device)
            .Include(m => m.FromResponsiblePerson).Include(m => m.ToResponsiblePerson)
            .Include(m => m.FromComputer).Include(m => m.ToComputer)
            .OrderByDescending(m => m.ChangedAt).ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);

        return movements.Select(m => new MovementSummary(
                m.Id,
                m.DeviceId,
                m.Device.DeviceName,
                m.MovementType,
                m.FromResponsiblePerson?.Name,
                m.FromComputer?.Hostname,
                m.ToResponsiblePerson?.Name,
                m.ToComputer?.Hostname,
                m.ChangedAt,
                m.Reason))
            .ToList();
    }
}
