using SQLite;

namespace PizzaTake.Models;

[Table("IngredientOptions")]
public class IngredientOption
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public OptionKind Kind { get; set; }
}
