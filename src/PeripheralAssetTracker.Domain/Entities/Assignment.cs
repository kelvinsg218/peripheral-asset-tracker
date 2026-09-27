namespace PeripheralAssetTracker.Domain.Entities;

/// <summary>
/// OFFICIAL assignment of a device: who is responsible and where it is expected to be.
/// Either the responsible person or the expected computer may be unknown, but not both.
/// Assignments are created through <see cref="Device"/>, which guarantees a single active one.
/// </summary>
public sealed class Assignment
{
    private Assignment()
    {
    }

    public int Id { get; private set; }
    public int DeviceId { get; private set; }
    public Device Device { get; private set; } = null!;
    public int? ResponsiblePersonId { get; private set; }
    public ResponsiblePerson? ResponsiblePerson { get; private set; }
    public int? ExpectedComputerId { get; private set; }
    public Computer? ExpectedComputer { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public bool IsActive { get; private set; }

    internal static Assignment Create(Device device, int? responsiblePersonId, int? expectedComputerId, DateTime now)
    {
        if (responsiblePersonId is null && expectedComputerId is null)
        {
            throw new DomainException("Uma atribuição precisa de um responsável e/ou de um computador esperado.");
        }

        return new Assignment
        {
            Device = device,
            DeviceId = device.Id,
            ResponsiblePersonId = responsiblePersonId,
            ExpectedComputerId = expectedComputerId,
            AssignedAt = now,
            IsActive = true
        };
    }

    internal void End(DateTime now)
    {
        if (!IsActive)
        {
            throw new DomainException("A atribuição já está encerrada.");
        }

        EndedAt = now < AssignedAt ? AssignedAt : now;
        IsActive = false;
    }

    /// <summary>True when this assignment was the official one at the given instant.</summary>
    public bool WasActiveAt(DateTime instant) =>
        AssignedAt <= instant && (EndedAt is null || instant < EndedAt);

    public bool HasSameTarget(int? responsiblePersonId, int? expectedComputerId) =>
        ResponsiblePersonId == responsiblePersonId && ExpectedComputerId == expectedComputerId;
}
