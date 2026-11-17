using SQLite;

namespace PizzaTake.Models;

[Table("Pizzerias")]
public class Pizzeria
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int CityId { get; set; }
    public string StoreNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public TimeSpan OpenFrom { get; set; }
    public TimeSpan OpenTo { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public bool IsOpenNow(DateTime now)
    {
        var time = now.TimeOfDay;
        return OpenFrom <= OpenTo
            ? time >= OpenFrom && time < OpenTo
            : time >= OpenFrom || time < OpenTo;
    }
}
