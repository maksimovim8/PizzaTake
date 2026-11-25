using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for persisting and managing shopping cart items.
/// </summary>
public sealed class CartRepository : SqliteRepositoryBase, ICartRepository
{
    private readonly ILogger<CartRepository> logger;

    public CartRepository(SqliteDatabase database, ILogger<CartRepository>? logger = null) : base(database)
    {
        this.logger = logger ?? NullLogger<CartRepository>.Instance;
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
        var rows = await InsertOrUpdateAsync(connection, item, item.Id);
        logger.LogDebug("Saved cart item {ProductName} (Qty={Quantity})", item.ProductName, item.Quantity);
        return rows;
    }

    public async Task<int> DeleteAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        var rows = await connection.DeleteAsync(item);
        logger.LogDebug("Deleted cart item {ProductName} (Id={CartItemId})", item.ProductName, item.Id);
        return rows;
    }

    public async Task ClearAsync()
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAllAsync<CartItem>();
        logger.LogInformation("Cleared all items from shopping cart");
    }
}
