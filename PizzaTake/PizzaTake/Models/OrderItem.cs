using SQLite;

namespace PizzaTake.Models;

[Table("OrderItems")]
public class OrderItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int WeightGrams { get; set; }
    public string SizeName { get; set; } = string.Empty;
    public string OptionsSummary { get; set; } = string.Empty;
}
