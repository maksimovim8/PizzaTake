using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite CRUD repository for saved user delivery addresses.
/// </summary>
public sealed class AddressRepository : SqliteRepositoryBase, IAddressRepository
{
    public AddressRepository(SqliteDatabase database) : base(database)
    {
    }

    public async Task<List<UserAddress>> GetAllAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<UserAddress>()
            .OrderByDescending(a => a.LastUsedAt)
            .ToListAsync();
    }

    public async Task<UserAddress?> GetByIdAsync(int id)
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<UserAddress>().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<int> SaveAsync(UserAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var connection = await GetConnectionAsync();
        if (address.LastUsedAt == default)
            address.LastUsedAt = DateTime.UtcNow;

        return await InsertOrUpdateAsync(connection, address, address.Id);
    }

    public async Task<int> DeleteAsync(UserAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var connection = await GetConnectionAsync();
        return await connection.DeleteAsync(address);
    }
}
