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
        if (await connection.Table<City>().CountAsync() == 0)
        {
            await connection.InsertAllAsync(new[]
            {
                new City { Name = "Київ", Region = "Київська область", IsPopular = true, Latitude = 50.4501, Longitude = 30.5234 },
                new City { Name = "Львів", Region = "Львівська область", IsPopular = true, Latitude = 49.8397, Longitude = 24.0297 },
                new City { Name = "Одеса", Region = "Одеська область", IsPopular = true, Latitude = 46.4825, Longitude = 30.7233 }
            });
        }

        if (await connection.Table<Pizzeria>().CountAsync() == 0)
        {
            var cityRows = await connection.Table<City>().ToListAsync();
            var kyiv = cityRows.FirstOrDefault(city => city.Name == "Київ");
            var lviv = cityRows.FirstOrDefault(city => city.Name == "Львів");
            var odesa = cityRows.FirstOrDefault(city => city.Name == "Одеса");
            if (kyiv is not null && lviv is not null && odesa is not null)
            {
                await connection.InsertAllAsync(new[]
                {
                    new Pizzeria { CityId = kyiv.Id, StoreNumber = "TT 78", Address = "вул. Хрещатик, 22", OpenFrom = new TimeSpan(10, 0, 0), OpenTo = new TimeSpan(22, 0, 0), Latitude = 50.4477, Longitude = 30.5228 },
                    new Pizzeria { CityId = kyiv.Id, StoreNumber = "TT 84", Address = "просп. Перемоги, 45", OpenFrom = new TimeSpan(10, 0, 0), OpenTo = new TimeSpan(23, 0, 0), Latitude = 50.4500, Longitude = 30.4500 },
                    new Pizzeria { CityId = lviv.Id, StoreNumber = "TT 19", Address = "пл. Ринок, 8", OpenFrom = new TimeSpan(10, 0, 0), OpenTo = new TimeSpan(22, 0, 0), Latitude = 49.8419, Longitude = 24.0315 },
                    new Pizzeria { CityId = odesa.Id, StoreNumber = "TT 32", Address = "вул. Дерибасівська, 12", OpenFrom = new TimeSpan(10, 0, 0), OpenTo = new TimeSpan(22, 0, 0), Latitude = 46.4842, Longitude = 30.7350 }
                });
            }
        }

        if (await connection.Table<Product>().CountAsync() == 0)
        {
            await connection.InsertAllAsync(new[]
            {
                new Product { Name = "Чеддерман", Description = "Бекон, сирний мікс, чеддер", Category = "Новинки", WeightGrams = 560, SizeName = "Середня", IsNew = true, BasePrice = 219m, Calories = 245, Proteins = 12m, Fats = 10m, Carbs = 27m },
                new Product { Name = "Діжон", Description = "Шинка, гриби, гірчичний соус", Category = "WOW піци", WeightGrams = 590, SizeName = "Середня", IsNew = true, BasePrice = 299m, Calories = 238, Proteins = 14m, Fats = 9m, Carbs = 26m },
                new Product { Name = "Маргарита", Description = "Класична піца з томатами та моцарелою", Category = "Standard", WeightGrams = 520, SizeName = "Середня", BasePrice = 259m, Calories = 220, Proteins = 10m, Fats = 8m, Carbs = 28m }
            });
        }
        else
        {
            // Bring forward the original demo catalog labels used by the supplied screen designs.
            var products = await connection.Table<Product>().ToListAsync();
            var cheddar = products.FirstOrDefault(product => product.Name == "Чеддерман" && product.Description == "Сирна піца з чеддером");
            if (cheddar is not null)
            {
                cheddar.Description = "Бекон, сирний мікс, чеддер";
                cheddar.BasePrice = 219m;
                await connection.UpdateAsync(cheddar);
            }

            var dijon = products.FirstOrDefault(product => product.Name == "Діксон" && product.Description == "Піца з куркою та соусом");
            if (dijon is not null)
            {
                dijon.Name = "Діжон";
                dijon.Description = "Шинка, гриби, гірчичний соус";
                dijon.IsNew = true;
                await connection.UpdateAsync(dijon);
            }
        }

        var catalogProducts = await connection.Table<Product>().ToListAsync();
        foreach (var product in catalogProducts)
        {
            var section = product.Name switch
            {
                "Чеддерман" => "Новинки",
                "Діжон" => "WOW піци",
                "Маргарита" => "Standard",
                _ => product.Category
            };
            if (product.Category != section)
            {
                product.Category = section;
                await connection.UpdateAsync(product);
            }

            var defaults = product.Name switch
            {
                "Чеддерман" => new[]
                {
                    new IngredientOption { Name = "Додатковий чеддер", Price = 35m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Халапеньйо", Price = 25m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Без цибулі", Price = 0m, Kind = OptionKind.Exclusion },
                    new IngredientOption { Name = "Заміна соусу", Price = 15m, Kind = OptionKind.Replacement }
                },
                "Діжон" => new[]
                {
                    new IngredientOption { Name = "Додаткова шинка", Price = 35m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Додаткові гриби", Price = 25m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Без грибів", Price = 0m, Kind = OptionKind.Exclusion },
                    new IngredientOption { Name = "Заміна гірчичного соусу на томатний", Price = 15m, Kind = OptionKind.Replacement }
                },
                "Маргарита" => new[]
                {
                    new IngredientOption { Name = "Додаткова моцарела", Price = 35m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Оливки", Price = 25m, Kind = OptionKind.AddOn },
                    new IngredientOption { Name = "Без томатів", Price = 0m, Kind = OptionKind.Exclusion },
                    new IngredientOption { Name = "Заміна моцарели на фету", Price = 15m, Kind = OptionKind.Replacement }
                },
                _ => Array.Empty<IngredientOption>()
            };
            var existing = await connection.Table<IngredientOption>().Where(option => option.ProductId == product.Id).ToListAsync();
            var missing = defaults
                .Where(option => existing.All(saved => saved.Kind != option.Kind || saved.Name != option.Name))
                .Select(option => { option.ProductId = product.Id; return option; })
                .ToList();
            if (missing.Count > 0)
                await connection.InsertAllAsync(missing);
        }
    }
}
