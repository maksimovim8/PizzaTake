using System.Globalization;
using PizzaTake.Core.Models;

namespace PizzaTake.Converters;

/// <summary>
/// Converts an OrderStatus enum value to localized status text, progress fraction, or indicator color.
/// </summary>
public sealed class OrderProgressConverter : IValueConverter
{
    private static readonly Color ActiveOrange = Color.FromArgb("#EB5E28");
    private static readonly Color ActiveAmber = Color.FromArgb("#F7A600");
    private static readonly Color SuccessGreen = Color.FromArgb("#16A34A");
    private static readonly Color DangerRed = Color.FromArgb("#DC2626");
    private static readonly Color MutedGray = Color.FromArgb("#78716C");
    private static readonly Color TrackGray = Color.FromArgb("#E7E5E4");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OrderStatus status)
            return parameter?.ToString() == "Progress" ? 0d : string.Empty;

        var mode = parameter?.ToString();
        return mode switch
        {
            "Color" => GetStatusColor(status),
            "Progress" => GetProgressFraction(status),
            "Step1Color" => status switch
            {
                OrderStatus.Cancelled => DangerRed,
                OrderStatus.Pending => TrackGray,
                _ => ActiveOrange
            },
            "Step2Color" => status is OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Delivering or OrderStatus.Completed
                ? ActiveAmber
                : TrackGray,
            "Step3Color" => status is OrderStatus.Ready or OrderStatus.Delivering or OrderStatus.Completed
                ? SuccessGreen
                : TrackGray,
            _ => GetStatusText(status)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static string GetStatusText(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Очікує підтвердження",
        OrderStatus.Accepted => "Прийняли",
        OrderStatus.Preparing => "Готуємо ваше замовлення",
        OrderStatus.Ready => "Очікує у піцерії",
        OrderStatus.Delivering => "В дорозі",
        OrderStatus.Completed => "Виконано",
        OrderStatus.Cancelled => "Скасовано",
        _ => status.ToString()
    };

    private static Color GetStatusColor(OrderStatus status) => status switch
    {
        OrderStatus.Pending => MutedGray,
        OrderStatus.Accepted or OrderStatus.Delivering => ActiveOrange,
        OrderStatus.Preparing => ActiveAmber,
        OrderStatus.Ready or OrderStatus.Completed => SuccessGreen,
        OrderStatus.Cancelled => DangerRed,
        _ => MutedGray
    };

    private static double GetProgressFraction(OrderStatus status) => status switch
    {
        OrderStatus.Pending => 0.15,
        OrderStatus.Accepted => 0.35,
        OrderStatus.Preparing => 0.65,
        OrderStatus.Delivering => 0.85,
        OrderStatus.Ready or OrderStatus.Completed => 1.0,
        OrderStatus.Cancelled => 0.0,
        _ => 0.0
    };
}
