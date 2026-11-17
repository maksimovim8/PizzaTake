using SQLite;

namespace PizzaTake.Models;

[Table("UserProfiles")]
public class UserProfile
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public bool IsPhoneVerified { get; set; }
    public DateTime UpdatedAt { get; set; }
}
