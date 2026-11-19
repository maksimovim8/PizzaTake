using PizzaTake.Models;

namespace PizzaTake.Repositories;

/// <summary>
/// Data access contract for the offline synchronization queue.
/// </summary>
public interface ISyncQueueRepository
{
    Task<List<SyncQueueItem>> GetAllPendingAsync();
    Task<int> EnqueueAsync(SyncQueueItem item);
    Task<int> DeleteAsync(SyncQueueItem item);
}
