using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>Official assignments and transfers. Only these operations change who/where a device should be.</summary>
public sealed class AssignmentService(
    IAssetTrackerDbContext db,
    TimeProvider clock,
    ILogger<AssignmentService> logger)
{
    public async Task<Assignment> AssignAsync(
        int deviceId, int? responsiblePersonId, int? expectedComputerId, CancellationToken ct = default)
    {
        var device = await LoadDeviceAsync(deviceId, ct);
        await EnsureReferencesExistAsync(responsiblePersonId, expectedComputerId, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var (assignment, movement) = device.Assign(responsiblePersonId, expectedComputerId, null, now);
        db.DeviceMovements.Add(movement);

        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Device {DeviceId} assigned (assignment {AssignmentId})", device.Id, assignment.Id);
        return assignment;
    }

    /// <summary>
    /// Official transfer in a single transaction:
    /// validate → end current assignment → create new assignment → record movement → commit.
    /// Any failure rolls everything back, so the device never ends with zero or two active assignments.
    /// </summary>
    public async Task<DeviceMovement> TransferAsync(TransferRequest request, CancellationToken ct = default)
    {
        var device = await LoadDeviceAsync(request.DeviceId, ct);
        await EnsureReferencesExistAsync(request.NewResponsiblePersonId, request.NewExpectedComputerId, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await db.BeginTransactionAsync(ct);
        try
        {
            var ended = device.BeginTransfer(request.NewResponsiblePersonId, request.NewExpectedComputerId, request.Reason, now);
            // Persist the end of the old assignment first: the database allows only one active assignment per device.
            await db.SaveChangesAsync(ct);

            var (_, movement) = device.CompleteTransfer(
                ended, request.NewResponsiblePersonId, request.NewExpectedComputerId, request.Reason, now);
            db.DeviceMovements.Add(movement);
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            logger.LogInformation("Device {DeviceId} transferred (movement {MovementId})", device.Id, movement.Id);
            return movement;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(ex, "Transfer of device {DeviceId} rolled back", request.DeviceId);
            throw;
        }
    }

    private async Task<Device> LoadDeviceAsync(int deviceId, CancellationToken ct) =>
        await db.Devices
            .Include(d => d.Assignments.Where(a => a.IsActive))
            .SingleOrDefaultAsync(d => d.Id == deviceId, ct)
        ?? throw new NotFoundException($"Dispositivo #{deviceId} não encontrado.");

    private async Task EnsureReferencesExistAsync(int? personId, int? computerId, CancellationToken ct)
    {
        if (personId is not null && await db.ResponsiblePeople.FindAsync([personId.Value], ct) is null)
        {
            throw new NotFoundException($"Responsável #{personId} não encontrado.");
        }

        if (computerId is not null && await db.Computers.FindAsync([computerId.Value], ct) is null)
        {
            throw new NotFoundException($"Computador #{computerId} não encontrado.");
        }
    }
}
