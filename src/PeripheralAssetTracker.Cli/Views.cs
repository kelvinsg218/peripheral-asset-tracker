using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Cli;

/// <summary>Formatting of domain data for the console. No business rules here.</summary>
internal static class Views
{
    public static string TypeLabel(DeviceType type) => type switch
    {
        DeviceType.Mouse => "Mouse",
        DeviceType.Keyboard => "Teclado",
        _ => "Outro"
    };

    public static string BusLabel(BusType bus) => bus switch
    {
        BusType.Usb => "USB",
        BusType.Bluetooth => "Bluetooth",
        BusType.BluetoothLowEnergy => "Bluetooth LE",
        BusType.Internal => "Interno (ACPI/I2C/PS2)",
        _ => "Desconhecida"
    };

    public static string Confidence(FingerprintConfidence confidence) => confidence.ToString().ToUpperInvariant();

    public static string Status(DeviceConditions conditions) => string.Join(" + ", conditions.ToCodes());

    public static string ShortFingerprint(string fingerprint) => fingerprint[..16] + "…";

    public static void ScanItem(ConsoleUi ui, int index, ScanItem item, bool details)
    {
        var d = item.Discovered;
        var name = item.Registered?.DeviceName ?? d.DeviceName ?? "Dispositivo sem nome";
        ui.Line(new string('-', 56));
        ui.Line($"[{index}] {name}");
        ui.Line(new string('-', 56));
        ui.Field("Tipo", TypeLabel(item.Registered?.DeviceType ?? d.SuggestedType));
        ui.Field("Fabricante", d.Manufacturer);
        ui.Field("VID", d.VendorId);
        ui.Field("PID", d.ProductId);
        ui.Field("Serial", d.SerialNumber);
        if (d.BluetoothAddress is not null)
        {
            ui.Field("Endereço Bluetooth", d.BluetoothAddress);
        }

        ui.Field("Conexão", BusLabel(d.BusType) + (d.IsBuiltIn ? " — integrado ao computador" : string.Empty));
        ui.Field("Fingerprint", details ? item.Fingerprint.Fingerprint : ShortFingerprint(item.Fingerprint.Fingerprint));
        ui.Field("Confiança", $"{Confidence(item.Fingerprint.Confidence)} — {item.Fingerprint.Basis}");

        if (item.Registered is { } r)
        {
            ui.Field("Registro", $"REGISTERED (#{r.Id})");
            ui.Field("Responsável", r.ResponsiblePersonName ?? "UNASSIGNED");
            ui.Field("Computador esperado", r.ExpectedComputerHostname ?? "Não definido");
            ui.Field("Computador observado", r.LastSeenComputerHostname);
        }
        else
        {
            ui.Field("Registro", "NOT REGISTERED");
        }

        ui.Field("Status", Status(item.Conditions));
        if (item.Conditions.HasFlag(DeviceConditions.UnexpectedComputer))
        {
            ui.Warning("DEVICE FOUND ON UNEXPECTED COMPUTER (a atribuição NÃO foi alterada).");
        }

        if (item.SharedFingerprintCount > 1)
        {
            ui.Warning($"{item.SharedFingerprintCount} dispositivos conectados geraram o MESMO fingerprint: " +
                       "o Windows não fornece dados para diferenciá-los.");
        }

        foreach (var note in d.Notes)
        {
            ui.Line("  * " + note);
        }

        if (details)
        {
            ui.Field("PNP Device ID", d.PnpDeviceId);
            ui.Field("Funções HID", string.Join(", ", d.Functions));
            ui.Field("Container ID", d.ContainerId);
            ui.Line($"Entradas PnP/HID agrupadas ({d.InterfaceInstanceIds.Count}):");
            foreach (var id in d.InterfaceInstanceIds)
            {
                ui.Line("    " + id);
            }

            ui.Line("Hardware IDs:");
            foreach (var id in d.HardwareIds)
            {
                ui.Line("    " + id);
            }

            ui.Field("Forma canônica", item.Fingerprint.CanonicalForm);
        }
    }

    public static void DeviceTable(ConsoleUi ui, IReadOnlyList<DeviceSummary> devices)
    {
        ui.Table(
            ["#", "Tipo", "Dispositivo", "VID:PID", "Conf.", "Responsável", "Esperado", "Visto em", "Últ. vez", "Status"],
            devices.Select(d => (IReadOnlyList<string?>)
            [
                d.Id.ToString(),
                TypeLabel(d.DeviceType),
                d.DeviceName,
                d.VendorId is null ? null : $"{d.VendorId}:{d.ProductId}",
                Confidence(d.Confidence),
                d.ResponsiblePersonName,
                d.ExpectedComputerHostname,
                d.LastSeenComputerHostname,
                ConsoleUi.FormatDate(d.LastSeenAt),
                Status(d.Conditions)
            ]),
            [5, 7, 28, 9, 6, 20, 16, 16, 16, 44]);
        ui.Line($"Total: {devices.Count}");
    }

    public static void DeviceDetails(ConsoleUi ui, DeviceSummary d)
    {
        ui.Section($"Dispositivo #{d.Id}");
        ui.Field("Nome", d.DeviceName);
        ui.Field("Tipo", TypeLabel(d.DeviceType));
        ui.Field("Fabricante", d.Manufacturer);
        ui.Field("VID", d.VendorId);
        ui.Field("PID", d.ProductId);
        ui.Field("Serial", d.SerialNumber);
        if (d.BluetoothAddress is not null)
        {
            ui.Field("Endereço Bluetooth", d.BluetoothAddress);
        }

        ui.Field("Conexão", BusLabel(d.BusType));
        ui.Field("PNP Device ID", d.PnpDeviceId);
        ui.Field("Fingerprint", d.Fingerprint);
        ui.Field("Confiança", $"{Confidence(d.Confidence)} — {d.FingerprintBasis}");
        ui.Field("Responsável", d.ResponsiblePersonName is null
            ? "UNASSIGNED"
            : d.ResponsiblePersonName + (d.ResponsibleDepartment is null ? "" : $" ({d.ResponsibleDepartment})"));
        ui.Field("Computador esperado", d.ExpectedComputerHostname ?? "Não definido");
        ui.Field("Último computador", d.LastSeenComputerHostname);
        ui.Field("Primeira detecção", ConsoleUi.FormatDate(d.FirstSeenAt));
        ui.Field("Última detecção", ConsoleUi.FormatDate(d.LastSeenAt));
        ui.Field("Status", Status(d.Conditions));
    }

    public static void History(ConsoleUi ui, DeviceHistory history)
    {
        ui.Section($"DEVICE: {history.Device.DeviceName} (#{history.Device.Id})");
        foreach (var e in history.Entries)
        {
            ui.Line();
            ui.Line(ConsoleUi.FormatDate(e.At));
            switch (e.Kind)
            {
                case HistoryEntryKind.Registered:
                    ui.Line("  Registered");
                    break;
                case HistoryEntryKind.Assigned:
                    ui.Line("  Assigned");
                    ui.Line($"  {ConsoleUi.OrUnavailable(e.ToPerson)} / {ConsoleUi.OrUnavailable(e.ToComputer)}");
                    ui.Line($"  Motivo: {e.Reason}");
                    break;
                case HistoryEntryKind.Transferred:
                    ui.Line("  Transferred");
                    ui.Line($"  {ConsoleUi.OrUnavailable(e.FromPerson)} / {ConsoleUi.OrUnavailable(e.FromComputer)}");
                    ui.Line("  →");
                    ui.Line($"  {ConsoleUi.OrUnavailable(e.ToPerson)} / {ConsoleUi.OrUnavailable(e.ToComputer)}");
                    ui.Line($"  Motivo: {e.Reason}");
                    break;
                case HistoryEntryKind.Observed:
                    ui.Line("  Observed");
                    ui.Line($"  {e.ToComputer}" +
                            (e.ScanCount > 1 ? $" (confirmado em {e.ScanCount} scans até {ConsoleUi.FormatDate(e.ObservedUntil)})" : string.Empty));
                    if (e.UnexpectedComputer)
                    {
                        ui.Line("  WARNING: unexpected computer");
                    }

                    break;
            }
        }
    }
}
