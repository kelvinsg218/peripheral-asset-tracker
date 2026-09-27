using Microsoft.EntityFrameworkCore;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Services;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>Shared loading/mapping of devices with their ACTIVE assignment (used by several use cases).</summary>
internal static class DeviceReadModel
{
    public static IQueryable<Device> WithActiveAssignment(this IAssetTrackerDbContext db) =>
        db.Devices
            .Include(d => d.Assignments.Where(a => a.IsActive)).ThenInclude(a => a.ResponsiblePerson)
            .Include(d => d.Assignments.Where(a => a.IsActive)).ThenInclude(a => a.ExpectedComputer)
            .Include(d => d.LastSeenComputer);

    /// <param name="observedComputerId">
    /// Computer to compare with the expected one. Defaults to where the device was last seen.
    /// </param>
    public static DeviceSummary ToSummary(Device device, int? observedComputerId = null)
    {
        var active = device.ActiveAssignment;
        var observed = observedComputerId ?? device.LastSeenComputerId;
        return new DeviceSummary
        {
            Id = device.Id,
            Fingerprint = device.Fingerprint,
            Confidence = device.FingerprintConfidence,
            FingerprintBasis = device.FingerprintBasis,
            DeviceType = device.DeviceType,
            DeviceName = device.DeviceName,
            Manufacturer = device.Manufacturer,
            VendorId = device.VendorId,
            ProductId = device.ProductId,
            SerialNumber = device.SerialNumber,
            BluetoothAddress = device.BluetoothAddress,
            BusType = device.BusType,
            PnpDeviceId = device.PnpDeviceId,
            ResponsiblePersonId = active?.ResponsiblePersonId,
            ResponsiblePersonName = active?.ResponsiblePerson?.Name,
            ResponsibleDepartment = active?.ResponsiblePerson?.Department,
            ExpectedComputerId = active?.ExpectedComputerId,
            ExpectedComputerHostname = active?.ExpectedComputer?.Hostname,
            AssignedAt = active?.AssignedAt,
            LastSeenComputerId = device.LastSeenComputerId,
            LastSeenComputerHostname = device.LastSeenComputer?.Hostname,
            FirstSeenAt = device.FirstSeenAt,
            LastSeenAt = device.LastSeenAt,
            CreatedAt = device.CreatedAt,
            Conditions = DeviceStatusEvaluator.Evaluate(device.FingerprintConfidence, active, observed)
        };
    }
}
