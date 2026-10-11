using System.Globalization;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Desktop.Presentation;

internal enum StatusFilter
{
    All,
    Connected,
    Disconnected,
    Unknown,
    WithAlerts,
    Unassigned,
    UnexpectedComputer,
    LowConfidence
}

/// <summary>Combo box option: a value plus its pt-BR label.</summary>
internal sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal static class InventoryFilter
{
    public static IReadOnlyList<Option<DeviceType?>> Categories { get; } =
    [
        new(null, "Todas as categorias"),
        new(DeviceType.Mouse, "Mouse"),
        new(DeviceType.Keyboard, "Teclado"),
        new(DeviceType.Other, "Outro")
    ];

    public static IReadOnlyList<Option<StatusFilter>> Statuses { get; } =
    [
        new(StatusFilter.All, "Todos os status"),
        new(StatusFilter.Connected, "Conectados"),
        new(StatusFilter.Disconnected, "Desconectados"),
        new(StatusFilter.Unknown, "Estado desconhecido"),
        new(StatusFilter.WithAlerts, "Com alertas"),
        new(StatusFilter.Unassigned, "Sem responsável"),
        new(StatusFilter.UnexpectedComputer, "Computador inesperado"),
        new(StatusFilter.LowConfidence, "Baixa confiança")
    ];

    public static bool Matches(DeviceRow row, string? search, DeviceType? type, StatusFilter status)
    {
        if (type is not null && row.Summary.DeviceType != type)
        {
            return false;
        }

        var conditions = row.Summary.Conditions;
        var statusMatches = status switch
        {
            StatusFilter.Connected => row.Presence == DevicePresence.Connected,
            StatusFilter.Disconnected => row.Presence == DevicePresence.Disconnected,
            StatusFilter.Unknown => row.Presence == DevicePresence.Unknown,
            StatusFilter.WithAlerts => conditions != DeviceConditions.None,
            StatusFilter.Unassigned => conditions.HasFlag(DeviceConditions.Unassigned),
            StatusFilter.UnexpectedComputer => conditions.HasFlag(DeviceConditions.UnexpectedComputer),
            StatusFilter.LowConfidence => conditions.HasFlag(DeviceConditions.LowConfidence),
            _ => true
        };

        return statusMatches &&
               (string.IsNullOrWhiteSpace(search) ||
                InventoryQueryService.Matches(row.Summary, search.Trim()) ||
                TextSearch.Contains(row.TypeLabel, search.Trim()));
    }
}

/// <summary>Inventory report for spreadsheets, with pt-BR headers and values.</summary>
internal static class CsvReport
{
    public static IReadOnlyList<string> Headers { get; } =
    [
        "Nome", "Categoria", "Estado", "Fabricante", "VID", "PID", "Número de série", "Endereço Bluetooth",
        "Conexão", "Responsável", "Departamento", "Computador esperado", "Último computador",
        "Primeira detecção", "Última detecção", "Confiança", "Alertas", "Fingerprint"
    ];

    public static string Build(IEnumerable<DeviceRow> rows, char delimiter) =>
        InventoryCsvFormatter.FormatTable(Headers, rows.Select(r => new[]
        {
            r.Name, r.TypeLabel, r.PresenceLabel, r.Summary.Manufacturer, r.Summary.VendorId, r.Summary.ProductId,
            r.Summary.SerialNumber, r.Summary.BluetoothAddress, r.Bus, r.Summary.ResponsiblePersonName,
            r.Summary.ResponsibleDepartment, r.Summary.ExpectedComputerHostname, r.Summary.LastSeenComputerHostname,
            r.FirstSeen, r.LastSeen, r.ConfidenceLabel, r.HasAlerts ? r.AlertsText : string.Empty, r.Fingerprint
        }), delimiter);

    /// <summary>Excel splits columns with the Windows list separator (';' in pt-BR, ',' in en-US).</summary>
    public static char DelimiterFor(CultureInfo culture)
    {
        var separator = culture.TextInfo.ListSeparator;
        return separator.Length == 1 && separator[0] is not ('"' or '\r' or '\n') ? separator[0] : ';';
    }
}

internal static class AlertBuilder
{
    private const int MaxPerDeviceAlerts = 5;

    public static IReadOnlyList<AlertItem> Build(
        InventoryOverview overview, IReadOnlyList<DeviceRow> rows, ScanReport? scan)
    {
        var alerts = new List<AlertItem>();
        if (scan is null)
        {
            alerts.Add(new(Severity.Info, "Este computador ainda não foi verificado",
                "Use \"Verificar dispositivos\" para confirmar quais periféricos estão conectados."));
        }

        foreach (var row in rows.Where(r => r.Summary.Conditions.HasFlag(DeviceConditions.UnexpectedComputer)).Take(MaxPerDeviceAlerts))
        {
            alerts.Add(new(Severity.Error, $"{row.Name} está em um computador inesperado",
                $"Última detecção em {row.LastSeenComputer}; computador esperado: {row.ExpectedComputer}."));
        }

        foreach (var row in rows.Where(r => r.Presence == DevicePresence.Disconnected).Take(MaxPerDeviceAlerts))
        {
            alerts.Add(new(Severity.Warning, $"{row.Name} não foi detectado",
                $"Pertence a este computador, mas não estava conectado na última verificação. Última detecção: {row.LastSeen}."));
        }

        if (overview.NewDetected > 0)
        {
            alerts.Add(new(Severity.Warning,
                $"{Labels.Count(overview.NewDetected, "periférico conectado não está cadastrado", "periféricos conectados não estão cadastrados")}",
                "Cadastre-os em \"Verificar dispositivos\" para acompanhá-los no inventário."));
        }

        if (overview.AmbiguousFingerprint > 0)
        {
            alerts.Add(new(Severity.Warning, "Unidades idênticas conectadas",
                "Mais de uma unidade gerou o mesmo fingerprint. O Windows não fornece dados para diferenciá-las."));
        }

        foreach (var warning in scan?.Discovery.Warnings ?? [])
        {
            alerts.Add(new(Severity.Warning, "Aviso da leitura de dispositivos", warning));
        }

        if (overview.Unassigned > 0)
        {
            alerts.Add(new(Severity.Info, Labels.Count(overview.Unassigned, "dispositivo sem responsável", "dispositivos sem responsável"),
                "Atribua um responsável no Inventário de dispositivos."));
        }

        if (overview.LowConfidence > 0)
        {
            alerts.Add(new(Severity.Info,
                Labels.Count(overview.LowConfidence, "dispositivo com baixa confiança", "dispositivos com baixa confiança"),
                "A identificação usa apenas dados comuns a todas as unidades do mesmo modelo (ex.: VID/PID)."));
        }

        if (alerts.Count == 0)
        {
            alerts.Add(new(Severity.Ok, "Nenhum alerta", "O inventário está consistente com a última verificação."));
        }

        return alerts.OrderByDescending(a => a.Severity).ToList();
    }
}
