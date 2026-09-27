namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// Person accountable for an asset. This is NOT an authenticated user of the system.
/// </summary>
public sealed class ResponsiblePerson
{
    public const int MaxNameLength = 200;
    public const int MaxDepartmentLength = 120;

    private ResponsiblePerson()
    {
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Department { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static ResponsiblePerson Create(string name, string? department, DateTime now) => new()
    {
        Name = CleanName(name),
        Department = CleanDepartment(department),
        CreatedAt = now,
        UpdatedAt = now
    };

    public void Update(string name, string? department, DateTime now)
    {
        Name = CleanName(name);
        Department = CleanDepartment(department);
        UpdatedAt = now;
    }

    private static string CleanName(string name)
    {
        var cleaned = CollapseSpaces(name) ?? throw new DomainException("O nome do responsável é obrigatório.");
        if (cleaned.Length > MaxNameLength)
        {
            throw new DomainException($"O nome do responsável deve ter no máximo {MaxNameLength} caracteres.");
        }

        return cleaned;
    }

    private static string? CleanDepartment(string? department)
    {
        var cleaned = CollapseSpaces(department);
        if (cleaned is { Length: > MaxDepartmentLength })
        {
            throw new DomainException($"O setor deve ter no máximo {MaxDepartmentLength} caracteres.");
        }

        return cleaned;
    }

    private static string? CollapseSpaces(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
