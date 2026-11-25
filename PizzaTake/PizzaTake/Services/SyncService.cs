using System.Text.Json;
using Microsoft.Extensions.Logging;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.Services;

/// <summary>
/// Offline synchronization queue service using Connectivity monitoring and Last-Write-Wins conflict resolution.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ISyncQueueRepository syncQueueRepository;
    private readonly HttpApiClient httpApiClient;
    private readonly ILogger<SyncService>? logger;
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private bool isListening;

    public SyncService(
        ISyncQueueRepository syncQueueRepository,
        HttpApiClient httpApiClient,
        ILogger<SyncService>? logger = null)
    {
        this.syncQueueRepository = syncQueueRepository;
        this.httpApiClient = httpApiClient;
        this.logger = logger;
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
        logger?.LogInformation("SyncQueue: enqueued {Operation} for {EntityType}.", operation, entityType);

        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            await SyncPendingAsync();
    }

    public async Task SyncPendingAsync()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return;

        await syncLock.WaitAsync();
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return;

            var pendingItems = await syncQueueRepository.GetAllPendingAsync();
            if (pendingItems.Count == 0)
                return;

            var syncedAny = false;
            foreach (var item in pendingItems)
            {
                var entityKey = ExtractEntityKey(item);
                var localUpdatedAt = ExtractUpdatedAt(item);
                var remoteUpdatedAt = await httpApiClient.GetRemoteUpdatedAtAsync(item.EntityType, entityKey);

                if (!ShouldLocalWinLastWriteWins(localUpdatedAt, remoteUpdatedAt))
                {
                    logger?.LogWarning(
                        "SyncQueue LWW conflict on {EntityType}:{EntityKey}: remote ({RemoteAt:O}) is newer than local ({LocalAt:O}); keeping remote version.",
                        item.EntityType, entityKey, remoteUpdatedAt, localUpdatedAt);
                    await syncQueueRepository.DeleteAsync(item);
                    syncedAny = true;
                    continue;
                }

                var pushed = await httpApiClient.PushSyncItemAsync(item, entityKey, localUpdatedAt);
                if (!pushed)
                    break;

                await syncQueueRepository.DeleteAsync(item);
                syncedAny = true;
            }

            if (syncedAny)
                SyncCompleted?.Invoke(this, EventArgs.Empty);
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
    }

    public void StopConnectivityListener()
    {
        if (!isListening)
            return;

        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        isListening = false;
    }

    internal static bool ShouldLocalWinLastWriteWins(DateTime localUpdatedAt, DateTime? remoteUpdatedAt) =>
        !remoteUpdatedAt.HasValue || localUpdatedAt >= remoteUpdatedAt.Value;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet)
            return;

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
            logger?.LogError(ex, "Background sync failed.");
        }
    }

    private static string ExtractEntityKey(SyncQueueItem item)
    {
        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("Id", out var idProp))
                    return idProp.ToString();
                if (document.RootElement.TryGetProperty("OrderNumber", out var orderNumProp))
                    return orderNumProp.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall back to queue row id when payload is not structured JSON.
        }

        return item.Id.ToString();
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
