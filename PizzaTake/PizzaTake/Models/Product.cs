using SQLite;

namespace PizzaTake.Models;

[Table("Products")]
public class Product
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int WeightGrams { get; set; }
    public string SizeName { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public decimal BasePrice { get; set; }
    public decimal? PromoPrice { get; set; }
    public int Calories { get; set; }
    public decimal Proteins { get; set; }
    public decimal Fats { get; set; }
    public decimal Carbs { get; set; }

    [Ignore] public decimal EffectivePrice => GetEffectivePrice();
    public decimal GetEffectivePrice() => PromoPrice ?? BasePrice;
}
