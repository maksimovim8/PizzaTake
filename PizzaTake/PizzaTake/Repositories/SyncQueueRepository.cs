using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for managing pending offline synchronization queue entries.
/// </summary>
public sealed class SyncQueueRepository : SqliteRepositoryBase, ISyncQueueRepository
{
    public SyncQueueRepository(SqliteDatabase database) : base(database)
    {
    }

    public async Task<List<SyncQueueItem>> GetAllPendingAsync()
    {
        var connection = await GetConnectionAsync();
        return await connection.Table<SyncQueueItem>()
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> EnqueueAsync(SyncQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        if (item.CreatedAt == default)
            item.CreatedAt = DateTime.UtcNow;

        return await connection.InsertAsync(item);
    }

    public async Task<int> DeleteAsync(SyncQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        return await connection.DeleteAsync(item);
    }
}
