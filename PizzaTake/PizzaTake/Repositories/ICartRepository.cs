using PizzaTake.Models;

namespace PizzaTake.Repositories;

/// <summary>
/// Data access contract for shopping cart items.
/// </summary>
public interface ICartRepository
{
    event EventHandler? CartChanged;
    Task<List<CartItem>> GetAllAsync();
    Task<int> SaveAsync(CartItem item);
    Task<int> DeleteAsync(CartItem item);
    Task ClearAsync();
}
