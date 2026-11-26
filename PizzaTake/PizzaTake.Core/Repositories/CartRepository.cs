using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.Repositories;

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

    public event EventHandler? CartChanged;

    public async Task<List<CartItem>> GetAllAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<CartItem>().ToListAsync();
    }

    public async Task<int> SaveAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();

        int rows;
        if (item.Id == 0)
        {
            var normalizedOptions = (item.OptionsSummary ?? string.Empty).Trim();
            var existingItems = await connection.Table<CartItem>().ToListAsync();
            var duplicate = existingItems.FirstOrDefault(existing =>
                existing.ProductId == item.ProductId &&
                string.Equals(existing.ProductName, item.ProductName, StringComparison.OrdinalIgnoreCase) &&
                existing.UnitPrice == item.UnitPrice &&
                string.Equals((existing.OptionsSummary ?? string.Empty).Trim(), normalizedOptions, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                duplicate.Quantity += Math.Max(1, item.Quantity);
                rows = await connection.UpdateAsync(duplicate);
                item.Id = duplicate.Id;
                item.Quantity = duplicate.Quantity;
                logger.LogDebug("Merged cart item {ProductName} (NewQty={Quantity})", duplicate.ProductName, duplicate.Quantity);
                CartChanged?.Invoke(this, EventArgs.Empty);
                return rows;
            }
        }

        rows = await InsertOrUpdateAsync(connection, item, item.Id);
        logger.LogDebug("Saved cart item {ProductName} (Qty={Quantity})", item.ProductName, item.Quantity);
        CartChanged?.Invoke(this, EventArgs.Empty);
        return rows;
    }

    public async Task<int> DeleteAsync(CartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        var rows = await connection.DeleteAsync(item);
        logger.LogDebug("Deleted cart item {ProductName} (Id={CartItemId})", item.ProductName, item.Id);
        CartChanged?.Invoke(this, EventArgs.Empty);
        return rows;
    }

    public async Task ClearAsync()
    {
        var connection = await GetConnectionAsync();
        await connection.DeleteAllAsync<CartItem>();
        logger.LogInformation("Cleared all items from shopping cart");
        CartChanged?.Invoke(this, EventArgs.Empty);
    }
}
