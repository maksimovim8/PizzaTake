using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;

namespace PizzaTake.Core.Services;

/// <summary>
/// Offline synchronization queue service using <see cref="IConnectivityService"/> and Last-Write-Wins conflict resolution.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ISyncQueueRepository syncQueueRepository;
    private readonly HttpApiClient httpApiClient;
    private readonly IConnectivityService connectivityService;
    private readonly ILogger<SyncService> logger;
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private bool isListening;

    public SyncService(
        ISyncQueueRepository syncQueueRepository,
        HttpApiClient httpApiClient,
        IConnectivityService connectivityService,
        ILogger<SyncService>? logger = null)
    {
        this.syncQueueRepository = syncQueueRepository;
        this.httpApiClient = httpApiClient;
        this.connectivityService = connectivityService;
        this.logger = logger ?? NullLogger<SyncService>.Instance;
    }

    public event EventHandler? SyncCompleted;

    public async Task<int> GetPendingCountAsync()
    {
        var items = await syncQueueRepository.GetAllPendingAsync();
        return items.Count;
    }

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

        if (connectivityService.IsConnected)
            await SyncPendingAsync();
        else
            logger.LogWarning("Device is offline; sync item {EntityType}/{Operation} deferred in queue", entityType, operation);
    }

    public async Task SyncPendingAsync()
    {
        if (!connectivityService.IsConnected)
        {
            logger.LogWarning("Skipping pending sync because connectivity is offline ({Description})", connectivityService.ConnectionDescription);
            return;
        }

        await syncLock.WaitAsync();
        try
        {
            if (!connectivityService.IsConnected)
                return;

            var pendingItems = await syncQueueRepository.GetAllPendingAsync();
            if (pendingItems.Count == 0)
                return;

            logger.LogInformation("Starting synchronization of {PendingCount} queued item(s)", pendingItems.Count);
            var syncedCount = 0;
            foreach (var item in pendingItems)
            {
                var entityKey = ExtractEntityKey(item);
                var localUpdatedAt = ExtractUpdatedAt(item);
                var remoteUpdatedAt = await httpApiClient.GetRemoteUpdatedAtAsync(item.EntityType, entityKey);

                if (!ShouldLocalWinLastWriteWins(localUpdatedAt, remoteUpdatedAt))
                {
                    logger.LogWarning(
                        "SyncQueue LWW conflict on {EntityType}:{EntityKey}: remote ({RemoteAt:O}) is newer than local ({LocalAt:O}); keeping remote version.",
                        item.EntityType, entityKey, remoteUpdatedAt, localUpdatedAt);
                    await syncQueueRepository.DeleteAsync(item);
                    syncedCount++;
                    continue;
                }

                var pushed = await httpApiClient.PushSyncItemAsync(item, entityKey, localUpdatedAt);
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

        connectivityService.StartMonitoring();
        connectivityService.ConnectivityChanged += OnConnectivityChanged;
        isListening = true;
        logger.LogDebug("SyncService connectivity listener started");

        if (connectivityService.IsConnected)
            _ = SyncPendingSafeAsync();
    }

    public void StopConnectivityListener()
    {
        if (!isListening)
            return;

        connectivityService.ConnectivityChanged -= OnConnectivityChanged;
        connectivityService.StopMonitoring();
        isListening = false;
        logger.LogDebug("SyncService connectivity listener stopped");
    }

    public static bool ShouldLocalWinLastWriteWins(DateTime localUpdatedAt, DateTime? remoteUpdatedAt) =>
        !remoteUpdatedAt.HasValue || localUpdatedAt >= remoteUpdatedAt.Value;

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        if (!isConnected)
        {
            logger.LogWarning("Connectivity changed to offline ({Description})", connectivityService.ConnectionDescription);
            return;
        }

        logger.LogInformation("Internet connectivity restored ({Description}); triggering background sync",
            connectivityService.ConnectionDescription);
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

    private static readonly string[] TimestampPropertyPriority =
    [
        "UpdatedAt",
        "UpdatedAtUtc",
        "CreatedAtUtc",
        "CreatedAt"
    ];

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string ExtractEntityKey(SyncQueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PayloadJson))
            return item.Id.ToString();

        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                string? orderNumber = null;
                foreach (var prop in document.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Null)
                        continue;

                    if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        var idValue = prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(idValue))
                            return idValue.Trim();
                    }

                    if (orderNumber is null &&
                        string.Equals(prop.Name, "OrderNumber", StringComparison.OrdinalIgnoreCase))
                    {
                        var orderValue = prop.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(orderValue))
                            orderNumber = orderValue.Trim();
                    }
                }

                if (orderNumber is not null)
                    return orderNumber;
            }
        }
        catch (JsonException)
        {
        }

        return item.Id.ToString();
    }

    private static DateTime ExtractUpdatedAt(SyncQueueItem item)
    {
        if (string.IsNullOrWhiteSpace(item.PayloadJson))
            return NormalizeToUtc(item.CreatedAt);

        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                DateTime? bestMatch = null;
                var bestPriority = int.MaxValue;

                foreach (var prop in document.RootElement.EnumerateObject())
                {
                    for (var i = 0; i < TimestampPropertyPriority.Length && i < bestPriority; i++)
                    {
                        if (string.Equals(prop.Name, TimestampPropertyPriority[i], StringComparison.OrdinalIgnoreCase) &&
                            prop.Value.TryGetDateTime(out var parsed))
                        {
                            bestMatch = NormalizeToUtc(parsed);
                            bestPriority = i;
                            break;
                        }
                    }
                }

                if (bestMatch.HasValue)
                    return bestMatch.Value;
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeToUtc(item.CreatedAt);
    }
}
