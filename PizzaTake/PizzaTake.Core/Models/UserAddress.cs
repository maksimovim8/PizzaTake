using SQLite;

namespace PizzaTake.Core.Models;

[Table("UserAddresses")]
public class UserAddress
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime LastUsedAt { get; set; }
}
