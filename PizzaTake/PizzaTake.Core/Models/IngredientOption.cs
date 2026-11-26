using System.ComponentModel;
using SQLite;

namespace PizzaTake.Core.Models;

[Table("IngredientOptions")]
public class IngredientOption : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public OptionKind Kind { get; set; }

    [Ignore]
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionSymbol)));
        }
    }

    [Ignore]
    public string ActionSymbol => IsSelected
        ? "✓"
        : Kind switch
        {
            OptionKind.AddOn => "+",
            OptionKind.Exclusion => "−",
            _ => "↔"
        };
}
