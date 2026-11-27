using System.Globalization;

namespace PizzaTake.Converters;

/// <summary>
/// Converts either a selected category string or a chip's IsSelected boolean into theme-aware chip colors.
/// </summary>
public sealed class CategoryChipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var param = parameter as string ?? string.Empty;
        bool isSelected;
        string mode;

        if (value is bool selectedFlag)
        {
            isSelected = selectedFlag;
            mode = string.IsNullOrWhiteSpace(param) ? "Background" : param.Trim();
        }
        else
        {
            var selectedCategory = value as string;
            var parts = param.Split('|', 2, StringSplitOptions.TrimEntries);
            var chipCategory = parts[0];
            mode = parts.Length > 1 ? parts[1] : "Background";
            isSelected = string.Equals(selectedCategory, chipCategory, StringComparison.OrdinalIgnoreCase);
        }

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
