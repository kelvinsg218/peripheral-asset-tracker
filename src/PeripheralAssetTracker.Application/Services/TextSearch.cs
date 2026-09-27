using System.Globalization;

namespace PeripheralAssetTracker.Application.Services;

/// <summary>Friendly text matching: case-insensitive and accent-insensitive ("joao" finds "João").</summary>
public static class TextSearch
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private const CompareOptions Options =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    public static bool Contains(string? source, string? term)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        return Compare.IndexOf(source, term.Trim(), Options) >= 0;
    }
}
