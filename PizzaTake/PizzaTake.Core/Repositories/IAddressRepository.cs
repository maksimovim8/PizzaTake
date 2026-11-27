using PizzaTake.Core.Models;

namespace PizzaTake.Core.Repositories;

/// <summary>
/// Data access contract for saved user delivery addresses.
/// </summary>
public interface IAddressRepository
{
    Task<List<UserAddress>> GetAllAsync();
    Task<UserAddress?> GetByIdAsync(int id);
    Task<int> SaveAsync(UserAddress address);
    Task<int> DeleteAsync(UserAddress address);
}
