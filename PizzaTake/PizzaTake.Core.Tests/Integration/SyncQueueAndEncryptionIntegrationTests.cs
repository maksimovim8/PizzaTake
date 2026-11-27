using FluentAssertions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Core.Tests.Helpers;
using Xunit;

namespace PizzaTake.Core.Tests.Integration;

public sealed class SyncQueueAndEncryptionIntegrationTests : IAsyncLifetime
{
    private string dbPath = string.Empty;
    private SqliteDatabase database = null!;

    public async Task InitializeAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        dbPath = Path.Combine(Path.GetTempPath(), $"pizzatake_sec_{Guid.NewGuid():N}.db3");
        database = new SqliteDatabase(dbPath);
        await database.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await database.CloseAsync();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    [Fact]
    public async Task SyncQueueRepository_EnqueueListAndDelete_ShouldPersistCorrectly()
    {
        var repo = new SyncQueueRepository(database);
        var item = new SyncQueueItem
        {
            EntityType = "Order",
            Operation = "Insert",
            PayloadJson = "{\"Id\":101,\"OrderNumber\":\"PD-1010\"}"
        };

        await repo.EnqueueAsync(item);
        item.Id.Should().BeGreaterThan(0);

        var pending = await repo.GetAllPendingAsync();
        pending.Should().ContainSingle(i => i.Id == item.Id && i.EntityType == "Order");

        await repo.DeleteAsync(item);
        (await repo.GetAllPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task UserProfileRepository_WithAesEncryption_ShouldEncryptAtRestAndDecryptOnRead()
    {
        var secureStore = new InMemorySecureStore();
        var aes = new AesEncryptionService(secureStore);
        var repo = new UserProfileRepository(database, aes);

        var profile = new UserProfile
        {
            Phone = "+380990000001",
            CityName = "Київ",
            IsPhoneVerified = true,
            Role = UserRole.Administrator
        };

        await repo.SaveAsync(profile);

        // Verify raw row in SQLite is encrypted (never stored as plaintext).
        var rawStored = await database.Connection.Table<UserProfile>().FirstAsync();
        rawStored.Phone.Should().NotBe("+380990000001").And.NotBeNullOrEmpty();
        rawStored.CityName.Should().NotBe("Київ").And.NotBeNullOrEmpty();

        // Verify repository transparently decrypts fields and preserves Role.
        var decrypted = await repo.GetCurrentAsync();
        decrypted.Should().NotBeNull();
        decrypted!.Phone.Should().Be("+380990000001");
        decrypted.CityName.Should().Be("Київ");
        decrypted.Role.Should().Be(UserRole.Administrator);
    }
}
