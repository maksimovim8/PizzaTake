using System.Globalization;

namespace PizzaTake.Converters;

/// <summary>
/// Converts the currently selected menu category into chip background or text colors based on the active app theme.
/// </summary>
public sealed class CategoryChipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selectedCategory = value as string;
        var param = parameter as string ?? string.Empty;
        var parts = param.Split('|', 2, StringSplitOptions.TrimEntries);
        var chipCategory = parts[0];
        var mode = parts.Length > 1 ? parts[1] : "Background";

        var isSelected = string.Equals(selectedCategory, chipCategory, StringComparison.OrdinalIgnoreCase);
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        if (string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return isSelected
                ? Color.FromArgb("#F4511E")
                : Color.FromArgb(isDark ? "#D6D3D1" : "#777777");
        }

        return isSelected
            ? Color.FromArgb(isDark ? "#3A2D1E" : "#FFF1D3")
            : Color.FromArgb(isDark ? "#26201B" : "#FFFFFF");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
