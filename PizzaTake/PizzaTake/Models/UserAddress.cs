using SQLite;

namespace PizzaTake.Models;

[Table("UserAddresses")]
public class UserAddress
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime LastUsedAt { get; set; }
}
