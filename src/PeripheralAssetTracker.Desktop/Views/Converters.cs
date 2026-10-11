using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using PeripheralAssetTracker.Application.Services;
using PeripheralAssetTracker.Desktop.Presentation;

namespace PeripheralAssetTracker.Desktop.Views;

/// <summary>Visible when the value is true (or false with ConverterParameter=Invert).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ (parameter as string == "Invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Enum value equals ConverterParameter (by name). Two-way for radio buttons.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equals = value?.ToString() == parameter as string;
        return targetType == typeof(Visibility) ? equals ? Visibility.Visible : Visibility.Collapsed : equals;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>Severity → accent brush, or its soft background with ConverterParameter=Soft.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter as string == "Soft";
        var key = value switch
        {
            Severity.Ok => soft ? "Brush.GreenSoft" : "Brush.Green",
            Severity.Warning => soft ? "Brush.YellowSoft" : "Brush.Yellow",
            Severity.Error => soft ? "Brush.RedSoft" : "Brush.Red",
            _ => soft ? "Brush.NeutralSoft" : "Brush.Neutral"
        };
        return System.Windows.Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Presence → green (connected), yellow (disconnected), grey (unknown). ConverterParameter=Soft for backgrounds.</summary>
public sealed class PresenceBrushConverter : IValueConverter
{
    private static readonly SeverityBrushConverter Inner = new();

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        Inner.Convert(value switch
        {
            DevicePresence.Connected => Severity.Ok,
            DevicePresence.Disconnected => Severity.Warning,
            _ => Severity.Info
        }, targetType, parameter, culture);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when an integer is zero (ConverterParameter=Zero) or greater than zero (default).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is not int count || count == 0;
        return isZero == (parameter as string == "Zero") ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int CaptionColor = 35;

    /// <summary>Dark native title bar (Windows 10 20H1+ / 11). Ignored silently where unsupported.</summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        // COLORREF is 0x00BBGGRR: matches the window background #0B0D10 (Windows 11 only).
        var background = (Color)window.FindResource("WindowColor");
        var color = background.B << 16 | background.G << 8 | background.R;
        DwmSetWindowAttribute(hwnd, CaptionColor, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

public sealed class SeverityGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        new AlertItem(value is Severity severity ? severity : Severity.Info, string.Empty, string.Empty).Glyph;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
