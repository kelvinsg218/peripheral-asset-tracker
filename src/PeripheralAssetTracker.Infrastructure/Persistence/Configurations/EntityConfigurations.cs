using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeripheralAssetTracker.Domain.Entities;

namespace PeripheralAssetTracker.Infrastructure.Persistence.Configurations;

// History must never disappear because a related row was deleted: every FK uses RESTRICT.

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("Devices", t =>
        {
            t.HasCheckConstraint("CK_Devices_FingerprintLength", "length(\"Fingerprint\") = 64");
            t.HasCheckConstraint("CK_Devices_Confidence", "\"FingerprintConfidence\" IN ('Low', 'Medium', 'High')");
            t.HasCheckConstraint("CK_Devices_Type", "\"DeviceType\" IN ('Mouse', 'Keyboard', 'Other')");
        });

        b.HasKey(d => d.Id);
        b.Property(d => d.Fingerprint).IsRequired().HasMaxLength(Device.FingerprintLength);
        b.HasIndex(d => d.Fingerprint).IsUnique().HasDatabaseName("UX_Devices_Fingerprint");

        b.Property(d => d.FingerprintConfidence).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(d => d.FingerprintBasis).IsRequired().HasMaxLength(200);
        b.Property(d => d.DeviceType).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(d => d.BusType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(d => d.DeviceName).IsRequired().HasMaxLength(Device.MaxNameLength);
        b.Property(d => d.Manufacturer).HasMaxLength(200);
        b.Property(d => d.VendorId).HasMaxLength(4);
        b.Property(d => d.ProductId).HasMaxLength(4);
        b.Property(d => d.SerialNumber).HasMaxLength(255);
        b.Property(d => d.BluetoothAddress).HasMaxLength(12);
        b.Property(d => d.PnpDeviceId).HasMaxLength(400);

        b.HasIndex(d => d.DeviceType);
        b.HasIndex(d => d.SerialNumber);
        b.HasIndex(d => new { d.VendorId, d.ProductId });

        b.HasOne(d => d.LastSeenComputer)
            .WithMany()
            .HasForeignKey(d => d.LastSeenComputerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(d => d.Assignments)
            .WithOne(a => a.Device)
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Navigation(d => d.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(d => d.ActiveAssignment);
    }
}

internal sealed class ComputerConfiguration : IEntityTypeConfiguration<Computer>
{
    public void Configure(EntityTypeBuilder<Computer> b)
    {
        b.ToTable("Computers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Hostname).IsRequired().HasMaxLength(Computer.MaxHostnameLength);
        b.HasIndex(c => c.Hostname).IsUnique().HasDatabaseName("UX_Computers_Hostname");
        b.Property(c => c.WindowsVersion).HasMaxLength(200);
    }
}

internal sealed class ResponsiblePersonConfiguration : IEntityTypeConfiguration<ResponsiblePerson>
{
    public void Configure(EntityTypeBuilder<ResponsiblePerson> b)
    {
        b.ToTable("ResponsiblePeople");
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).IsRequired().HasMaxLength(ResponsiblePerson.MaxNameLength);
        b.Property(p => p.Department).HasMaxLength(ResponsiblePerson.MaxDepartmentLength);
        b.HasIndex(p => p.Name);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.ToTable("Assignments", t =>
        {
            t.HasCheckConstraint("CK_Assignments_Target",
                "\"ResponsiblePersonId\" IS NOT NULL OR \"ExpectedComputerId\" IS NOT NULL");
            t.HasCheckConstraint("CK_Assignments_ActiveState",
                "(\"IsActive\" = 1 AND \"EndedAt\" IS NULL) OR (\"IsActive\" = 0 AND \"EndedAt\" IS NOT NULL)");
        });

        b.HasKey(a => a.Id);

        // The core invariant, enforced by the database too: at most ONE active assignment per device.
        b.HasIndex(a => a.DeviceId)
            .IsUnique()
            .HasFilter("\"IsActive\" = 1")
            .HasDatabaseName("UX_Assignments_Device_Active");

        b.HasIndex(a => new { a.DeviceId, a.AssignedAt }).HasDatabaseName("IX_Assignments_Device_AssignedAt");
        b.HasIndex(a => a.ResponsiblePersonId);
        b.HasIndex(a => a.ExpectedComputerId);

        b.HasOne(a => a.ResponsiblePerson).WithMany().HasForeignKey(a => a.ResponsiblePersonId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(a => a.ExpectedComputer).WithMany().HasForeignKey(a => a.ExpectedComputerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeviceObservationConfiguration : IEntityTypeConfiguration<DeviceObservation>
{
    public void Configure(EntityTypeBuilder<DeviceObservation> b)
    {
        b.ToTable("DeviceObservations", t =>
        {
            t.HasCheckConstraint("CK_DeviceObservations_Range", "\"LastObservedAt\" >= \"ObservedAt\"");
            t.HasCheckConstraint("CK_DeviceObservations_ScanCount", "\"ScanCount\" >= 1");
        });

        b.HasKey(o => o.Id);
        b.HasIndex(o => new { o.DeviceId, o.LastObservedAt }).HasDatabaseName("IX_DeviceObservations_Device_Last");
        b.HasIndex(o => new { o.ComputerId, o.LastObservedAt });

        b.HasOne(o => o.Device).WithMany().HasForeignKey(o => o.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(o => o.Computer).WithMany().HasForeignKey(o => o.ComputerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeviceMovementConfiguration : IEntityTypeConfiguration<DeviceMovement>
{
    public void Configure(EntityTypeBuilder<DeviceMovement> b)
    {
        b.ToTable("DeviceMovements", t =>
            t.HasCheckConstraint("CK_DeviceMovements_Type", "\"MovementType\" IN ('Assignment', 'Transfer')"));

        b.HasKey(m => m.Id);
        b.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(m => m.Reason).IsRequired().HasMaxLength(DeviceMovement.MaxReasonLength);
        b.HasIndex(m => new { m.DeviceId, m.ChangedAt }).HasDatabaseName("IX_DeviceMovements_Device_ChangedAt");

        b.HasOne(m => m.Device).WithMany().HasForeignKey(m => m.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(m => m.FromResponsiblePerson).WithMany().HasForeignKey(m => m.FromResponsiblePersonId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(m => m.ToResponsiblePerson).WithMany().HasForeignKey(m => m.ToResponsiblePersonId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(m => m.FromComputer).WithMany().HasForeignKey(m => m.FromComputerId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(m => m.ToComputer).WithMany().HasForeignKey(m => m.ToComputerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
