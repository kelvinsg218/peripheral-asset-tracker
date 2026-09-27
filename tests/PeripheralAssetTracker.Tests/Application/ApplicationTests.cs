using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PeripheralAssetTracker.Application;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain;
using PeripheralAssetTracker.Domain.Enums;
using PeripheralAssetTracker.Tests.TestSupport;

namespace PeripheralAssetTracker.Tests.Application;

public class ApplicationTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<ScanReport> ScanAsync(int computerId, TimeSpan? window = null)
    {
        await using var db = _db.NewContext();
        return await _db.Scan(db, window).ScanAsync(computerId, includeBuiltIn: false);
    }

    [Fact]
    public async Task Current_computer_is_created_once_then_updated()
    {
        var first = await _db.AddComputerAsync("ti-04");
        _db.Clock.Advance(TimeSpan.FromDays(1));
        var second = await _db.AddComputerAsync("TI-04");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("TI-04", second.Hostname);
        Assert.Equal(first.FirstSeenAt, second.FirstSeenAt);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, second.LastSeenAt);
    }

    [Fact]
    public async Task Computer_created_by_technician_is_unseen_and_reused()
    {
        await using var db = _db.NewContext();
        var service = _db.Computers(db, "TI-04");

        var created = await service.GetOrCreateAsync("financeiro-03");
        var again = await service.GetOrCreateAsync("FINANCEIRO-03");

        Assert.Equal(created.Id, again.Id);
        Assert.Null(created.FirstSeenAt);
        Assert.Null(created.WindowsVersion);
    }

    [Fact]
    public async Task Scan_reports_unregistered_devices_without_recording_anything()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        _db.Discovery.Devices.Add(DeviceFixtures.DellWithoutSerial());

        var report = await ScanAsync(computer.Id);

        var item = Assert.Single(report.Items);
        Assert.False(item.IsRegistered);
        Assert.Equal(["NOT_REGISTERED", "LOW_CONFIDENCE"], item.Conditions.ToCodes());
        await using var db = _db.NewContext();
        Assert.Equal(0, await db.DeviceObservations.CountAsync());
    }

    [Fact]
    public async Task Registration_without_assignment_is_unassigned()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        var device = await _db.RegisterAsync(DeviceFixtures.DellWithoutSerial(), computer.Id);

        await using var db = _db.NewContext();
        var summary = await new InventoryQueryService(db).GetAsync(device.Id);
        Assert.Equal(["UNASSIGNED", "LOW_CONFIDENCE"], summary!.Conditions.ToCodes());
        Assert.Equal(0, await db.Assignments.CountAsync());
        Assert.Equal(1, await db.DeviceObservations.CountAsync()); // registration records where it was found
    }

    [Fact]
    public async Task Registration_allows_type_and_name_correction()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        await using var db = _db.NewContext();
        var device = await _db.Registration(db).RegisterAsync(
            DeviceFixtures.LogitechWithSerial(), new RegisterDeviceRequest(DeviceType.Other, "  Mouse do Kelvin ", null, null), computer.Id);

        Assert.Equal(DeviceType.Other, device.DeviceType);
        Assert.Equal("Mouse do Kelvin", device.DeviceName);
    }

    [Fact]
    public async Task Registering_the_same_fingerprint_twice_is_rejected_with_honest_message()
    {
        var computer = await _db.AddComputerAsync("TI-04");
        await _db.RegisterAsync(DeviceFixtures.DellWithoutSerial("5&1&0&1"), computer.Id);

        var ex = await Assert.ThrowsAsync<AssetTrackerException>(() =>
            _db.RegisterAsync(DeviceFixtures.DellWithoutSerial("5&1&0&2"), computer.Id));
        Assert.Contains("LOW", ex.Message);
    }

    [Fact]
    public async Task Registration_with_unknown_person_fails_and_saves_nothing()
    {
        var computer = await _db.AddComputerAsync("TI-04");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), computer.Id, personId: 404));
        await using var db = _db.NewContext();
        Assert.Equal(0, await db.Devices.CountAsync());
    }

    [Fact]
    public async Task Known_device_on_expected_computer_is_ok()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, ti04.Id);
        _db.Discovery.Devices.Add(DeviceFixtures.LogitechWithSerial());

        var item = Assert.Single((await ScanAsync(ti04.Id)).Items);

        Assert.True(item.IsRegistered);
        Assert.Equal(["OK"], item.Conditions.ToCodes());
        Assert.Equal("Kelvin", item.Registered!.ResponsiblePersonName);
        Assert.Equal("TI-04", item.Registered.ExpectedComputerHostname);
        Assert.Equal("TI-04", item.Registered.LastSeenComputerHostname);
    }

    [Fact]
    public async Task Device_found_on_unexpected_computer_is_flagged_and_assignment_is_not_changed()
    {
        var fin02 = await _db.AddComputerAsync("FINANCEIRO-02");
        var recepcao = await _db.AddComputerAsync("RECEPCAO-01");
        var joao = await _db.AddPersonAsync("João");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), fin02.Id, joao.Id, fin02.Id);
        _db.Clock.Advance(TimeSpan.FromDays(19));
        _db.Discovery.Devices.Add(DeviceFixtures.LogitechWithSerial());

        var item = Assert.Single((await ScanAsync(recepcao.Id)).Items);

        Assert.True(item.Conditions.HasFlag(DeviceConditions.UnexpectedComputer));
        Assert.Equal("FINANCEIRO-02", item.Registered!.ExpectedComputerHostname);
        Assert.Equal("RECEPCAO-01", item.Registered.LastSeenComputerHostname);

        await using var db = _db.NewContext();
        var assignment = await db.Assignments.SingleAsync(a => a.DeviceId == device.Id);
        Assert.True(assignment.IsActive);
        Assert.Equal(fin02.Id, assignment.ExpectedComputerId);

        var listed = await new InventoryQueryService(db).ListAsync(new DeviceFilter { OnlyUnexpectedComputer = true });
        Assert.Equal(device.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Repeated_scans_are_coalesced_but_location_changes_are_kept()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var recepcao = await _db.AddComputerAsync("RECEPCAO-01");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id);
        _db.Discovery.Devices.Add(DeviceFixtures.LogitechWithSerial());

        for (var i = 0; i < 5; i++)
        {
            _db.Clock.Advance(TimeSpan.FromSeconds(10));
            await ScanAsync(ti04.Id);
        }

        _db.Clock.Advance(TimeSpan.FromMinutes(1));
        await ScanAsync(recepcao.Id);           // moved → new observation
        _db.Clock.Advance(TimeSpan.FromMinutes(1));
        await ScanAsync(ti04.Id);               // back → new observation
        _db.Clock.Advance(TimeSpan.FromHours(2));
        await ScanAsync(ti04.Id);               // after the window → new observation

        await using var db = _db.NewContext();
        var observations = await db.DeviceObservations.Where(o => o.DeviceId == device.Id).OrderBy(o => o.Id).ToListAsync();
        Assert.Equal([ti04.Id, recepcao.Id, ti04.Id, ti04.Id], observations.Select(o => o.ComputerId));
        Assert.Equal(6, observations[0].ScanCount); // registration + 5 quick scans
        var stored = await db.Devices.SingleAsync(d => d.Id == device.Id);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, stored.LastSeenAt);
        Assert.Equal(ti04.Id, stored.LastSeenComputerId);
    }

    [Fact]
    public async Task Identical_units_in_the_same_scan_are_flagged_as_ambiguous()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        _db.Discovery.Devices.Add(DeviceFixtures.DellWithoutSerial("5&1&0&1"));
        _db.Discovery.Devices.Add(DeviceFixtures.DellWithoutSerial("5&1&0&2"));

        var report = await ScanAsync(ti04.Id);

        Assert.Equal(2, report.Items.Count);
        Assert.All(report.Items, i => Assert.True(i.Conditions.HasFlag(DeviceConditions.AmbiguousFingerprint)));
        Assert.All(report.Items, i => Assert.Equal(2, i.SharedFingerprintCount));
    }

    [Fact]
    public async Task Builtin_devices_are_hidden_unless_requested()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        _db.Discovery.Devices.Add(DeviceFixtures.LogitechWithSerial());
        _db.Discovery.Devices.Add(DeviceFixtures.DellWithoutSerial() with { IsBuiltIn = true, BusType = BusType.Internal });

        await using var db = _db.NewContext();
        var hidden = await _db.Scan(db).ScanAsync(ti04.Id, includeBuiltIn: false);
        var shown = await _db.Scan(db).ScanAsync(ti04.Id, includeBuiltIn: true);

        Assert.Single(hidden.Items);
        Assert.Equal(1, hidden.HiddenBuiltInCount);
        Assert.Equal(2, shown.Items.Count);
    }

    [Fact]
    public async Task Discovery_failure_is_reported_as_friendly_error()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        _db.Discovery.Failure = new DeviceDiscoveryException("Não foi possível consultar os dispositivos conectados.");

        var ex = await Assert.ThrowsAsync<DeviceDiscoveryException>(() => ScanAsync(ti04.Id));
        Assert.Equal("Não foi possível consultar os dispositivos conectados.", ex.Message);
    }

    [Fact]
    public async Task Assign_creates_active_assignment_and_movement_and_refuses_second()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id);

        await using (var db = _db.NewContext())
        {
            await _db.Assignments(db).AssignAsync(device.Id, kelvin.Id, ti04.Id);
        }

        await using (var db = _db.NewContext())
        {
            await Assert.ThrowsAsync<DomainException>(() => _db.Assignments(db).AssignAsync(device.Id, kelvin.Id, ti04.Id));
        }

        await using var check = _db.NewContext();
        Assert.Equal(1, await check.Assignments.CountAsync(a => a.DeviceId == device.Id && a.IsActive));
        Assert.Equal(MovementType.Assignment, (await check.DeviceMovements.SingleAsync()).MovementType);
    }

    [Fact]
    public async Task Transfer_of_unassigned_device_is_rejected()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id);

        await using var db = _db.NewContext();
        await Assert.ThrowsAsync<DomainException>(() =>
            _db.Assignments(db).TransferAsync(new TransferRequest(device.Id, kelvin.Id, ti04.Id, "x")));
    }

    [Fact]
    public async Task History_shows_registration_observations_warnings_and_transfer_in_order()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var recepcao = await _db.AddComputerAsync("RECEPCAO-01");
        var fin03 = await _db.AddComputerAsync("FINANCEIRO-03");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var joao = await _db.AddPersonAsync("João");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, ti04.Id);
        _db.Discovery.Devices.Add(DeviceFixtures.LogitechWithSerial());

        _db.Clock.Advance(TimeSpan.FromDays(9));
        await ScanAsync(ti04.Id);
        _db.Clock.Advance(TimeSpan.FromDays(10));
        await ScanAsync(recepcao.Id);
        _db.Clock.Advance(TimeSpan.FromDays(1));
        await using (var db = _db.NewContext())
        {
            await _db.Assignments(db).TransferAsync(new TransferRequest(device.Id, joao.Id, fin03.Id, "Equipment relocation"));
        }

        await using var ctx = _db.NewContext();
        var history = await new HistoryService(ctx, new InventoryQueryService(ctx)).GetDeviceHistoryAsync(device.Id);

        Assert.Equal(
            [HistoryEntryKind.Registered, HistoryEntryKind.Assigned, HistoryEntryKind.Observed, HistoryEntryKind.Observed,
             HistoryEntryKind.Observed, HistoryEntryKind.Transferred],
            history.Entries.Select(e => e.Kind));
        var unexpected = history.Entries.Where(e => e.UnexpectedComputer).ToList();
        Assert.Equal("RECEPCAO-01", Assert.Single(unexpected).ToComputer); // compared with the assignment active at that time
        var transfer = history.Entries.Last();
        Assert.Equal(("Kelvin", "TI-04", "João", "FINANCEIRO-03"), (transfer.FromPerson, transfer.FromComputer, transfer.ToPerson, transfer.ToComputer));
        Assert.Equal("Equipment relocation", transfer.Reason);
    }

    [Fact]
    public async Task Search_is_case_and_accent_insensitive_over_all_documented_fields()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var joao = await _db.AddPersonAsync("João Pereira", "Financeiro");
        var logitech = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, joao.Id, ti04.Id);
        var dell = await _db.RegisterAsync(DeviceFixtures.DellWithoutSerial(), ti04.Id);

        await using var db = _db.NewContext();
        var service = new InventoryQueryService(db);
        async Task<int[]> Ids(string term) => (await service.SearchAsync(term)).Select(d => d.Id).Order().ToArray();

        Assert.Equal([logitech.Id], await Ids("joao"));                 // responsible, accent-insensitive
        Assert.Equal([logitech.Id], await Ids("abc123"));               // serial
        Assert.Equal([logitech.Id], await Ids("046d"));                 // VID
        Assert.Equal([dell.Id], await Ids("2113"));                     // PID
        Assert.Equal([dell.Id], await Ids("dell"));                     // manufacturer / name
        Assert.Equal([logitech.Id], await Ids("g203"));                 // device name
        Assert.Equal(new[] { logitech.Id, dell.Id }.Order().ToArray(), await Ids("ti-04")); // hostname (expected or last seen)
        Assert.Equal([logitech.Id], await Ids(logitech.Fingerprint[..10].ToLowerInvariant())); // fingerprint prefix
        Assert.Empty(await Ids("inexistente"));
    }

    [Fact]
    public async Task Filters_select_the_expected_devices()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var fin02 = await _db.AddComputerAsync("FINANCEIRO-02");
        var kelvin = await _db.AddPersonAsync("Kelvin");
        var mouse = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, fin02.Id);
        var keyboard = await _db.RegisterAsync(DeviceFixtures.DellWithoutSerial(), ti04.Id);

        await using var db = _db.NewContext();
        var service = new InventoryQueryService(db);
        async Task<int[]> Ids(DeviceFilter f) => (await service.ListAsync(f)).Select(d => d.Id).Order().ToArray();

        Assert.Equal(2, (await Ids(DeviceFilter.All)).Length);
        Assert.Equal([mouse.Id], await Ids(new DeviceFilter { Type = DeviceType.Mouse }));
        Assert.Equal([keyboard.Id], await Ids(new DeviceFilter { Type = DeviceType.Keyboard }));
        Assert.Empty(await Ids(new DeviceFilter { Type = DeviceType.Other }));
        Assert.Equal([mouse.Id], await Ids(new DeviceFilter { ResponsiblePersonId = kelvin.Id }));
        Assert.Equal([mouse.Id], await Ids(new DeviceFilter { ComputerId = fin02.Id }));
        Assert.Equal([keyboard.Id], await Ids(new DeviceFilter { OnlyUnassigned = true }));
        Assert.Equal([keyboard.Id], await Ids(new DeviceFilter { OnlyLowConfidence = true }));
        Assert.Equal([mouse.Id], await Ids(new DeviceFilter { OnlyUnexpectedComputer = true })); // expected FIN-02, seen TI-04
    }

    [Fact]
    public async Task Resolve_accepts_id_hash_and_fingerprint_prefix()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var device = await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id);

        await using var db = _db.NewContext();
        var service = new InventoryQueryService(db);
        Assert.Equal(device.Id, (await service.ResolveAsync(device.Id.ToString())).Id);
        Assert.Equal(device.Id, (await service.ResolveAsync("#" + device.Id)).Id);
        Assert.Equal(device.Id, (await service.ResolveAsync(device.Fingerprint[..8].ToLowerInvariant())).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ResolveAsync("999"));
        await Assert.ThrowsAsync<AssetTrackerException>(() => service.ResolveAsync("ab"));
    }

    [Fact]
    public async Task Computers_and_people_lists_include_counts()
    {
        var ti04 = await _db.AddComputerAsync("TI-04");
        var kelvin = await _db.AddPersonAsync("Kelvin", "TI");
        await _db.AddPersonAsync("Maria");
        await _db.RegisterAsync(DeviceFixtures.LogitechWithSerial(), ti04.Id, kelvin.Id, ti04.Id);

        await using var db = _db.NewContext();
        var computers = await _db.Computers(db, "TI-04").ListAsync();
        var people = await new PeopleService(db, _db.Clock, NullLogger<PeopleService>.Instance).ListAsync();
        var searched = await new PeopleService(db, _db.Clock, NullLogger<PeopleService>.Instance).ListAsync("kel");

        var computer = Assert.Single(computers);
        Assert.Equal((1, 1), (computer.AssignedDeviceCount, computer.RecentlyObservedDeviceCount));
        Assert.Equal(1, people.Single(p => p.Name == "Kelvin").ActiveDeviceCount);
        Assert.Equal(0, people.Single(p => p.Name == "Maria").ActiveDeviceCount);
        Assert.Equal("Kelvin", Assert.Single(searched).Name);
    }
}
