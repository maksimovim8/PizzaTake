using SQLite;

namespace PizzaTake.Core.Models;

/// <summary>
/// Configurable catalog category managed by administrators.
/// </summary>
[Table("ProductCategories")]
public class ProductCategory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
