using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for persisting and managing shopping cart items.
/// </summary>
public sealed class CartRepository : SqliteRepositoryBase, ICartRepository
{
    public CartRepository(SqliteDatabase database) : base(database)
    {
    }

    public async Task<List<CartItem>> GetAllAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<CartItem>().ToListAsync();
    }

    public async Task<int> SaveAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        return await InsertOrUpdateAsync(connection, item, item.Id);
    }

    public async Task<int> DeleteAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        return await connection.DeleteAsync(item);
    }

    public async Task ClearAsync()
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAllAsync<CartItem>();
    }
}
