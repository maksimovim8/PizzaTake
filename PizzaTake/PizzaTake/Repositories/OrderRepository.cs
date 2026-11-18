using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for reading and transactionally saving orders and order items.
/// </summary>
public sealed class OrderRepository : SqliteRepositoryBase, IOrderRepository
{
    public OrderRepository(SqliteDatabase database) : base(database)
    {
    }

    public async Task<Order?> GetActiveAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Order>()
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Order>> GetHistoryAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Order>()
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<Order>().FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<OrderItem>> GetItemsByOrderIdAsync(int orderId)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<OrderItem>()
            .Where(i => i.OrderId == orderId)
            .ToListAsync();
    }

    public async Task SaveOrderWithItemsAsync(Order order, IEnumerable<OrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();
        var connection = await GetConnectionAsync();

        await connection.RunInTransactionAsync(tran =>
        {
            if (order.Id != 0)
                tran.Update(order);
            else
                tran.Insert(order);

            foreach (var item in itemList)
            {
                item.OrderId = order.Id;
                if (item.Id != 0)
                    tran.Update(item);
                else
                    tran.Insert(item);
            }
        });
    }
}
