using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// A registered peripheral, identified by its technical fingerprint.
/// The device is the aggregate root for its assignments and enforces that at most
/// ONE assignment is active at any time.
/// </summary>
public sealed class Device
{
    public const int FingerprintLength = 64;
    public const int MaxNameLength = 200;

    private readonly List<Assignment> _assignments = [];

    private Device()
    {
    }

    public int Id { get; private set; }
    public string Fingerprint { get; private set; } = null!;
    public FingerprintConfidence FingerprintConfidence { get; private set; }

    /// <summary>Human readable description of what the fingerprint was built from.</summary>
    public string FingerprintBasis { get; private set; } = null!;

    public DeviceType DeviceType { get; private set; }
    public string DeviceName { get; private set; } = null!;
    public string? Manufacturer { get; private set; }
    public string? VendorId { get; private set; }
    public string? ProductId { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? BluetoothAddress { get; private set; }
    public BusType BusType { get; private set; }

    /// <summary>Windows device instance id (PNP Device ID) of the physical node, as last seen.</summary>
    public string? PnpDeviceId { get; private set; }

    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public int? LastSeenComputerId { get; private set; }
    public Computer? LastSeenComputer { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<Assignment> Assignments => _assignments;

    /// <summary>
    /// The active assignment. Requires the active assignment to be loaded when read from persistence.
    /// </summary>
    public Assignment? ActiveAssignment
    {
        get
        {
            var active = _assignments.Where(a => a.IsActive).ToList();
            return active.Count switch
            {
                0 => null,
                1 => active[0],
                _ => throw new InvalidOperationException($"Device {Id} has more than one active assignment.")
            };
        }
    }

    public static Device Register(DeviceRegistrationData data, int observedComputerId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(data.Fingerprint) || data.Fingerprint.Length != FingerprintLength ||
            !data.Fingerprint.All(Uri.IsHexDigit))
        {
            throw new DomainException("Fingerprint inválido: esperado SHA-256 em hexadecimal (64 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(data.FingerprintBasis))
        {
            throw new DomainException("A base do fingerprint deve ser informada.");
        }

        return new Device
        {
            Fingerprint = data.Fingerprint.ToUpperInvariant(),
            FingerprintConfidence = data.Confidence,
            FingerprintBasis = data.FingerprintBasis.Trim(),
            DeviceType = data.DeviceType,
            DeviceName = CleanName(data.DeviceName),
            Manufacturer = Clean(data.Manufacturer),
            VendorId = Clean(data.VendorId),
            ProductId = Clean(data.ProductId),
            SerialNumber = Clean(data.SerialNumber),
            BluetoothAddress = Clean(data.BluetoothAddress),
            BusType = data.BusType,
            PnpDeviceId = Clean(data.PnpDeviceId),
            FirstSeenAt = now,
            LastSeenAt = now,
            LastSeenComputerId = observedComputerId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string name, DateTime now)
    {
        DeviceName = CleanName(name);
        UpdatedAt = now;
    }

    public void ChangeType(DeviceType type, DateTime now)
    {
        DeviceType = type;
        UpdatedAt = now;
    }

    /// <summary>Called when a scan detects this device on a computer.</summary>
    public void RecordSighting(int computerId, string? pnpDeviceId, DateTime now)
    {
        if (now > LastSeenAt)
        {
            LastSeenAt = now;
        }

        LastSeenComputerId = computerId;
        var cleanedPnp = Clean(pnpDeviceId);
        if (cleanedPnp is not null && cleanedPnp != PnpDeviceId)
        {
            PnpDeviceId = cleanedPnp;
            UpdatedAt = now;
        }
    }

    /// <summary>Creates the first official assignment. Fails if an assignment is already active.</summary>
    public (Assignment Assignment, DeviceMovement Movement) Assign(
        int? responsiblePersonId, int? expectedComputerId, string? reason, DateTime now)
    {
        if (ActiveAssignment is not null)
        {
            throw new DomainException("O dispositivo já possui uma atribuição ativa. Use a transferência.");
        }

        var assignment = Assignment.Create(this, responsiblePersonId, expectedComputerId, now);
        _assignments.Add(assignment);
        UpdatedAt = now;
        return (assignment, DeviceMovement.ForAssignment(this, assignment, reason, now));
    }

    /// <summary>
    /// First step of an official transfer: validates the request and ends the current assignment.
    /// Persist, then call <see cref="CompleteTransfer"/>. Both steps must run in the same transaction.
    /// (Two steps are needed because the database enforces a single active assignment per device.)
    /// </summary>
    public Assignment BeginTransfer(int? newResponsiblePersonId, int? newExpectedComputerId, string reason, DateTime now)
    {
        var current = ActiveAssignment
            ?? throw new DomainException("O dispositivo não possui atribuição ativa. Use 'Atribuir dispositivo'.");

        if (newResponsiblePersonId is null && newExpectedComputerId is null)
        {
            throw new DomainException("A transferência precisa de um novo responsável e/ou de um novo computador.");
        }

        if (current.HasSameTarget(newResponsiblePersonId, newExpectedComputerId))
        {
            throw new DomainException("A transferência não altera nem o responsável nem o computador esperado.");
        }

        DeviceMovement.CleanReason(reason, required: true);
        current.End(now);
        UpdatedAt = now;
        return current;
    }

    /// <summary>Second step of an official transfer: creates the new assignment and the movement record.</summary>
    public (Assignment Assignment, DeviceMovement Movement) CompleteTransfer(
        Assignment endedAssignment, int? newResponsiblePersonId, int? newExpectedComputerId, string reason, DateTime now)
    {
        if (endedAssignment.IsActive || !_assignments.Contains(endedAssignment))
        {
            throw new DomainException("A transferência não foi iniciada corretamente.");
        }

        if (ActiveAssignment is not null)
        {
            throw new DomainException("O dispositivo já possui uma atribuição ativa.");
        }

        var assignment = Assignment.Create(this, newResponsiblePersonId, newExpectedComputerId, now);
        _assignments.Add(assignment);
        UpdatedAt = now;
        return (assignment, DeviceMovement.ForTransfer(this, endedAssignment, assignment, reason, now));
    }

    private static string CleanName(string? name)
    {
        var cleaned = Clean(name) ?? throw new DomainException("O nome do dispositivo é obrigatório.");
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Technical data used to register a device (produced by discovery + fingerprinting).</summary>
public sealed record DeviceRegistrationData(
    string Fingerprint,
    FingerprintConfidence Confidence,
    string FingerprintBasis,
    DeviceType DeviceType,
    string DeviceName,
    string? Manufacturer,
    string? VendorId,
    string? ProductId,
    string? SerialNumber,
    string? BluetoothAddress,
    BusType BusType,
    string? PnpDeviceId);
