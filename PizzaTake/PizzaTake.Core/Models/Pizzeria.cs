using System.ComponentModel;
using SQLite;

namespace PizzaTake.Core.Models;

[Table("Pizzerias")]
public class Pizzeria : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int CityId { get; set; }
    public string StoreNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public TimeSpan OpenFrom { get; set; }
    public TimeSpan OpenTo { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [Ignore]
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectButtonText)));
        }
    }

    [Ignore]
    public string SelectButtonText => IsSelected ? "Обрано ✓" : "Обрати";

    [Ignore]
    public string WorkingHoursText => $"{OpenFrom:hh\\:mm} – {OpenTo:hh\\:mm}";

    public bool IsOpenNow(DateTime now)
    {
        var time = now.TimeOfDay;
        return OpenFrom <= OpenTo
            ? time >= OpenFrom && time < OpenTo
            : time >= OpenFrom || time < OpenTo;
    }
}
