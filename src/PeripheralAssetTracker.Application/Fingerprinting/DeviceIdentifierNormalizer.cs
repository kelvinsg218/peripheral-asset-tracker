using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeripheralAssetTracker.Application.Fingerprinting;

/// <summary>
/// Explicit normalization rules for hardware identifiers. Formatting differences that carry no
/// meaning (case, surrounding/duplicated spaces, "0x"/"VID_" prefixes, separators) must never
/// change a fingerprint. Every method returns null for empty / placeholder values.
/// </summary>
public static partial class DeviceIdentifierNormalizer
{
    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "-", "N/A", "NA", "NONE", "NULL", "UNKNOWN", "UNAVAILABLE", "DEFAULT", "NOT AVAILABLE",
        "TO BE FILLED BY O.E.M.", "DEFAULT STRING", "SERIAL", "SERIALNUMBER", "SERIAL NUMBER", "SN"
    };

    /// <summary>Generic driver-provider strings that do not identify the real manufacturer.</summary>
    private static readonly string[] GenericManufacturerFragments =
    [
        "standard", "padrão", "padrao", "(standard", "generic", "genérico", "generico", "system devices",
        "dispositivos do sistema", "hid-compliant", "compatível com hid"
    ];

    /// <summary>Trims, collapses internal whitespace and removes control chars; keeps case (for display).</summary>
    public static string? CleanText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        var collapsed = string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return PlaceholderValues.Contains(collapsed) ? null : collapsed;
    }

    /// <summary>Comparison form of free text: cleaned + upper-case invariant.</summary>
    public static string? NormalizeText(string? value) => CleanText(value)?.ToUpperInvariant();

    /// <summary>
    /// Normalizes a 16-bit USB/Bluetooth id: accepts "046d", "0x046D", "VID_046D", "PID&amp;C084", " 46d ".
    /// Returns 4 upper-case hex digits or null when invalid.
    /// </summary>
    public static string? NormalizeHexId(string? value)
    {
        var cleaned = CleanText(value)?.ToUpperInvariant().Replace(" ", string.Empty);
        if (cleaned is null)
        {
            return null;
        }

        cleaned = HexIdPrefix().Replace(cleaned, string.Empty);
        if (cleaned.Length is 0 or > 4 || !cleaned.All(Uri.IsHexDigit))
        {
            return null;
        }

        return cleaned.PadLeft(4, '0');
    }

    /// <summary>
    /// Serial numbers: whitespace removed, upper-case (Windows instance ids are case-insensitive and
    /// are reported upper-case), placeholders discarded.
    /// </summary>
    public static string? NormalizeSerial(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var compact = string.Concat(cleaned.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        return compact.Length == 0 || PlaceholderValues.Contains(compact) ? null : compact;
    }

    /// <summary>Bluetooth address as 12 upper-case hex digits (separators removed). Null if invalid or all zeros.</summary>
    public static string? NormalizeBluetoothAddress(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var hex = string.Concat(cleaned.Where(c => c is not (':' or '-' or '.' or ' '))).ToUpperInvariant();
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit) || hex.All(c => c == '0'))
        {
            return null;
        }

        return hex;
    }

    /// <summary>
    /// PnP ids (instance ids, hardware ids): trimmed, upper-case, '/' converted to '\', no spaces around separators.
    /// </summary>
    public static string? NormalizePnpId(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var upper = cleaned.ToUpperInvariant().Replace('/', '\\');
        return SpacesAroundSeparators().Replace(upper, "$1");
    }

    /// <summary>Normalized, de-duplicated hardware ids keeping the original (most specific first) order.</summary>
    public static IReadOnlyList<string> NormalizeHardwareIds(IEnumerable<string?>? hardwareIds)
    {
        if (hardwareIds is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in hardwareIds)
        {
            var normalized = NormalizePnpId(id);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>Removes the firmware revision component (REV_xxxx) from a hardware id.</summary>
    public static string StripRevision(string hardwareId) => RevisionComponent().Replace(hardwareId, string.Empty);

    /// <summary>
    /// Manufacturer: cleaned text, or null when it is a generic driver-provider label
    /// (e.g. "(Standard system devices)", "(Dispositivos padrão do sistema)").
    /// </summary>
    public static string? NormalizeManufacturer(string? value)
    {
        var cleaned = CleanText(value);
        if (cleaned is null)
        {
            return null;
        }

        var lower = cleaned.ToLower(CultureInfo.InvariantCulture);
        if (lower.StartsWith('(') || GenericManufacturerFragments.Any(lower.Contains))
        {
            return null;
        }

        return cleaned;
    }

    [GeneratedRegex(@"^(?:0X|VID[_&]?|PID[_&]?)", RegexOptions.CultureInvariant)]
    private static partial Regex HexIdPrefix();

    [GeneratedRegex(@"\s*([\\&_])\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesAroundSeparators();

    [GeneratedRegex(@"&REV_[0-9A-F]{1,4}(?=&|$|\\)", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionComponent();
}
