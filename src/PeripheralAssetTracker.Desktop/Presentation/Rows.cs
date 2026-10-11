using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Desktop.Presentation;

/// <summary>A registered device as shown in the inventory (read-only snapshot).</summary>
internal sealed class DeviceRow(DeviceSummary summary, DevicePresence presence, bool scanned)
{
    public DeviceSummary Summary { get; } = summary;
    public DevicePresence Presence { get; } = presence;

    public int Id => Summary.Id;
    public string Name => Labels.DeviceName(Summary.DeviceName);
    public string TypeLabel => Labels.Type(Summary.DeviceType);
    public string TypeGlyph => Summary.DeviceType switch
    {
        DeviceType.Mouse => "",
        DeviceType.Keyboard => "",
        _ => ""
    };

    public string Manufacturer => Labels.Text(Summary.Manufacturer);
    public string Model => Labels.Model(Summary.VendorId, Summary.ProductId);
    public string VendorId => Labels.Text(Summary.VendorId);
    public string ProductId => Labels.Text(Summary.ProductId);
    public string SerialNumber => Labels.Text(Summary.SerialNumber);
    public string BluetoothAddress => Labels.Text(Summary.BluetoothAddress);
    public string Bus => Labels.Bus(Summary.BusType);
    public string PnpDeviceId => Labels.Text(Summary.PnpDeviceId);
    public string Fingerprint => Summary.Fingerprint;
    public string FingerprintBasis => Labels.Text(Summary.FingerprintBasis);
    public string ConfidenceLabel => Labels.Confidence(Summary.Confidence);

    public string Responsible => Labels.Text(Summary.ResponsiblePersonName);
    public string Department => Labels.Text(Summary.ResponsibleDepartment);
    public string ExpectedComputer => Labels.Text(Summary.ExpectedComputerHostname);
    public string AssignedAt => Labels.Date(Summary.AssignedAt);
    public string LastSeenComputer => Labels.Text(Summary.LastSeenComputerHostname);
    public string FirstSeen => Labels.Date(Summary.FirstSeenAt);
    public string LastSeen => Labels.Date(Summary.LastSeenAt);
    public string RegisteredAt => Labels.Date(Summary.CreatedAt);

    public string PresenceLabel => Labels.Presence(Presence);
    public string PresenceHint => Labels.PresenceHint(Presence, scanned);
    public string AlertsText => Labels.ConditionsText(Summary.Conditions);
    public bool HasAlerts => Summary.Conditions != DeviceConditions.None;
    public bool HasActiveAssignment => Summary.HasActiveAssignment;
}

/// <summary>A unit found by the latest scan, registered ("Conhecido") or not ("Novo").</summary>
internal sealed class ScanRow(ScanItem item)
{
    public ScanItem Item { get; } = item;

    public bool IsRegistered => Item.IsRegistered;
    public bool IsBuiltIn => Item.Discovered.IsBuiltIn;
    public int? RegisteredId => Item.Registered?.Id;
    public string Name => Item.Registered?.DeviceName ?? Labels.DeviceName(Item.Discovered.DeviceName);
    public string TypeLabel => Labels.Type(Item.Registered?.DeviceType ?? Item.Discovered.SuggestedType);
    public string Manufacturer => Labels.Text(Item.Discovered.Manufacturer);
    public string Model => Labels.Model(Item.Discovered.VendorId, Item.Discovered.ProductId);
    public string Bus => Labels.Bus(Item.Discovered.BusType) + (IsBuiltIn ? " · integrado" : string.Empty);
    public string ConfidenceLabel => Labels.Confidence(Item.Fingerprint.Confidence);
    public string StatusLabel => IsRegistered ? "Conhecido" : "Novo";
    public string AlertsText => Labels.ConditionsText(Item.Conditions & ~DeviceConditions.NotRegistered);
    public bool HasAlerts => (Item.Conditions & ~DeviceConditions.NotRegistered) != DeviceConditions.None;
    public string Basis => Item.Fingerprint.Basis;
}

internal sealed record PersonRow(int Id, string Name, string Department, int ActiveDevices, string CreatedAt)
{
    public static PersonRow Create(PersonSummary p) =>
        new(p.Id, p.Name, Labels.Text(p.Department), p.ActiveDeviceCount, Labels.Date(p.CreatedAt));
}

internal sealed record ComputerRow(
    int Id, string Hostname, string WindowsVersion, string FirstSeen, string LastSeen, int Assigned, int RecentlyObserved, bool IsCurrent)
{
    public static ComputerRow Create(ComputerSummary c, int currentComputerId) =>
        new(c.Id, c.Hostname, Labels.Text(c.WindowsVersion), Labels.Date(c.FirstSeenAt), Labels.Date(c.LastSeenAt),
            c.AssignedDeviceCount, c.RecentlyObservedDeviceCount, c.Id == currentComputerId);
}

internal sealed record MovementRow(string When, string Kind, string Device, string From, string To, string Reason)
{
    public static MovementRow Create(MovementSummary m) =>
        new(Labels.Date(m.ChangedAt), Labels.Movement(m.Type), m.DeviceName,
            Party(m.FromPerson, m.FromComputer), Party(m.ToPerson, m.ToComputer), m.Reason);

    /// <summary>Short form for table cells: "Ana · PC-01", "Ana", "PC-01" or "—".</summary>
    internal static string Party(string? person, string? computer) =>
        person is null && computer is null ? Labels.Missing : string.Join(" · ", new[] { person, computer }.OfType<string>());

    /// <summary>Sentence form: "responsável Ana, computador PC-01".</summary>
    internal static string Describe(string? person, string? computer) =>
        $"responsável {person ?? "não definido"}, computador {computer ?? "não definido"}";
}

internal sealed record HistoryRow(string When, string Kind, string Description, bool IsWarning)
{
    public static HistoryRow Create(HistoryEntry e) => e.Kind switch
    {
        HistoryEntryKind.Registered => new(Labels.Date(e.At), Labels.HistoryKind(e.Kind), "Dispositivo cadastrado no inventário.", false),
        HistoryEntryKind.Assigned => new(Labels.Date(e.At), Labels.HistoryKind(e.Kind),
            $"Atribuído: {MovementRow.Describe(e.ToPerson, e.ToComputer)}" + Reason(e.Reason), false),
        HistoryEntryKind.Transferred => new(Labels.Date(e.At), Labels.HistoryKind(e.Kind),
            $"De {MovementRow.Describe(e.FromPerson, e.FromComputer)} para {MovementRow.Describe(e.ToPerson, e.ToComputer)}" + Reason(e.Reason), false),
        _ => new(Labels.Date(e.At), Labels.HistoryKind(e.Kind),
            $"Detectado em {Labels.Text(e.ToComputer)}" +
            (e.ScanCount > 1 ? $" (confirmado em {e.ScanCount} verificações até {Labels.Date(e.ObservedUntil)})" : string.Empty) +
            (e.UnexpectedComputer ? " — computador diferente do esperado" : string.Empty),
            e.UnexpectedComputer)
    };

    private static string Reason(string? reason) => string.IsNullOrWhiteSpace(reason) ? "." : $". Motivo: {reason}";
}

internal enum Severity
{
    Ok,
    Info,
    Warning,
    Error
}

internal sealed record AlertItem(Severity Severity, string Title, string Detail)
{
    public string Glyph => Severity switch
    {
        Severity.Ok => "",
        Severity.Info => "",
        Severity.Warning => "",
        _ => ""
    };
}

internal sealed record ActivityItem(string Time, string Text, Severity Severity)
{
    public string Glyph => Severity switch
    {
        Severity.Ok => "",
        Severity.Warning => "",
        Severity.Error => "",
        _ => ""
    };
}
