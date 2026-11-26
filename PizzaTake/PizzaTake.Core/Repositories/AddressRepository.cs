using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.Repositories;

/// <summary>
/// SQLite CRUD repository for saved user delivery addresses.
/// </summary>
public sealed class AddressRepository : SqliteRepositoryBase, IAddressRepository
{
    private readonly ILogger<AddressRepository> logger;

    public AddressRepository(SqliteDatabase database, ILogger<AddressRepository>? logger = null) : base(database)
    {
        this.logger = logger ?? NullLogger<AddressRepository>.Instance;
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

        var rows = await InsertOrUpdateAsync(connection, address, address.Id);
        logger.LogInformation("Saved delivery address (Id={AddressId}, Label={Label})", address.Id, address.Label);
        return rows;
    }

    public async Task<int> DeleteAsync(UserAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var connection = await GetConnectionAsync();
        var rows = await connection.DeleteAsync(address);
        logger.LogInformation("Deleted delivery address (Id={AddressId})", address.Id);
        return rows;
    }
}
