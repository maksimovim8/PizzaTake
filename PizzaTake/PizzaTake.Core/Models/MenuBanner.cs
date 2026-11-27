using SQLite;

namespace PizzaTake.Core.Models;

/// <summary>
/// Promotional banner displayed in the horizontal carousel on MenuPage and configurable by administrators.
/// </summary>
[Table("MenuBanners")]
public class MenuBanner
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Eyebrow { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = "#2460A0";
    public string TitleColorHex { get; set; } = "#FFD166";
    public string BadgeColorHex { get; set; } = "#F4511E";
    public string BadgeTextColorHex { get; set; } = "#FFFFFF";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    [Ignore]
    public string StatusBadgeText => IsActive ? "Активний" : "Прихований";
}
