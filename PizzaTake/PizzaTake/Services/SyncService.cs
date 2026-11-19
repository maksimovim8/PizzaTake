using System.Diagnostics;
using System.Text.Json;
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
    private readonly SemaphoreSlim syncLock = new(1, 1);
    private bool isListening;

    public SyncService(ISyncQueueRepository syncQueueRepository, HttpApiClient httpApiClient)
    {
        this.syncQueueRepository = syncQueueRepository;
        this.httpApiClient = httpApiClient;
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
                var resolvedItem = (SyncQueueItem)ResolveConflictLastWriteWins(item, item);
                var pushed = await httpApiClient.PushSyncItemAsync(resolvedItem);
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
            Debug.WriteLine($"[SyncService] Background sync failed: {ex.Message}");
        }
    }

    private static object ResolveConflictLastWriteWins(object local, object remote)
    {
        if (local is SyncQueueItem localItem && remote is SyncQueueItem remoteItem)
            return ExtractUpdatedAt(localItem) >= ExtractUpdatedAt(remoteItem) ? localItem : remoteItem;

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
