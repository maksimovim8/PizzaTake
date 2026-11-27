using SQLite;

namespace PizzaTake.Core.Models;

[Table("FavoriteProducts")]
public class FavoriteProduct
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ProductId { get; set; }
}
