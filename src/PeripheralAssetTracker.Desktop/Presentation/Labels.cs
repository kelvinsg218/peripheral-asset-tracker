using System.Globalization;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Desktop.Presentation;

/// <summary>Every user-facing text derived from domain values (pt-BR). Internal names stay in English.</summary>
internal static class Labels
{
    public const string Missing = "—";
    public const string UnknownDevice = "Dispositivo desconhecido";

    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Type(DeviceType type) => type switch
    {
        DeviceType.Mouse => "Mouse",
        DeviceType.Keyboard => "Teclado",
        _ => "Outro"
    };

    public static string Bus(BusType bus) => bus switch
    {
        BusType.Usb => "USB",
        BusType.Bluetooth => "Bluetooth",
        BusType.BluetoothLowEnergy => "Bluetooth LE",
        BusType.Internal => "Integrado (ACPI/I2C/PS2)",
        _ => "Desconhecido"
    };

    public static string Confidence(FingerprintConfidence confidence) => confidence switch
    {
        FingerprintConfidence.High => "Alta",
        FingerprintConfidence.Medium => "Média",
        _ => "Baixa"
    };

    public static string Presence(DevicePresence presence) => presence switch
    {
        DevicePresence.Connected => "Conectado",
        DevicePresence.Disconnected => "Desconectado",
        _ => "Desconhecido"
    };

    public static string PresenceHint(DevicePresence presence, bool scanned) => presence switch
    {
        DevicePresence.Connected => "Detectado na última verificação deste computador.",
        DevicePresence.Disconnected => "Pertence a este computador, mas não foi detectado na última verificação.",
        _ when !scanned => "Este computador ainda não foi verificado nesta sessão.",
        _ => "Associado a outro computador: este computador não consegue confirmar se está conectado."
    };

    public static string Condition(DeviceConditions condition) => condition switch
    {
        DeviceConditions.NotRegistered => "Não cadastrado",
        DeviceConditions.Unassigned => "Sem responsável",
        DeviceConditions.UnexpectedComputer => "Computador inesperado",
        DeviceConditions.LowConfidence => "Baixa confiança",
        DeviceConditions.AmbiguousFingerprint => "Fingerprint ambíguo",
        _ => condition.ToString()
    };

    public static IReadOnlyList<string> Conditions(DeviceConditions conditions) =>
        Enum.GetValues<DeviceConditions>()
            .Where(c => c != DeviceConditions.None && conditions.HasFlag(c))
            .Select(Condition)
            .ToList();

    public static string ConditionsText(DeviceConditions conditions)
    {
        var list = Conditions(conditions);
        return list.Count == 0 ? "Sem alertas" : string.Join(" · ", list);
    }

    public static string HistoryKind(HistoryEntryKind kind) => kind switch
    {
        HistoryEntryKind.Registered => "Cadastro",
        HistoryEntryKind.Assigned => "Atribuição",
        HistoryEntryKind.Transferred => "Transferência",
        _ => "Detecção"
    };

    public static string Movement(MovementType type) => type == MovementType.Transfer ? "Transferência" : "Atribuição";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value.Trim();

    public static string DeviceName(string? name) => string.IsNullOrWhiteSpace(name) ? UnknownDevice : name.Trim();

    public static string Model(string? vendorId, string? productId) =>
        vendorId is null && productId is null ? Missing : $"{vendorId ?? "?"}:{productId ?? "?"}";

    /// <summary>UTC from the database → local "dd/MM/yyyy HH:mm".</summary>
    public static string Date(DateTime? utc) =>
        utc is null ? Missing : ToLocal(utc.Value).ToString("dd/MM/yyyy HH:mm", Culture);

    public static string Time(DateTime utc) => ToLocal(utc).ToString("HH:mm:ss", Culture);

    /// <summary>"agora", "há 5 min", "há 3 h", "ontem", "há 4 dias" or the date for older values.</summary>
    public static string Relative(DateTime utc, DateTime nowUtc)
    {
        var elapsed = nowUtc - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return elapsed.TotalSeconds switch
        {
            < 60 => "agora",
            < 3600 => $"há {(int)elapsed.TotalMinutes} min",
            < 86400 => $"há {(int)elapsed.TotalHours} h",
            < 172800 => "ontem",
            < 604800 => $"há {(int)elapsed.TotalDays} dias",
            _ => Date(utc)
        };
    }

    public static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
