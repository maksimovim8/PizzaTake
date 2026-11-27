using System.Globalization;
using SQLite;

namespace PizzaTake.Core.Models;

/// <summary>
/// Customer quality report with optional photo attachment path and GPS coordinates.
/// </summary>
[Table("QualityReports")]
public class QualityReport
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Ignore]
    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);

    [Ignore]
    public bool HasCoordinates =>
        Latitude.HasValue && Longitude.HasValue &&
        (Math.Abs(Latitude.Value) > 0.00001 || Math.Abs(Longitude.Value) > 0.00001);

    [Ignore]
    public string CoordinatesText => HasCoordinates
        ? string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", Latitude!.Value, Longitude!.Value)
        : "Координати не вказано";

    [Ignore]
    public string OrderBadgeText => !string.IsNullOrWhiteSpace(OrderNumber)
        ? $"Замовлення №{OrderNumber}"
        : "Загальне звернення";

    [Ignore]
    public string CreatedAtLocalText => CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
