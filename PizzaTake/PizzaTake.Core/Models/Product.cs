using SQLite;

namespace PizzaTake.Core.Models;

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
    [Ignore] public bool HasPromo => PromoPrice.HasValue && PromoPrice.Value > 0m && BasePrice > 0m && PromoPrice.Value < BasePrice;
    [Ignore]
    public int DiscountPercent
    {
        get
        {
            if (!HasPromo)
                return 0;

            var raw = (int)Math.Round(100m * (PromoPrice!.Value / BasePrice - 1m), MidpointRounding.AwayFromZero);
            return raw == 0 ? -1 : raw;
        }
    }
    [Ignore] public string DiscountBadgeText => HasPromo ? $"{DiscountPercent}%" : string.Empty;
    [Ignore] public bool ShowCategoryHeader { get; set; }
    public decimal GetEffectivePrice() => PromoPrice ?? BasePrice;
}
