namespace PizzaTake.Services;

/// <summary>
/// Manages offline change queueing and background synchronization on connectivity changes.
/// </summary>
public interface ISyncService
{
    event EventHandler? SyncCompleted;
    Task EnqueueAsync(string entityType, string operation, object payload);
    Task SyncPendingAsync();
    void StartConnectivityListener();
    void StopConnectivityListener();
}
