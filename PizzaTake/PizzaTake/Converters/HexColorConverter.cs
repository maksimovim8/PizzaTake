using System.Globalization;

namespace PizzaTake.Converters;

/// <summary>
/// Safely parses a hex color string (e.g. "#2460A0") into a MAUI Color with a fallback value.
/// </summary>
public sealed class HexColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var parsed))
            return parsed;

        var fallback = parameter as string;
        if (!string.IsNullOrWhiteSpace(fallback) && Color.TryParse(fallback.Trim(), out var fallbackColor))
            return fallbackColor;

        return Color.FromArgb("#2460A0");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
