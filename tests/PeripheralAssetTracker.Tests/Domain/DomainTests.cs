using PeripheralAssetTracker.Domain;
using PeripheralAssetTracker.Domain.Entities;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Domain.Services;

namespace PeripheralAssetTracker.Tests.Domain;

public class DomainTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string ValidFingerprint = new('A', 64);

    private static Device NewDevice(FingerprintConfidence confidence = FingerprintConfidence.High) =>
        Device.Register(new DeviceRegistrationData(
            ValidFingerprint, confidence, "test", DeviceType.Mouse, "  Logitech   M90 ", "Logitech", "046D", "C05A",
            null, null, BusType.Usb, @"USB\VID_046D&PID_C05A\5&1&0&1"), observedComputerId: 1, T0);

    [Fact]
    public void Device_registration_cleans_name_and_starts_unassigned()
    {
        var device = NewDevice();

        Assert.Equal("Logitech M90", device.DeviceName);
        Assert.Null(device.ActiveAssignment);
        Assert.Equal(T0, device.FirstSeenAt);
        Assert.Equal(1, device.LastSeenComputerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void Device_requires_a_sha256_fingerprint(string fingerprint) =>
        Assert.Throws<DomainException>(() => Device.Register(new DeviceRegistrationData(
            fingerprint, FingerprintConfidence.Low, "b", DeviceType.Mouse, "x", null, null, null, null, null, BusType.Usb, null), 1, T0));

    [Fact]
    public void Device_accepts_only_one_active_assignment()
    {
        var device = NewDevice();
        device.Assign(10, 20, null, T0);

        Assert.Throws<DomainException>(() => device.Assign(11, 21, null, T0.AddMinutes(1)));
        Assert.Single(device.Assignments, a => a.IsActive);
    }

    [Fact]
    public void Assignment_needs_person_or_computer()
    {
        var device = NewDevice();

        Assert.Throws<DomainException>(() => device.Assign(null, null, null, T0));
    }

    [Fact]
    public void Assignment_may_have_only_expected_computer()
    {
        var device = NewDevice();
        var (assignment, movement) = device.Assign(null, 20, null, T0);

        Assert.Null(assignment.ResponsiblePersonId);
        Assert.Equal(20, assignment.ExpectedComputerId);
        Assert.Equal(MovementType.Assignment, movement.MovementType);
        Assert.Equal("Atribuição inicial", movement.Reason);
    }

    [Fact]
    public void Transfer_ends_old_assignment_creates_new_one_and_records_movement()
    {
        var device = NewDevice();
        var (first, _) = device.Assign(10, 20, null, T0);
        var t1 = T0.AddDays(20);

        var ended = device.BeginTransfer(11, 30, "Equipment relocation", t1);
        var (second, movement) = device.CompleteTransfer(ended, 11, 30, "Equipment relocation", t1);

        Assert.Same(first, ended);
        Assert.False(first.IsActive);
        Assert.Equal(t1, first.EndedAt);
        Assert.True(second.IsActive);
        Assert.Same(second, device.ActiveAssignment);
        Assert.Equal(MovementType.Transfer, movement.MovementType);
        Assert.Equal((10, 20, 11, 30), (movement.FromResponsiblePersonId, movement.FromComputerId, movement.ToResponsiblePersonId, movement.ToComputerId));
        Assert.Equal("Equipment relocation", movement.Reason);
    }

    [Fact]
    public void Transfer_requires_active_assignment_reason_and_a_real_change()
    {
        var device = NewDevice();
        Assert.Throws<DomainException>(() => device.BeginTransfer(1, 2, "r", T0));

        device.Assign(10, 20, null, T0);
        Assert.Throws<DomainException>(() => device.BeginTransfer(11, 20, "  ", T0));
        Assert.Throws<DomainException>(() => device.BeginTransfer(10, 20, "same", T0));
        Assert.Throws<DomainException>(() => device.BeginTransfer(null, null, "none", T0));
        Assert.True(device.ActiveAssignment!.IsActive); // validation failures leave the assignment untouched
    }

    [Fact]
    public void Complete_transfer_without_begin_is_rejected()
    {
        var device = NewDevice();
        var (active, _) = device.Assign(10, 20, null, T0);

        Assert.Throws<DomainException>(() => device.CompleteTransfer(active, 11, 21, "r", T0));
    }

    [Fact]
    public void Assignment_was_active_at_uses_half_open_interval()
    {
        var device = NewDevice();
        var (first, _) = device.Assign(10, 20, null, T0);
        var t1 = T0.AddDays(1);
        device.BeginTransfer(11, 21, "r", t1);

        Assert.True(first.WasActiveAt(T0));
        Assert.True(first.WasActiveAt(t1.AddTicks(-1)));
        Assert.False(first.WasActiveAt(t1));
        Assert.False(first.WasActiveAt(T0.AddTicks(-1)));
    }

    [Fact]
    public void Sighting_updates_last_seen_and_observed_computer()
    {
        var device = NewDevice();
        device.RecordSighting(7, @"USB\VID_046D&PID_C05A\5&1&0&9", T0.AddHours(3));

        Assert.Equal(T0.AddHours(3), device.LastSeenAt);
        Assert.Equal(7, device.LastSeenComputerId);
        Assert.Equal(@"USB\VID_046D&PID_C05A\5&1&0&9", device.PnpDeviceId);
    }

    [Fact]
    public void Status_ok_when_assigned_expected_and_high_confidence()
    {
        var device = NewDevice();
        device.Assign(10, 20, null, T0);

        var status = DeviceStatusEvaluator.Evaluate(device.FingerprintConfidence, device.ActiveAssignment, observedComputerId: 20);

        Assert.Equal(DeviceConditions.None, status);
        Assert.Equal(["OK"], status.ToCodes());
    }

    [Fact]
    public void Expected_vs_observed_mismatch_raises_unexpected_computer_without_changing_assignment()
    {
        var device = NewDevice();
        var (assignment, _) = device.Assign(10, 20, null, T0);

        var status = DeviceStatusEvaluator.Evaluate(device.FingerprintConfidence, device.ActiveAssignment, observedComputerId: 99);

        Assert.True(status.HasFlag(DeviceConditions.UnexpectedComputer));
        Assert.Equal(20, assignment.ExpectedComputerId);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public void Multiple_conditions_coexist()
    {
        var device = NewDevice(FingerprintConfidence.Low);

        var status = DeviceStatusEvaluator.Evaluate(device.FingerprintConfidence, null, 5);

        Assert.Equal(["UNASSIGNED", "LOW_CONFIDENCE"], status.ToCodes());
    }

    [Fact]
    public void Assignment_with_computer_only_is_unassigned_but_still_checks_location()
    {
        var device = NewDevice();
        device.Assign(null, 20, null, T0);

        var status = DeviceStatusEvaluator.Evaluate(device.FingerprintConfidence, device.ActiveAssignment, 21);

        Assert.Equal(["UNASSIGNED", "UNEXPECTED_COMPUTER"], status.ToCodes());
    }

    [Fact]
    public void Unregistered_devices_report_not_registered_and_confidence()
    {
        Assert.Equal(["NOT_REGISTERED", "LOW_CONFIDENCE"], DeviceStatusEvaluator.EvaluateUnregistered(FingerprintConfidence.Low).ToCodes());
        Assert.Equal(["NOT_REGISTERED"], DeviceStatusEvaluator.EvaluateUnregistered(FingerprintConfidence.High).ToCodes());
    }

    [Fact]
    public void Observation_policy_extends_only_same_computer_within_window()
    {
        var device = NewDevice();
        var policy = new ObservationPolicy(TimeSpan.FromHours(1));
        var observation = DeviceObservation.Create(device, 1, T0);

        Assert.True(policy.ShouldExtend(observation, 1, T0.AddMinutes(59)));
        Assert.False(policy.ShouldExtend(observation, 2, T0.AddMinutes(1)));   // location change is always recorded
        Assert.False(policy.ShouldExtend(observation, 1, T0.AddMinutes(61)));  // new sighting session
        Assert.False(policy.ShouldExtend(null, 1, T0));
    }

    [Fact]
    public void Observation_extend_updates_range_and_count()
    {
        var observation = DeviceObservation.Create(NewDevice(), 1, T0);
        observation.Extend(T0.AddMinutes(10));

        Assert.Equal(T0, observation.ObservedAt);
        Assert.Equal(T0.AddMinutes(10), observation.LastObservedAt);
        Assert.Equal(2, observation.ScanCount);
    }

    [Theory]
    [InlineData("ti-04", "TI-04")]
    [InlineData("  Financeiro-02 ", "FINANCEIRO-02")]
    public void Computer_hostname_is_normalized(string input, string expected) =>
        Assert.Equal(expected, Computer.Create(input, null, T0).Hostname);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("TI 04")]
    public void Computer_hostname_is_validated(string input) =>
        Assert.Throws<DomainException>(() => Computer.Create(input, null, T0));

    [Fact]
    public void Computer_mark_seen_updates_last_seen_and_version()
    {
        var computer = Computer.Create("TI-04", "Windows 10", T0);
        computer.MarkSeen("Windows 11", T0.AddDays(1));

        Assert.Equal(T0, computer.FirstSeenAt);
        Assert.Equal(T0.AddDays(1), computer.LastSeenAt);
        Assert.Equal("Windows 11", computer.WindowsVersion);
    }

    [Fact]
    public void Computer_named_by_technician_is_never_seen_until_the_app_runs_there()
    {
        var computer = Computer.CreateUnseen("financeiro-03", T0);

        Assert.Null(computer.FirstSeenAt);
        Assert.Null(computer.LastSeenAt);
        Assert.Equal(T0, computer.CreatedAt);

        computer.MarkSeen("Windows 11", T0.AddDays(2));
        Assert.Equal(T0.AddDays(2), computer.FirstSeenAt);
        Assert.Equal(T0.AddDays(2), computer.LastSeenAt);
    }

    [Fact]
    public void Responsible_person_is_cleaned_and_validated()
    {
        var person = ResponsiblePerson.Create("  João   da Silva ", "  Financeiro ", T0);

        Assert.Equal("João da Silva", person.Name);
        Assert.Equal("Financeiro", person.Department);
        Assert.Throws<DomainException>(() => ResponsiblePerson.Create("   ", null, T0));
        Assert.Throws<DomainException>(() => ResponsiblePerson.Create(new string('x', 201), null, T0));
    }
}
