using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Application.Services;

public sealed class ComputerService(
    IAssetTrackerDbContext db,
    IMachineInfoProvider machineInfo,
    TimeProvider clock,
    ILogger<ComputerService> logger)
{
    /// <summary>Observations newer than this count as "recently observed" in the computer list.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    /// <summary>Registers the current computer on first run, or refreshes LastSeenAt/WindowsVersion.</summary>
    public async Task<Computer> RegisterCurrentComputerAsync(CancellationToken ct = default)
    {
        var hostname = Computer.NormalizeHostname(machineInfo.GetHostname());
        var windowsVersion = machineInfo.GetWindowsVersion();
        var now = clock.GetUtcNow().UtcDateTime;

        var computer = await db.Computers.SingleOrDefaultAsync(c => c.Hostname == hostname, ct);
        if (computer is null)
        {
            computer = Computer.Create(hostname, windowsVersion, now);
            db.Computers.Add(computer);
            logger.LogInformation("Current computer {Hostname} registered for the first time", hostname);
        }
        else
        {
            computer.MarkSeen(windowsVersion, now);
        }

        await db.SaveChangesAsync(ct);
        return computer;
    }

    /// <summary>Finds a computer by hostname, creating it when the technician names a new one.</summary>
    public async Task<Computer> GetOrCreateAsync(string hostname, CancellationToken ct = default)
    {
        var normalized = Computer.NormalizeHostname(hostname);
        var computer = await db.Computers.SingleOrDefaultAsync(c => c.Hostname == normalized, ct);
        if (computer is not null)
        {
            return computer;
        }

        // A computer named by the technician: the application never ran there, so it stays "never seen".
        computer = Computer.CreateUnseen(normalized, clock.GetUtcNow().UtcDateTime);
        db.Computers.Add(computer);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Computer {Hostname} created manually", normalized);
        return computer;
    }

    public async Task<Computer?> FindAsync(string reference, CancellationToken ct = default)
    {
        if (int.TryParse(reference, out var id))
        {
            var byId = await db.Computers.FindAsync([id], ct);
            if (byId is not null)
            {
                return byId;
            }
        }

        var hostname = reference.Trim().ToUpperInvariant();
        return await db.Computers.SingleOrDefaultAsync(c => c.Hostname == hostname, ct);
    }

    public async Task<IReadOnlyList<ComputerSummary>> ListAsync(CancellationToken ct = default)
    {
        var recentSince = clock.GetUtcNow().UtcDateTime - RecentWindow;

        var assigned = await db.Assignments
            .Where(a => a.IsActive && a.ExpectedComputerId != null)
            .GroupBy(a => a.ExpectedComputerId!.Value)
            .Select(g => new { ComputerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ComputerId, x => x.Count, ct);

        var observed = await db.DeviceObservations
            .Where(o => o.LastObservedAt >= recentSince)
            .GroupBy(o => o.ComputerId)
            .Select(g => new { ComputerId = g.Key, Count = g.Select(o => o.DeviceId).Distinct().Count() })
            .ToDictionaryAsync(x => x.ComputerId, x => x.Count, ct);

        var computers = await db.Computers.AsNoTracking().OrderBy(c => c.Hostname).ToListAsync(ct);
        return computers
            .Select(c => new ComputerSummary(
                c.Id,
                c.Hostname,
                c.WindowsVersion,
                c.FirstSeenAt,
                c.LastSeenAt,
                assigned.GetValueOrDefault(c.Id),
                observed.GetValueOrDefault(c.Id)))
            .ToList();
    }
}
