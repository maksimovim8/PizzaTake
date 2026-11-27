using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.Repositories;

/// <summary>
/// SQLite repository for managing pending offline synchronization queue entries.
/// </summary>
public sealed class SyncQueueRepository : SqliteRepositoryBase, ISyncQueueRepository
{
    private readonly ILogger<SyncQueueRepository> logger;

    public SyncQueueRepository(SqliteDatabase database, ILogger<SyncQueueRepository>? logger = null) : base(database)
    {
        this.logger = logger ?? NullLogger<SyncQueueRepository>.Instance;
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

        var rows = await connection.InsertAsync(item);
        logger.LogDebug("Persisted SyncQueue item {EntityType}/{Operation} (Id={SyncItemId})", item.EntityType, item.Operation, item.Id);
        return rows;
    }

    public async Task<int> DeleteAsync(SyncQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var connection = await GetConnectionAsync();
        var rows = await connection.DeleteAsync(item);
        logger.LogDebug("Removed SyncQueue item (Id={SyncItemId})", item.Id);
        return rows;
    }
}
