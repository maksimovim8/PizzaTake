using System.Globalization;

namespace PizzaTake.Converters;

/// <summary>
/// Converts the active log level filter into chip background, text, or border color based on theme.
/// Avoids MAUI AppThemeBinding + DataTrigger override conflicts on Android buttons.
/// </summary>
public sealed class LogFilterChipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selectedLevel = value as string;
        var param = parameter as string ?? string.Empty;
        var parts = param.Split('|', 2, StringSplitOptions.TrimEntries);
        var chipLevel = parts[0];
        var mode = parts.Length > 1 ? parts[1] : "Background";

        var isSelected = string.Equals(selectedLevel, chipLevel, StringComparison.OrdinalIgnoreCase);
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        if (string.Equals(mode, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return isSelected
                ? Color.FromArgb("#FFFFFF")
                : Color.FromArgb(isDark ? "#D6D3D1" : "#57534E");
        }

        if (string.Equals(mode, "Border", StringComparison.OrdinalIgnoreCase))
        {
            return isSelected
                ? GetActiveColor(chipLevel)
                : Color.FromArgb(isDark ? "#3D342C" : "#EAE5DC");
        }

        return isSelected
            ? GetActiveColor(chipLevel)
            : Color.FromArgb(isDark ? "#26201B" : "#FFFFFF");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Color GetActiveColor(string level) => level switch
    {
        "Information" => Color.FromArgb("#2563EB"),
        "Warning" => Color.FromArgb("#D97706"),
        "Error" => Color.FromArgb("#DC2626"),
        "Fatal" => Color.FromArgb("#7F1D1D"),
        _ => Color.FromArgb("#EB5E28")
    };
}
