using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// Official change of responsibility/location recorded by a technician. Never created automatically by scans.
/// </summary>
public sealed class DeviceMovement
{
    public const int MaxReasonLength = 500;

    private DeviceMovement()
    {
    }

    public int Id { get; private set; }
    public int DeviceId { get; private set; }
    public Device Device { get; private set; } = null!;
    public MovementType MovementType { get; private set; }
    public int? FromResponsiblePersonId { get; private set; }
    public ResponsiblePerson? FromResponsiblePerson { get; private set; }
    public int? ToResponsiblePersonId { get; private set; }
    public ResponsiblePerson? ToResponsiblePerson { get; private set; }
    public int? FromComputerId { get; private set; }
    public Computer? FromComputer { get; private set; }
    public int? ToComputerId { get; private set; }
    public Computer? ToComputer { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string Reason { get; private set; } = null!;

    internal static DeviceMovement ForAssignment(Device device, Assignment to, string? reason, DateTime now) => new()
    {
        Device = device,
        DeviceId = device.Id,
        MovementType = MovementType.Assignment,
        ToResponsiblePersonId = to.ResponsiblePersonId,
        ToComputerId = to.ExpectedComputerId,
        ChangedAt = now,
        Reason = CleanReason(reason, required: false) ?? "Atribuição inicial"
    };

    internal static DeviceMovement ForTransfer(Device device, Assignment from, Assignment to, string reason, DateTime now) => new()
    {
        Device = device,
        DeviceId = device.Id,
        MovementType = MovementType.Transfer,
        FromResponsiblePersonId = from.ResponsiblePersonId,
        FromComputerId = from.ExpectedComputerId,
        ToResponsiblePersonId = to.ResponsiblePersonId,
        ToComputerId = to.ExpectedComputerId,
        ChangedAt = now,
        Reason = CleanReason(reason, required: true)!
    };

    internal static string? CleanReason(string? reason, bool required)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return required ? throw new DomainException("O motivo da transferência é obrigatório.") : null;
        }

        var cleaned = reason.Trim();
        if (cleaned.Length > MaxReasonLength)
        {
            throw new DomainException($"O motivo deve ter no máximo {MaxReasonLength} caracteres.");
        }

        return cleaned;
    }
}
