using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Application.Services;

public sealed class PeopleService(IAssetTrackerDbContext db, TimeProvider clock, ILogger<PeopleService> logger)
{
    public async Task<ResponsiblePerson> CreateAsync(string name, string? department, CancellationToken ct = default)
    {
        var person = ResponsiblePerson.Create(name, department, clock.GetUtcNow().UtcDateTime);
        db.ResponsiblePeople.Add(person);
        await db.SaveChangesAsync(ct);
        // Only the id is logged: names are personal data and are not needed in technical logs.
        logger.LogInformation("Responsible person {PersonId} created", person.Id);
        return person;
    }

    public async Task<ResponsiblePerson?> FindAsync(int id, CancellationToken ct = default) =>
        await db.ResponsiblePeople.FindAsync([id], ct);

    /// <summary>Lists people; optional friendly search (case and accent-insensitive) on name/department.</summary>
    public async Task<IReadOnlyList<PersonSummary>> ListAsync(string? search = null, CancellationToken ct = default)
    {
        var counts = await db.Assignments
            .Where(a => a.IsActive && a.ResponsiblePersonId != null)
            .GroupBy(a => a.ResponsiblePersonId!.Value)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PersonId, x => x.Count, ct);

        var people = await db.ResponsiblePeople.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return people
            .Where(p => string.IsNullOrWhiteSpace(search) ||
                        TextSearch.Contains(p.Name, search) ||
                        TextSearch.Contains(p.Department, search))
            .Select(p => new PersonSummary(p.Id, p.Name, p.Department, p.CreatedAt, counts.GetValueOrDefault(p.Id)))
            .ToList();
    }
}
