using PizzaTake.Models;

namespace PizzaTake.Repositories;

/// <summary>
/// Data access contract for orders and their line items.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetActiveAsync();
    Task<List<Order>> GetHistoryAsync();
    Task<Order?> GetByIdAsync(int id);
    Task<List<OrderItem>> GetItemsByOrderIdAsync(int orderId);
    Task SaveOrderWithItemsAsync(Order order, IEnumerable<OrderItem> items);
}
