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
    public async Task InitializeAsync_ShouldApplyMigrationsSetSchemaVersionAndSeedCatalog()
    {
        await database.InitializeAsync();

        var schemaVersion = await database.GetSchemaVersionAsync();
        schemaVersion.Should().Be(SqliteDatabase.LatestSchemaVersion);

        var addressColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(UserAddresses);");
        addressColumns.Select(c => c.Name).Should().Contain(["Notes", "Latitude", "Longitude"]);

        var profileColumns = await database.Connection.QueryAsync<ColumnRow>("PRAGMA table_info(UserProfiles);");
        profileColumns.Select(c => c.Name).Should().Contain("Role");

        var cities = await database.Connection.Table<City>().ToListAsync();
        var products = await database.Connection.Table<Product>().ToListAsync();
        cities.Should().HaveCountGreaterOrEqualTo(3);
        products.Should().HaveCountGreaterOrEqualTo(3);
    }

    private sealed class ColumnRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
