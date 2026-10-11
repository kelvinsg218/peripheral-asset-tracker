using Microsoft.Extensions.DependencyInjection;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Cli;

/// <summary>Use-case flows shared by the interactive menu and the direct commands.</summary>
internal sealed class Workflows(CliSession session)
{
    private readonly Prompts _prompts = new(session);
    private ConsoleUi Ui => session.Ui;

    public Prompts Prompts => _prompts;

    public async Task<ScanReport> ScanAsync(bool includeBuiltIn, bool details)
    {
        Ui.Info("Consultando dispositivos conectados...");
        var report = await session.Use(sp =>
            sp.GetRequiredService<ScanService>().ScanAsync(session.CurrentComputerId, includeBuiltIn));
        session.LastScan = report;

        Ui.Title($"SCAN — {report.Hostname} — {ConsoleUi.FormatDate(report.ScannedAt)}");
        foreach (var warning in report.Discovery.Warnings)
        {
            Ui.Warning(warning);
        }

        for (var i = 0; i < report.Items.Count; i++)
        {
            Views.ScanItem(Ui, i + 1, report.Items[i], details);
        }

        Ui.Line();
        Ui.Info($"{report.Items.Count} dispositivo(s) encontrado(s): " +
                $"{report.Items.Count(x => x.IsRegistered)} cadastrado(s), " +
                $"{report.Items.Count(x => !x.IsRegistered)} não cadastrado(s). " +
                $"({report.Discovery.RawEntryCount} entradas HID/PnP agrupadas" +
                (report.Discovery.IgnoredVirtualEntryCount > 0 ? $", {report.Discovery.IgnoredVirtualEntryCount} virtuais ignoradas" : "") + ")");
        if (report.HiddenBuiltInCount > 0)
        {
            Ui.Info($"{report.HiddenBuiltInCount} dispositivo(s) integrado(s) ao computador oculto(s) (teclado/touchpad do notebook). " +
                    "Use 'scan --all' para exibi-los.");
        }

        return report;
    }

    /// <summary>Interactive registration of a scanned device.</summary>
    public async Task RegisterInteractiveAsync(ScanItem item)
    {
        if (item.IsRegistered)
        {
            Ui.Warning($"Este dispositivo já está cadastrado (#{item.Registered!.Id}).");
            return;
        }

        var d = item.Discovered;
        Ui.Section("Cadastro de dispositivo — propriedades descobertas");
        Ui.Field("Nome detectado", d.DeviceName);
        Ui.Field("Tipo sugerido", Views.TypeLabel(d.SuggestedType));
        Ui.Field("Fabricante", d.Manufacturer);
        Ui.Field("VID / PID", d.VendorId is null ? null : $"{d.VendorId} / {ConsoleUi.OrUnavailable(d.ProductId)}");
        Ui.Field("Número de série", d.SerialNumber);
        Ui.Field("Conexão", Views.BusLabel(d.BusType));
        Ui.Field("ID do dispositivo PnP", d.PnpDeviceId);
        Ui.Field("Fingerprint", item.Fingerprint.Fingerprint);
        Ui.Field("Confiança", $"{Views.Confidence(item.Fingerprint.Confidence)} — {item.Fingerprint.Basis}");
        if (item.Fingerprint.Confidence == FingerprintConfidence.Low)
        {
            Ui.Warning("Confiança LOW: outra unidade idêntica deste modelo produziria o mesmo fingerprint.");
        }

        var type = _prompts.AskDeviceType(d.SuggestedType);
        if (type is null)
        {
            return;
        }

        var friendlyName = Ui.Ask($"Nome amigável (Enter = \"{d.DeviceName}\"):");
        var personId = await _prompts.AskPersonAsync("Responsável");
        var computerId = await _prompts.AskComputerAsync("Computador esperado");

        if (personId is null && computerId is null)
        {
            Ui.Info("Sem responsável e sem computador esperado: o dispositivo ficará UNASSIGNED.");
        }

        if (!Ui.Confirm("Confirmar cadastro?"))
        {
            Ui.Info("Cadastro cancelado.");
            return;
        }

        var device = await session.Use(sp => sp.GetRequiredService<DeviceRegistrationService>().RegisterAsync(
            d, new RegisterDeviceRequest(type.Value, friendlyName, personId, computerId), session.CurrentComputerId));
        Ui.Success($"Dispositivo cadastrado como #{device.Id} ({device.DeviceName}).");
    }

    public async Task AssignInteractiveAsync()
    {
        var unassigned = await session.Use(sp => sp.GetRequiredService<InventoryQueryService>()
            .ListAsync(new DeviceFilter { OnlyUnassigned = true }));
        var candidates = unassigned.Where(d => !d.HasActiveAssignment).ToList();
        Ui.Section("Dispositivos sem atribuição ativa");
        Views.DeviceTable(Ui, candidates);

        var device = await _prompts.AskDeviceAsync("Dispositivo");
        if (device is null)
        {
            return;
        }

        if (device.HasActiveAssignment)
        {
            Ui.Warning("Este dispositivo já possui atribuição ativa. Use 'Transferir dispositivo'.");
            return;
        }

        var personId = await _prompts.AskPersonAsync("Responsável");
        var computerId = await _prompts.AskComputerAsync("Computador esperado");
        if (personId is null && computerId is null)
        {
            Ui.Warning("Informe ao menos um responsável ou um computador esperado.");
            return;
        }

        Ui.Line();
        Ui.Field("Dispositivo", $"{device.DeviceName} (#{device.Id})");
        await ShowTargetAsync(personId, computerId);
        if (!Ui.Confirm("Confirmar atribuição?"))
        {
            return;
        }

        await session.Use(sp => sp.GetRequiredService<AssignmentService>().AssignAsync(device.Id, personId, computerId));
        Ui.Success("Atribuição criada.");
    }

    public async Task TransferInteractiveAsync()
    {
        var device = await _prompts.AskDeviceAsync("Dispositivo a transferir");
        if (device is null)
        {
            return;
        }

        if (!device.HasActiveAssignment)
        {
            Ui.Warning("Este dispositivo não possui atribuição ativa. Use 'Atribuir dispositivo'.");
            return;
        }

        Ui.Line();
        Ui.Field("Dispositivo", $"{device.DeviceName} (#{device.Id})");
        Ui.Field("Responsável atual", device.ResponsiblePersonName ?? "Nenhum");
        Ui.Field("Computador atual", device.ExpectedComputerHostname ?? "Nenhum");
        Ui.Line();

        var person = await _prompts.AskPersonAsync("Novo responsável", allowKeep: true);
        var computer = await _prompts.AskComputerAsync("Novo computador", allowKeep: true);
        var newPerson = person == Prompts.Keep ? device.ResponsiblePersonId : person;
        var newComputer = computer == Prompts.Keep ? device.ExpectedComputerId : computer;

        var reason = Ui.Ask("Motivo:");
        if (string.IsNullOrWhiteSpace(reason))
        {
            Ui.Warning("O motivo é obrigatório. Transferência cancelada.");
            return;
        }

        Ui.Line();
        Ui.Field("Dispositivo", device.DeviceName);
        Ui.Field("Responsável atual", device.ResponsiblePersonName ?? "Nenhum");
        Ui.Field("Computador atual", device.ExpectedComputerHostname ?? "Nenhum");
        await ShowTargetAsync(newPerson, newComputer, "Novo responsável", "Novo computador");
        Ui.Field("Motivo", reason);
        if (!Ui.Confirm("Confirmar?"))
        {
            Ui.Info("Transferência cancelada.");
            return;
        }

        await session.Use(sp => sp.GetRequiredService<AssignmentService>()
            .TransferAsync(new TransferRequest(device.Id, newPerson, newComputer, reason)));
        Ui.Success("Transferência registrada.");
    }

    public async Task ShowHistoryAsync(int deviceId)
    {
        var history = await session.Use(sp => sp.GetRequiredService<HistoryService>().GetDeviceHistoryAsync(deviceId));
        Views.History(Ui, history);
    }

    public async Task ShowRecentMovementsAsync()
    {
        var movements = await session.Use(sp => sp.GetRequiredService<HistoryService>().GetRecentMovementsAsync());
        Ui.Section("Movimentações oficiais recentes");
        Ui.Table(
            ["Data", "#", "Dispositivo", "Tipo", "De", "Para", "Motivo"],
            movements.Select(m => (IReadOnlyList<string?>)
            [
                ConsoleUi.FormatDate(m.ChangedAt),
                m.DeviceId.ToString(),
                m.DeviceName,
                m.Type == MovementType.Transfer ? "Transfer" : "Assignment",
                m.Type == MovementType.Transfer ? $"{m.FromPerson ?? "-"} / {m.FromComputer ?? "-"}" : "-",
                $"{m.ToPerson ?? "-"} / {m.ToComputer ?? "-"}",
                m.Reason
            ]),
            [16, 5, 24, 10, 28, 28, 40]);
    }

    public async Task ShowComputersAsync()
    {
        var computers = await session.Use(sp => sp.GetRequiredService<ComputerService>().ListAsync());
        Ui.Section("Computadores conhecidos");
        Ui.Table(
            ["#", "Hostname", "Windows", "Primeira vez", "Última vez", "Atribuídos", "Observados (30d)"],
            computers.Select(c => (IReadOnlyList<string?>)
            [
                c.Id.ToString(),
                c.Hostname + (c.Id == session.CurrentComputerId ? " *" : ""),
                c.WindowsVersion ?? ConsoleUi.Unavailable,
                c.FirstSeenAt is null ? "Nunca executado" : ConsoleUi.FormatDate(c.FirstSeenAt),
                c.LastSeenAt is null ? "Nunca executado" : ConsoleUi.FormatDate(c.LastSeenAt),
                c.AssignedDeviceCount.ToString(),
                c.RecentlyObservedDeviceCount.ToString()
            ]),
            [5, 20, 48, 16, 16, 10, 16]);
        Ui.Info("* computador atual. \"Nunca executado\": computador informado manualmente, onde o aplicativo ainda não rodou.");
    }

    public async Task ExportAsync(char delimiter)
    {
        var path = await session.Use(sp => sp.GetRequiredService<InventoryExportService>().ExportAsync(delimiter));
        Ui.Success($"Inventário exportado: {path}");
    }

    private async Task ShowTargetAsync(int? personId, int? computerId,
        string personLabel = "Responsável", string computerLabel = "Computador esperado")
    {
        var personName = personId is null
            ? "Nenhum"
            : (await session.Use(sp => sp.GetRequiredService<PeopleService>().FindAsync(personId.Value)))?.Name;
        var hostname = computerId is null
            ? "Nenhum"
            : (await session.Use(sp => sp.GetRequiredService<ComputerService>().FindAsync(computerId.Value.ToString())))?.Hostname;
        Ui.Field(personLabel, personName);
        Ui.Field(computerLabel, hostname);
    }
}
