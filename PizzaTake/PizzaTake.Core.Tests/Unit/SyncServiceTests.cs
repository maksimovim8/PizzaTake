using FluentAssertions;
using Moq;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using Xunit;

namespace PizzaTake.Core.Tests.Unit;

public sealed class SyncServiceTests
{
    [Fact]
    public async Task SyncPendingAsync_WhenRemoteIsNewer_ShouldKeepRemoteAndNotOverwrite()
    {
        var syncRepo = new Mock<ISyncQueueRepository>();
        var connectivity = new Mock<IConnectivityService>();
        connectivity.Setup(c => c.IsConnected).Returns(true);

        using var apiClient = new HttpApiClient();
        var newerTime = DateTime.UtcNow;
        var staleTime = newerTime.AddMinutes(-10);

        // Seed remote simulation with a newer timestamp for Order:77
        var seedItem = new SyncQueueItem
        {
            Id = 1,
            EntityType = "Order",
            Operation = "Insert",
            PayloadJson = $"{{\"Id\":77,\"UpdatedAt\":\"{newerTime:O}\"}}",
            CreatedAt = newerTime
        };
        await apiClient.PushSyncItemAsync(seedItem, "77", newerTime);

        var staleQueueItem = new SyncQueueItem
        {
            Id = 2,
            EntityType = "Order",
            Operation = "Update",
            PayloadJson = $"{{\"Id\":77,\"UpdatedAt\":\"{staleTime:O}\"}}",
            CreatedAt = staleTime
        };
        syncRepo.Setup(r => r.GetAllPendingAsync()).ReturnsAsync([staleQueueItem]);

        var sut = new SyncService(syncRepo.Object, apiClient, connectivity.Object);
        await sut.SyncPendingAsync();

        var remoteAfter = await apiClient.GetRemoteUpdatedAtAsync("Order", "77");
        remoteAfter.Should().Be(newerTime);
        SyncService.ShouldLocalWinLastWriteWins(staleTime, newerTime).Should().BeFalse();
        SyncService.ShouldLocalWinLastWriteWins(newerTime, staleTime).Should().BeTrue();
        syncRepo.Verify(r => r.DeleteAsync(staleQueueItem), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_WhenOffline_ShouldQueueWithoutAttemptingSync()
    {
        var syncRepo = new Mock<ISyncQueueRepository>();
        var connectivity = new Mock<IConnectivityService>();
        connectivity.Setup(c => c.IsConnected).Returns(false);
        connectivity.Setup(c => c.ConnectionDescription).Returns("None");

        using var apiClient = new HttpApiClient();
        var sut = new SyncService(syncRepo.Object, apiClient, connectivity.Object);

        await sut.EnqueueAsync("Order", "Insert", new { Id = 1, UpdatedAt = DateTime.UtcNow });

        syncRepo.Verify(r => r.EnqueueAsync(It.Is<SyncQueueItem>(i => i.EntityType == "Order" && i.Operation == "Insert")), Times.Once);
        syncRepo.Verify(r => r.GetAllPendingAsync(), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_WithCaseInsensitiveUpdatedAtUtcAndNonUtcTimestamps_ShouldNormalizeToUtcForLww()
    {
        var syncRepo = new Mock<ISyncQueueRepository>();
        var connectivity = new Mock<IConnectivityService>();
        connectivity.Setup(c => c.IsConnected).Returns(true);

        using var apiClient = new HttpApiClient();
        var remoteTimeUtc = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        var seedItem = new SyncQueueItem
        {
            Id = 1,
            EntityType = "Order",
            Operation = "Insert",
            PayloadJson = $"{{\"Id\":88,\"UpdatedAt\":\"{remoteTimeUtc:O}\"}}",
            CreatedAt = remoteTimeUtc
        };
        await apiClient.PushSyncItemAsync(seedItem, "88", remoteTimeUtc);

        // Local offset +02:00 -> 13:30+02:00 is 11:30Z (older than 12:00Z), even though CreatedAt is newer.
        var staleLocalItem = new SyncQueueItem
        {
            Id = 2,
            EntityType = "Order",
            Operation = "Update",
            PayloadJson = "{\"id\":88,\"updatedAtUtc\":\"2026-11-27T13:30:00+02:00\"}",
            CreatedAt = remoteTimeUtc.AddHours(3)
        };

        syncRepo.Setup(r => r.GetAllPendingAsync()).ReturnsAsync([staleLocalItem]);

        var sut = new SyncService(syncRepo.Object, apiClient, connectivity.Object);
        await sut.SyncPendingAsync();

        var remoteAfterStale = await apiClient.GetRemoteUpdatedAtAsync("Order", "88");
        remoteAfterStale.Should().Be(remoteTimeUtc);
        syncRepo.Verify(r => r.DeleteAsync(staleLocalItem), Times.Once);

        // Unspecified timestamp without offset should be treated as UTC -> 12:30:00Z (newer than 12:00:00Z).
        var newerUnspecifiedItem = new SyncQueueItem
        {
            Id = 3,
            EntityType = "Order",
            Operation = "Update",
            PayloadJson = "{\"id\":88,\"updatedAtUtc\":\"2026-11-27T12:30:00\"}",
            CreatedAt = remoteTimeUtc.AddHours(-5)
        };
        syncRepo.Setup(r => r.GetAllPendingAsync()).ReturnsAsync([newerUnspecifiedItem]);

        await sut.SyncPendingAsync();

        var expectedNewerUtc = new DateTime(2026, 11, 27, 12, 30, 0, DateTimeKind.Utc);
        var remoteAfterNewer = await apiClient.GetRemoteUpdatedAtAsync("Order", "88");
        remoteAfterNewer.Should().Be(expectedNewerUtc);
        remoteAfterNewer!.Value.Kind.Should().Be(DateTimeKind.Utc);
        syncRepo.Verify(r => r.DeleteAsync(newerUnspecifiedItem), Times.Once);
    }

    [Fact]
    public async Task StartConnectivityListener_WhenAlreadyOnline_ShouldTriggerPendingSync()
    {
        var syncRepo = new Mock<ISyncQueueRepository>();
        var connectivity = new Mock<IConnectivityService>();
        connectivity.Setup(c => c.IsConnected).Returns(true);
        connectivity.Setup(c => c.ConnectionDescription).Returns("Wi-Fi");

        var item = new SyncQueueItem
        {
            Id = 5,
            EntityType = "UserAddress",
            Operation = "Insert",
            PayloadJson = "{\"Id\":5,\"UpdatedAt\":\"2026-11-27T14:00:00Z\"}",
            CreatedAt = DateTime.UtcNow
        };
        var syncedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        syncRepo.Setup(r => r.GetAllPendingAsync()).ReturnsAsync([item]);
        syncRepo.Setup(r => r.DeleteAsync(item)).ReturnsAsync(1);

        var sut = new SyncService(syncRepo.Object, new HttpApiClient(), connectivity.Object);
        sut.SyncCompleted += (_, _) => syncedTcs.TrySetResult();

        sut.StartConnectivityListener();

        var completed = await Task.WhenAny(syncedTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().Be(syncedTcs.Task);
        syncRepo.Verify(r => r.DeleteAsync(item), Times.Once);
        sut.StopConnectivityListener();
    }
}
