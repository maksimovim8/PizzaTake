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
}
