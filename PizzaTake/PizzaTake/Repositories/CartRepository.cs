using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

public interface ICartRepository
{
    Task<List<CartItem>> GetAllAsync();
    Task SaveAsync(CartItem item);
    Task DeleteAsync(CartItem item);
    Task ClearAsync();
}

public sealed class CartRepository(SqliteDatabase database) : ICartRepository
{
    public async Task<List<CartItem>> GetAllAsync()
    {
        await database.InitializeAsync();
        return await database.Connection.Table<CartItem>().OrderBy(item => item.Id).ToListAsync();
    }

    public async Task SaveAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(item), "Cart item quantity must be positive.");
        await database.InitializeAsync();
        await database.Connection.InsertOrReplaceAsync(item);
    }

    public async Task DeleteAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        await database.InitializeAsync();
        await database.Connection.DeleteAsync(item);
    }

    public async Task ClearAsync()
    {
        await database.InitializeAsync();
        await database.Connection.DeleteAllAsync<CartItem>();
    }
}
