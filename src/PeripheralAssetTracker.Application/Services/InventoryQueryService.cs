using Microsoft.EntityFrameworkCore;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>
/// Listing, filtering and searching registered devices.
/// Status-based filters (unassigned, unexpected computer) depend on computed conditions, and search
/// must be accent-insensitive, which SQLite LIKE is not. For a local inventory (thousands of devices)
/// the filtering is therefore done in memory over the loaded read model.
/// </summary>
public sealed class InventoryQueryService(IAssetTrackerDbContext db)
{
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(DeviceFilter filter, CancellationToken ct = default)
    {
        var query = db.WithActiveAssignment().AsNoTracking();
        if (filter.Type is { } type)
        {
            query = query.Where(d => d.DeviceType == type);
        }

        if (filter.OnlyLowConfidence)
        {
            query = query.Where(d => d.FingerprintConfidence == FingerprintConfidence.Low);
        }

        var devices = await query.OrderBy(d => d.DeviceName).ThenBy(d => d.Id).ToListAsync(ct);
        return devices
            .Select(d => DeviceReadModel.ToSummary(d))
            .Where(s => filter.ResponsiblePersonId is null || s.ResponsiblePersonId == filter.ResponsiblePersonId)
            .Where(s => filter.ComputerId is null ||
                        s.ExpectedComputerId == filter.ComputerId || s.LastSeenComputerId == filter.ComputerId)
            .Where(s => !filter.OnlyUnassigned || s.Conditions.HasFlag(DeviceConditions.Unassigned))
            .Where(s => !filter.OnlyUnexpectedComputer || s.Conditions.HasFlag(DeviceConditions.UnexpectedComputer))
            .ToList();
    }

    /// <summary>
    /// Searches fingerprint, name, responsible person, hostnames (expected and last seen), VID, PID,
    /// serial and manufacturer. Case and accent-insensitive.
    /// </summary>
    public async Task<IReadOnlyList<DeviceSummary>> SearchAsync(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var all = await ListAsync(DeviceFilter.All, ct);
        return all.Where(s => Matches(s, term.Trim())).ToList();
    }

    public async Task<DeviceSummary?> GetAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await db.WithActiveAssignment().AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, ct);
        return device is null ? null : DeviceReadModel.ToSummary(device);
    }

    /// <summary>Resolves "#12", "12" or a fingerprint prefix (min. 6 chars) to a single device.</summary>
    public async Task<DeviceSummary> ResolveAsync(string reference, CancellationToken ct = default)
    {
        var trimmed = reference.Trim().TrimStart('#');
        if (int.TryParse(trimmed, out var id))
        {
            return await GetAsync(id, ct) ?? throw new NotFoundException($"Dispositivo #{id} não encontrado.");
        }

        if (trimmed.Length < 6)
        {
            throw new AssetTrackerException("Informe o número do dispositivo ou pelo menos 6 caracteres do fingerprint.");
        }

        var prefix = trimmed.ToUpperInvariant();
        var matches = await db.Devices.AsNoTracking()
            .Where(d => d.Fingerprint.StartsWith(prefix))
            .Select(d => d.Id)
            .Take(2)
            .ToListAsync(ct);

        return matches.Count switch
        {
            0 => throw new NotFoundException($"Nenhum dispositivo com fingerprint iniciando em {prefix}."),
            > 1 => throw new AssetTrackerException("Mais de um dispositivo corresponde a esse prefixo; informe mais caracteres."),
            _ => (await GetAsync(matches[0], ct))!
        };
    }

    /// <summary>Same matching rules as <see cref="SearchAsync"/>, for callers that filter an already loaded list.</summary>
    public static bool Matches(DeviceSummary s, string term)
    {
        var upper = term.ToUpperInvariant();
        return s.Fingerprint.StartsWith(upper, StringComparison.Ordinal) ||
               (upper.Length >= 8 && s.Fingerprint.Contains(upper, StringComparison.Ordinal)) ||
               TextSearch.Contains(s.DeviceName, term) ||
               TextSearch.Contains(s.ResponsiblePersonName, term) ||
               TextSearch.Contains(s.ResponsibleDepartment, term) ||
               TextSearch.Contains(s.ExpectedComputerHostname, term) ||
               TextSearch.Contains(s.LastSeenComputerHostname, term) ||
               TextSearch.Contains(s.VendorId, term) ||
               TextSearch.Contains(s.ProductId, term) ||
               TextSearch.Contains(s.SerialNumber, term) ||
               TextSearch.Contains(s.Manufacturer, term);
    }
}
