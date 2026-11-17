using PizzaTake.Models;
using SQLite;

namespace PizzaTake.Services;

public sealed class SqliteDatabase
{
    private readonly SQLiteAsyncConnection connection;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool isInitialized;

    public SqliteDatabase()
    {
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "pizzatake.db3");
        connection = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteAsyncConnection Connection => connection;

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;

        await initializationLock.WaitAsync();
        try
        {
            if (isInitialized)
                return;

            await connection.CreateTableAsync<City>();
            await connection.CreateTableAsync<Pizzeria>();
            await connection.CreateTableAsync<Product>();
            await connection.CreateTableAsync<IngredientOption>();
            await connection.CreateTableAsync<FavoriteProduct>();
            await connection.CreateTableAsync<UserProfile>();
            await connection.CreateTableAsync<UserAddress>();
            await connection.CreateTableAsync<CartItem>();
            await connection.CreateTableAsync<Order>();
            await connection.CreateTableAsync<OrderItem>();
            await connection.CreateTableAsync<SyncQueueItem>();

            await SeedCatalogAsync();
            isInitialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task SeedCatalogAsync()
    {
        if (await connection.Table<Product>().CountAsync() > 0)
            return;

        await connection.InsertAllAsync(new[]
        {
            new Product { Name = "Чеддерман", Description = "Сирна піца з чеддером", Category = "Піца", WeightGrams = 560, SizeName = "Середня", IsNew = true, BasePrice = 289m, Calories = 245, Proteins = 12m, Fats = 10m, Carbs = 27m },
            new Product { Name = "Діксон", Description = "Піца з куркою та соусом", Category = "Піца", WeightGrams = 590, SizeName = "Середня", BasePrice = 299m, Calories = 238, Proteins = 14m, Fats = 9m, Carbs = 26m },
            new Product { Name = "Маргарита", Description = "Класична піца з томатами та моцарелою", Category = "Піца", WeightGrams = 520, SizeName = "Середня", BasePrice = 259m, Calories = 220, Proteins = 10m, Fats = 8m, Carbs = 28m }
        });
    }
}
