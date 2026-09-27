using System.Globalization;

namespace PeripheralAssetTracker.Cli;

internal static class ExportDelimiter
{
    /// <summary>
    /// Excel splits CSV columns using the Windows list separator (';' for pt-BR, ',' for en-US).
    /// Using it by default makes the file open correctly in the local Excel.
    /// </summary>
    public static char Default
    {
        get
        {
            var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            return separator.Length == 1 && separator[0] is not ('"' or '\r' or '\n') ? separator[0] : ';';
        }
    }

    public static char Parse(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" => Default,
        ";" or "semicolon" => ';',
        "," or "comma" => ',',
        "tab" or "\\t" => '\t',
        _ => throw new ArgumentException("Delimitador inválido. Use ';', ',' ou 'tab'.")
    };
}
