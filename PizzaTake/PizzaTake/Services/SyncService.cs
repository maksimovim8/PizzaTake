using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.Services;

/// <summary>
/// Offline synchronization queue service using Connectivity monitoring and Last Write Wins conflict resolution.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ISyncQueueRepository syncQueueRepository;
    private readonly HttpApiClient httpApiClient;
    private readonly ILogger<SyncService> logger;
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private bool isListening;

    public SyncService(
        ISyncQueueRepository syncQueueRepository,
        HttpApiClient httpApiClient,
        ILogger<SyncService>? logger = null)
    {
        this.syncQueueRepository = syncQueueRepository;
        this.httpApiClient = httpApiClient;
        this.logger = logger ?? NullLogger<SyncService>.Instance;
    }

    public event EventHandler? SyncCompleted;

    public async Task EnqueueAsync(string entityType, string operation, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadJson = payload as string ?? JsonSerializer.Serialize(payload);
        var item = new SyncQueueItem
        {
            EntityType = entityType,
            Operation = operation,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        };

        await syncQueueRepository.EnqueueAsync(item);
        logger.LogInformation("Enqueued sync item {EntityType}/{Operation} (Id={SyncItemId})", entityType, operation, item.Id);

        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            await SyncPendingAsync();
        else
            logger.LogWarning("Device is offline; sync item {EntityType}/{Operation} deferred in queue", entityType, operation);
    }

    public async Task SyncPendingAsync()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            logger.LogWarning("Skipping pending sync because network access is {NetworkAccess}", Connectivity.Current.NetworkAccess);
            return;
        }

        await syncLock.WaitAsync();
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return;

            var pendingItems = await syncQueueRepository.GetAllPendingAsync();
            if (pendingItems.Count == 0)
                return;

            logger.LogInformation("Starting synchronization of {PendingCount} queued item(s)", pendingItems.Count);
            var syncedCount = 0;
            foreach (var item in pendingItems)
            {
                var resolvedItem = (SyncQueueItem)ResolveConflictLastWriteWins(item, item);
                var pushed = await httpApiClient.PushSyncItemAsync(resolvedItem);
                if (!pushed)
                {
                    logger.LogWarning("Sync paused because pushing item {SyncItemId} ({EntityType}/{Operation}) failed", item.Id, item.EntityType, item.Operation);
                    break;
                }

                await syncQueueRepository.DeleteAsync(item);
                syncedCount++;
            }

            if (syncedCount > 0)
            {
                logger.LogInformation("Synchronization completed: {SyncedCount} item(s) delivered", syncedCount);
                SyncCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Synchronization of pending queue items failed");
            throw;
        }
        finally
        {
            syncLock.Release();
        }
    }

    public void StartConnectivityListener()
    {
        if (isListening)
            return;

        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        isListening = true;
        logger.LogDebug("SyncService connectivity listener started");
    }

    public void StopConnectivityListener()
    {
        if (!isListening)
            return;

        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        isListening = false;
        logger.LogDebug("SyncService connectivity listener stopped");
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet)
        {
            logger.LogWarning("Connectivity changed to offline ({NetworkAccess})", e.NetworkAccess);
            return;
        }

        logger.LogInformation("Internet connectivity restored; triggering background sync");
        _ = SyncPendingSafeAsync();
    }

    private async Task SyncPendingSafeAsync()
    {
        try
        {
            await SyncPendingAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background sync failed after connectivity change");
        }
    }

    private object ResolveConflictLastWriteWins(object local, object remote)
    {
        if (local is SyncQueueItem localItem && remote is SyncQueueItem remoteItem)
        {
            var localUpdated = ExtractUpdatedAt(localItem);
            var remoteUpdated = ExtractUpdatedAt(remoteItem);
            if (localUpdated < remoteUpdated)
            {
                logger.LogWarning(
                    "LWW conflict resolved in favor of remote record for {EntityType} (Local={LocalUpdated:O}, Remote={RemoteUpdated:O})",
                    localItem.EntityType,
                    localUpdated,
                    remoteUpdated);
                return remoteItem;
            }

            return localItem;
        }

        return local;
    }

    private static DateTime ExtractUpdatedAt(SyncQueueItem item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("UpdatedAt", out var updatedProp) &&
                updatedProp.TryGetDateTime(out var updatedAt))
            {
                return updatedAt;
            }
        }
        catch (JsonException)
        {
            // Fall back to CreatedAt when payload has no UpdatedAt field or is not JSON.
        }

        return item.CreatedAt;
    }
}
