using SQLite;

namespace PizzaTake.Models;

[Table("CartItems")]
public class CartItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string OptionsSummary { get; set; } = string.Empty;
    public decimal CalculateSubtotal() => UnitPrice * Quantity;
}
