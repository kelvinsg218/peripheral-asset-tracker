using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Discovery;
using PeripheralAssetTracker.Application.Fingerprinting;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Application.Services;

public sealed class DeviceRegistrationService(
    IAssetTrackerDbContext db,
    DeviceFingerprintService fingerprints,
    TimeProvider clock,
    ILogger<DeviceRegistrationService> logger)
{
    /// <summary>
    /// Registers a discovered device. Responsible person and expected computer are optional:
    /// without both, the device stays UNASSIGNED. Everything is saved in one transaction.
    /// </summary>
    public async Task<Device> RegisterAsync(
        DiscoveredDevice discovered,
        Models.RegisterDeviceRequest request,
        int observedComputerId,
        CancellationToken ct = default)
    {
        var fingerprint = fingerprints.Compute(discovered);

        if (await db.Devices.AnyAsync(d => d.Fingerprint == fingerprint.Fingerprint, ct))
        {
            throw new AssetTrackerException(
                "Já existe um dispositivo cadastrado com este fingerprint." +
                (fingerprint.Confidence == Domain.Enums.FingerprintConfidence.Low
                    ? " Como a confiança é LOW, pode ser outra unidade do mesmo modelo — o Windows não fornece dados para diferenciá-las."
                    : string.Empty));
        }

        await EnsureReferencesExistAsync(request.ResponsiblePersonId, request.ExpectedComputerId, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var name = DeviceIdentifierNormalizer.CleanText(request.FriendlyName)
                   ?? DeviceIdentifierNormalizer.CleanText(discovered.DeviceName)
                   ?? "Dispositivo sem nome";

        var device = Device.Register(
            new DeviceRegistrationData(
                fingerprint.Fingerprint,
                fingerprint.Confidence,
                fingerprint.Basis,
                request.DeviceType,
                name,
                discovered.Manufacturer,
                DeviceIdentifierNormalizer.NormalizeHexId(discovered.VendorId),
                DeviceIdentifierNormalizer.NormalizeHexId(discovered.ProductId),
                DeviceIdentifierNormalizer.NormalizeSerial(discovered.SerialNumber),
                DeviceIdentifierNormalizer.NormalizeBluetoothAddress(discovered.BluetoothAddress),
                discovered.BusType,
                DeviceIdentifierNormalizer.NormalizePnpId(discovered.PnpDeviceId)),
            observedComputerId,
            now);

        await using var transaction = await db.BeginTransactionAsync(ct);
        db.Devices.Add(device);
        db.DeviceObservations.Add(DeviceObservation.Create(device, observedComputerId, now));

        if (request.ResponsiblePersonId is not null || request.ExpectedComputerId is not null)
        {
            var (_, movement) = device.Assign(request.ResponsiblePersonId, request.ExpectedComputerId, "Cadastro inicial", now);
            db.DeviceMovements.Add(movement);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Device {DeviceId} registered (type {Type}, confidence {Confidence}, assigned {Assigned})",
            device.Id, device.DeviceType, device.FingerprintConfidence, device.ActiveAssignment is not null);
        return device;
    }

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
