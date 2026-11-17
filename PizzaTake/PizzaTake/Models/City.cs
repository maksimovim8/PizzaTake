using SQLite;

namespace PizzaTake.Models;

[Table("Cities")]
public class City
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool IsPopular { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
