namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// A Windows computer. FirstSeenAt/LastSeenAt are only set when the application actually ran on it;
/// a computer typed by a technician (e.g. the expected computer of an assignment) stays "never seen".
/// </summary>
public sealed class Computer
{
    public const int MaxHostnameLength = 255;

    private Computer()
    {
    }

    public int Id { get; private set; }
    public string Hostname { get; private set; } = null!;
    public string? WindowsVersion { get; private set; }
    public DateTime? FirstSeenAt { get; private set; }
    public DateTime? LastSeenAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>The computer the application is running on.</summary>
    public static Computer Create(string hostname, string? windowsVersion, DateTime now)
    {
        var computer = CreateUnseen(hostname, now);
        computer.MarkSeen(windowsVersion, now);
        return computer;
    }

    /// <summary>A computer named by a technician but where the application never ran.</summary>
    public static Computer CreateUnseen(string hostname, DateTime now) => new()
    {
        Hostname = NormalizeHostname(hostname),
        CreatedAt = now
    };

    public void MarkSeen(string? windowsVersion, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(windowsVersion))
        {
            WindowsVersion = windowsVersion.Trim();
        }

        FirstSeenAt ??= now;
        if (LastSeenAt is null || now > LastSeenAt)
        {
            LastSeenAt = now;
        }
    }

    /// <summary>Windows host names are case-insensitive; they are stored upper-case.</summary>
    public static string NormalizeHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new DomainException("O nome do computador é obrigatório.");
        }

        var normalized = hostname.Trim().ToUpperInvariant();
        if (normalized.Length > MaxHostnameLength || normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainException($"Nome de computador inválido: '{hostname.Trim()}'.");
        }

        return normalized;
    }
}
