using System.Globalization;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Desktop.Presentation;
using PeripheralAssetTracker.Domain.Enums;
using static PeripheralAssetTracker.Tests.Application.InventoryOverviewTests;

namespace PeripheralAssetTracker.Tests.Desktop;

public class PresentationTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(DeviceType.Mouse, "Mouse")]
    [InlineData(DeviceType.Keyboard, "Teclado")]
    [InlineData(DeviceType.Other, "Outro")]
    public void Device_types_are_shown_in_portuguese(DeviceType type, string expected) =>
        Assert.Equal(expected, Labels.Type(type));

    [Fact]
    public void Presence_confidence_and_conditions_are_shown_in_portuguese()
    {
        Assert.Equal("Conectado", Labels.Presence(DevicePresence.Connected));
        Assert.Equal("Desconectado", Labels.Presence(DevicePresence.Disconnected));
        Assert.Equal("Desconhecido", Labels.Presence(DevicePresence.Unknown));
        Assert.Equal("Alta", Labels.Confidence(FingerprintConfidence.High));
        Assert.Equal("Baixa", Labels.Confidence(FingerprintConfidence.Low));
        Assert.Equal("Sem alertas", Labels.ConditionsText(DeviceConditions.None));
        Assert.Equal("Sem responsável · Baixa confiança",
            Labels.ConditionsText(DeviceConditions.Unassigned | DeviceConditions.LowConfidence));
    }

    [Fact]
    public void Every_condition_flag_has_a_portuguese_label()
    {
        foreach (var flag in Enum.GetValues<DeviceConditions>().Where(f => f != DeviceConditions.None))
        {
            Assert.NotEqual(flag.ToString(), Labels.Condition(flag));
        }
    }

    [Fact]
    public void Missing_values_and_models_have_placeholders()
    {
        Assert.Equal("—", Labels.Text("  "));
        Assert.Equal("Dispositivo desconhecido", Labels.DeviceName(null));
        Assert.Equal("046D:C084", Labels.Model("046D", "C084"));
        Assert.Equal("—", Labels.Model(null, null));
        Assert.Equal("—", Labels.Date(null));
    }

    [Fact]
    public void Relative_time_is_human_readable()
    {
        Assert.Equal("agora", Labels.Relative(Now.AddSeconds(-20), Now));
        Assert.Equal("há 5 min", Labels.Relative(Now.AddMinutes(-5), Now));
        Assert.Equal("há 3 h", Labels.Relative(Now.AddHours(-3), Now));
        Assert.Equal("ontem", Labels.Relative(Now.AddHours(-30), Now));
        Assert.Equal("há 4 dias", Labels.Relative(Now.AddDays(-4), Now));
    }

    [Fact]
    public void Filter_combines_search_category_and_status()
    {
        var mouse = Row(Summary(1, expected: 1, type: DeviceType.Mouse) with { Manufacturer = "Logitech" }, DevicePresence.Connected);
        var keyboard = Row(Summary(2, expected: 1, type: DeviceType.Keyboard, conditions: DeviceConditions.Unassigned),
            DevicePresence.Disconnected);

        Assert.True(InventoryFilter.Matches(mouse, null, null, StatusFilter.All));
        Assert.True(InventoryFilter.Matches(mouse, "logi", null, StatusFilter.All));
        Assert.True(InventoryFilter.Matches(keyboard, "teclado", null, StatusFilter.All));
        Assert.False(InventoryFilter.Matches(mouse, "teclado", null, StatusFilter.All));
        Assert.False(InventoryFilter.Matches(mouse, null, DeviceType.Keyboard, StatusFilter.All));
        Assert.True(InventoryFilter.Matches(keyboard, null, DeviceType.Keyboard, StatusFilter.Disconnected));
        Assert.False(InventoryFilter.Matches(mouse, null, null, StatusFilter.Disconnected));
        Assert.True(InventoryFilter.Matches(keyboard, null, null, StatusFilter.Unassigned));
        Assert.True(InventoryFilter.Matches(keyboard, null, null, StatusFilter.WithAlerts));
        Assert.False(InventoryFilter.Matches(mouse, null, null, StatusFilter.WithAlerts));
    }

    [Fact]
    public void Csv_report_has_portuguese_headers_and_values()
    {
        var row = Row(Summary(1, expected: 1, conditions: DeviceConditions.Unassigned) with
        {
            Manufacturer = "Logitech",
            VendorId = "046D",
            ProductId = "C084",
            SerialNumber = "ABC123",
            BusType = BusType.Usb,
            ExpectedComputerHostname = "PC-01"
        }, DevicePresence.Connected);

        var lines = CsvReport.Build([row], ';').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(string.Join(';', CsvReport.Headers), lines[0]);
        Assert.StartsWith("Device 1;Mouse;Conectado;Logitech;046D;C084;ABC123;;USB;;;PC-01;", lines[1]);
        Assert.EndsWith(";Alta;Sem responsável;FP1", lines[1]);
    }

    [Fact]
    public void Csv_report_neutralizes_formulas()
    {
        var row = Row(Summary(1, expected: 1) with { DeviceName = "=HYPERLINK(\"x\")" }, DevicePresence.Unknown);

        var data = CsvReport.Build([row], ',').Split("\r\n")[1];

        Assert.StartsWith("\"'=HYPERLINK(\"\"x\"\")\"", data);
    }

    [Fact]
    public void Csv_delimiter_follows_the_regional_list_separator()
    {
        Assert.Equal(';', CsvReport.DelimiterFor(CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal(',', CsvReport.DelimiterFor(CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Alerts_explain_missing_scan_and_disconnected_devices()
    {
        var disconnected = Row(Summary(1, expected: 1), DevicePresence.Disconnected);
        var overview = InventoryOverviewCalculator.Calculate([disconnected.Summary], null, 1, Now);

        var noScan = AlertBuilder.Build(overview, [disconnected], null);
        var afterScan = AlertBuilder.Build(overview with { Disconnected = 1 }, [disconnected], Scan(1));

        Assert.Contains(noScan, a => a.Title == "Este computador ainda não foi verificado");
        Assert.Contains(afterScan, a => a.Severity == Severity.Warning && a.Title == "Device 1 não foi detectado");
    }

    [Fact]
    public void Alerts_are_ok_when_nothing_needs_attention()
    {
        var alert = Assert.Single(AlertBuilder.Build(InventoryOverviewCalculator.Calculate([], Scan(1), 1, Now), [], Scan(1)));

        Assert.Equal(Severity.Ok, alert.Severity);
        Assert.Equal("Nenhum alerta", alert.Title);
    }

    [Fact]
    public void Alerts_are_sorted_by_severity()
    {
        var unexpected = Row(Summary(1, expected: 2, conditions: DeviceConditions.UnexpectedComputer | DeviceConditions.Unassigned),
            DevicePresence.Connected);
        var overview = InventoryOverviewCalculator.Calculate([unexpected.Summary], Scan(1), 1, Now);

        var alerts = AlertBuilder.Build(overview, [unexpected], Scan(1));

        Assert.Equal(Severity.Error, alerts[0].Severity);
        Assert.Equal(alerts.OrderByDescending(a => a.Severity).Select(a => a.Title), alerts.Select(a => a.Title));
    }

    [Fact]
    public void History_and_movements_are_described_in_portuguese()
    {
        var transfer = HistoryRow.Create(new HistoryEntry(Now, HistoryEntryKind.Transferred, "Ana", "PC-01", "Bruno", null, "Troca de setor"));
        var observed = HistoryRow.Create(new HistoryEntry(Now, HistoryEntryKind.Observed, null, null, null, "PC-02",
            null, Now.AddHours(1), ScanCount: 3, UnexpectedComputer: true));

        Assert.Equal("Transferência", transfer.Kind);
        Assert.Equal("De responsável Ana, computador PC-01 para responsável Bruno, computador não definido. Motivo: Troca de setor",
            transfer.Description);
        Assert.True(observed.IsWarning);
        Assert.StartsWith("Detectado em PC-02 (confirmado em 3 verificações", observed.Description);
        Assert.Equal("Ana · PC-01", MovementRow.Party("Ana", "PC-01"));
        Assert.Equal("PC-01", MovementRow.Party(null, "PC-01"));
        Assert.Equal("—", MovementRow.Party(null, null));
    }

    private static DeviceRow Row(DeviceSummary summary, DevicePresence presence) => new(summary, presence, scanned: true);
}
