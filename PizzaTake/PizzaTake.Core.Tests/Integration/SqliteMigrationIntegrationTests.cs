using FluentAssertions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Services;
using SQLite;
using Xunit;

namespace PizzaTake.Core.Tests.Integration;

public sealed class SqliteMigrationIntegrationTests : IAsyncLifetime
{
    private string dbPath = string.Empty;
    private SqliteDatabase database = null!;

    public Task InitializeAsync()
    {
        SQLitePCL.Batteries_V2.Init();
        dbPath = Path.Combine(Path.GetTempPath(), $"pizzatake_mig_{Guid.NewGuid():N}.db3");
        database = new SqliteDatabase(dbPath);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await database.CloseAsync();
        if (File.Exists(dbPath))
            File.Delete(dbPath);
    }

    [Fact]
    public async Task InitializeAsync_ShouldApplyMigrationsToV3CreateQualityReportsAndSeedCatalog()
    {
        await database.InitializeAsync();

        SqliteDatabase.LatestSchemaVersion.Should().Be(3);
        var schemaVersion = await database.GetSchemaVersionAsync();
        schemaVersion.Should().Be(3);

        var addressColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(UserAddresses);");
        addressColumns.Select(c => c.Name).Should().Contain(["Notes", "Latitude", "Longitude"]);

        var profileColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(UserProfiles);");
        profileColumns.Select(c => c.Name).Should().Contain("Role");

        var qualityReportColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(QualityReports);");
        qualityReportColumns.Select(c => c.Name).Should().Contain(
            ["Id", "OrderId", "OrderNumber", "Description", "PhotoPath", "Latitude", "Longitude", "CreatedAtUtc"]);

        var cities = await database.Connection.Table<City>().ToListAsync();
        var products = await database.Connection.Table<Product>().ToListAsync();
        cities.Should().HaveCountGreaterOrEqualTo(3);
        products.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task InitializeAsync_FromPreExistingV2Database_ShouldMigrateCleanlyToV3()
    {
        await database.Connection.CreateTableAsync<UserProfile>();
        await database.Connection.CreateTableAsync<UserAddress>();
        await database.Connection.CreateTableAsync<City>();
        await database.Connection.CreateTableAsync<Pizzeria>();
        await database.Connection.CreateTableAsync<Product>();
        await database.Connection.CreateTableAsync<IngredientOption>();
        await database.Connection.CreateTableAsync<FavoriteProduct>();
        await database.Connection.CreateTableAsync<CartItem>();
        await database.Connection.CreateTableAsync<Order>();
        await database.Connection.CreateTableAsync<OrderItem>();
        await database.Connection.CreateTableAsync<SyncQueueItem>();
        await database.Connection.ExecuteAsync("PRAGMA user_version = 2;");

        await database.InitializeAsync();

        (await database.GetSchemaVersionAsync()).Should().Be(3);
        var qualityReportColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(QualityReports);");
        qualityReportColumns.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InitializeAsync_OnSubsequentAppRestart_ShouldNotOverwriteAdminCategoryOrResurrectDeletedBannersAndOptions()
    {
        await database.InitializeAsync();

        var cheddar = await database.Connection.Table<Product>().Where(p => p.Name == "Чеддерман").FirstAsync();
        cheddar.Category = "Спеціальні";
        await database.Connection.UpdateAsync(cheddar);

        var cheddarOptions = await database.Connection.Table<IngredientOption>().Where(o => o.ProductId == cheddar.Id).ToListAsync();
        cheddarOptions.Should().NotBeEmpty();
        await database.Connection.DeleteAsync(cheddarOptions[0]);
        var expectedOptionCount = cheddarOptions.Count - 1;

        await database.Connection.DeleteAllAsync<MenuBanner>();
        await database.CloseAsync();

        // Simulate app restart with a fresh SqliteDatabase instance on the same file.
        database = new SqliteDatabase(dbPath);
        await database.InitializeAsync();

        var reloadedCheddar = await database.Connection.Table<Product>().Where(p => p.Name == "Чеддерман").FirstAsync();
        reloadedCheddar.Category.Should().Be("Спеціальні");

        var reloadedOptions = await database.Connection.Table<IngredientOption>().Where(o => o.ProductId == reloadedCheddar.Id).ToListAsync();
        reloadedOptions.Should().HaveCount(expectedOptionCount);

        var reloadedBanners = await database.Connection.Table<MenuBanner>().ToListAsync();
        reloadedBanners.Should().BeEmpty();
    }

    private sealed class ColumnRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
