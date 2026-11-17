using SQLite;

namespace PizzaTake.Models;

[Table("FavoriteProducts")]
public class FavoriteProduct
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int ProductId { get; set; }
}
