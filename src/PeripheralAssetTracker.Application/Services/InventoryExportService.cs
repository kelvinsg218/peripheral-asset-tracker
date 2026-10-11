using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PeripheralAssetTracker.Application.Abstractions;
using PeripheralAssetTracker.Application.Models;
using PeripheralAssetTracker.Domain.Enums;

namespace PeripheralAssetTracker.Application.Services;

public sealed class InventoryExportService(
    InventoryQueryService inventory,
    IReportFileWriter writer,
    TimeProvider clock,
    ILogger<InventoryExportService> logger)
{
    /// <summary>Exports all registered devices to reports/inventory_YYYY-MM-DD_HHMMSS.csv.</summary>
    public async Task<string> ExportAsync(char delimiter, CancellationToken ct = default)
    {
        var devices = await inventory.ListAsync(DeviceFilter.All, ct);
        var localNow = clock.GetLocalNow();
        var fileName = $"inventory_{localNow:yyyy-MM-dd_HHmmss}.csv";

        var csv = InventoryCsvFormatter.Format(devices, delimiter, clock.LocalTimeZone);
        var path = await writer.WriteAsync(fileName, csv, ct);
        logger.LogInformation("Inventory report generated with {Count} device(s): {FileName}", devices.Count, fileName);
        return path;
    }
}

/// <summary>
/// CSV (RFC 4180 quoting) for the inventory. The delimiter is configurable because Excel uses the
/// Windows list separator (';' in pt-BR, ',' in en-US). The file writer adds a UTF-8 BOM so Excel
/// detects the encoding and accented characters are displayed correctly.
/// </summary>
public static class InventoryCsvFormatter
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "Fingerprint", "Confidence", "Type", "Device", "Manufacturer", "VID", "PID", "Serial",
        "Responsible", "ExpectedComputer", "LastObservedComputer", "LastSeen", "Status"
    ];

    public static string Format(IEnumerable<DeviceSummary> devices, char delimiter, TimeZoneInfo timeZone)
    {
        return FormatTable(Headers, devices.Select(d => new[]
            {
                d.Fingerprint,
                d.Confidence.ToString().ToUpperInvariant(),
                d.DeviceType.ToString(),
                d.DeviceName,
                d.Manufacturer,
                d.VendorId,
                d.ProductId,
                d.SerialNumber,
                d.ResponsiblePersonName,
                d.ExpectedComputerHostname,
                d.LastSeenComputerHostname,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d.LastSeenAt, DateTimeKind.Utc), timeZone)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                string.Join('|', d.Conditions.ToCodes())
            }), delimiter);
    }

    /// <summary>Any table with the same quoting and formula-injection rules (used by the localized desktop report).</summary>
    public static string FormatTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, char delimiter)
    {
        if (delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentException("Invalid CSV delimiter.", nameof(delimiter));
        }

        var builder = new StringBuilder();
        AppendRow(builder, headers, delimiter);
        foreach (var row in rows)
        {
            AppendRow(builder, row, delimiter);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> values, char delimiter)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(delimiter);
            }

            builder.Append(Escape(values[i], delimiter));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string? value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Neutralize spreadsheet formula injection for values that start with a formula trigger.
        if (value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        var mustQuote = value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 || value != value.Trim();
        return mustQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
