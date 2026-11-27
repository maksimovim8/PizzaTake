using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using SQLite;

namespace PizzaTake.Core.Services;

public sealed class SqliteDatabase
{
    public const int LatestSchemaVersion = 4;

    private readonly SQLiteAsyncConnection connection;
    private readonly ILogger<SqliteDatabase> logger;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool isInitialized;

    public SqliteDatabase(IAppPathsService appPaths, ILogger<SqliteDatabase>? logger = null)
        : this(appPaths?.DatabasePath ?? throw new ArgumentNullException(nameof(appPaths)), logger)
    {
    }

    public SqliteDatabase(string databasePath, ILogger<SqliteDatabase>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.logger = logger ?? NullLogger<SqliteDatabase>.Instance;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        connection = new SQLiteAsyncConnection(databasePath);
        this.logger.LogDebug("Configured SQLite database at {DatabasePath}", databasePath);
    }

    public SQLiteAsyncConnection Connection => connection;

    public Task<int> GetSchemaVersionAsync() =>
        connection.ExecuteScalarAsync<int>("PRAGMA user_version;");

    public Task CloseAsync() => connection.CloseAsync();

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;

        await initializationLock.WaitAsync();
        try
        {
            if (isInitialized)
                return;

            logger.LogInformation("Initializing SQLite database tables, migrations, and catalog seed");
            await ApplyMigrationsAsync();
            await SeedCatalogAsync();
            isInitialized = true;
            logger.LogInformation("SQLite database initialization completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQLite database initialization failed");
            throw;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task ApplyMigrationsAsync()
    {
        var currentVersion = await GetSchemaVersionAsync();
        logger.LogInformation("SQLite schema check: current user_version={CurrentVersion}, target={TargetVersion}",
            currentVersion, LatestSchemaVersion);

        if (currentVersion < 1)
        {
            await CreateBaseTablesAsync();
            await connection.ExecuteAsync("PRAGMA user_version = 1;");
            currentVersion = 1;
            logger.LogInformation("Applied SQLite migration to schema v1 (base tables).");
        }

        if (currentVersion < 2)
        {
            await MigrateToV2Async();
            await connection.ExecuteAsync("PRAGMA user_version = 2;");
            currentVersion = 2;
            logger.LogInformation("Applied SQLite migration to schema v2 (UserAddresses Notes & coordinates).");
        }

        if (currentVersion < 3)
        {
            await MigrateToV3Async();
            await connection.ExecuteAsync("PRAGMA user_version = 3;");
            currentVersion = 3;
            logger.LogInformation("Applied SQLite migration to schema v3 (QualityReports table & UserProfiles.Role).");
        }

        if (currentVersion < 4)
        {
            await MigrateToV4Async();
            await connection.ExecuteAsync("PRAGMA user_version = 4;");
            logger.LogInformation("Applied SQLite migration to schema v4 (ProductCategories & MenuBanners tables).");
        }
    }

    private async Task MigrateToV4Async()
    {
        await EnsureUserProfileRoleColumnAsync();
        await connection.CreateTableAsync<ProductCategory>();
        await connection.CreateTableAsync<MenuBanner>();
    }

    private async Task MigrateToV3Async()
    {
        await connection.CreateTableAsync<QualityReport>();
        await EnsureUserProfileRoleColumnAsync();
    }

    private async Task EnsureUserProfileRoleColumnAsync()
    {
        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(UserProfiles);");
        var existingNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(nameof(UserProfile.Role)))
        {
            await connection.ExecuteAsync("ALTER TABLE UserProfiles ADD COLUMN Role INTEGER NOT NULL DEFAULT 0;");
            logger.LogInformation("Added missing Role column to UserProfiles table.");
        }
    }

    private async Task CreateBaseTablesAsync()
    {
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
    }

    private async Task MigrateToV2Async()
    {
        await CreateBaseTablesAsync();

        var columns = await connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(UserAddresses);");
        var existingNames = new HashSet<string>(columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(nameof(UserAddress.Notes)))
            await connection.ExecuteAsync("ALTER TABLE UserAddresses ADD COLUMN Notes TEXT NOT NULL DEFAULT '';");

        if (!existingNames.Contains(nameof(UserAddress.Latitude)))
            await connection.ExecuteAsync("ALTER TABLE UserAddresses ADD COLUMN Latitude REAL NOT NULL DEFAULT 0;");

        if (!existingNames.Contains(nameof(UserAddress.Longitude)))
            await connection.ExecuteAsync("ALTER TABLE UserAddresses ADD COLUMN Longitude REAL NOT NULL DEFAULT 0;");
    }

    private sealed class TableColumnInfo
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
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

        var productsSeededFresh = false;
        if (await connection.Table<Product>().CountAsync() == 0)
        {
            productsSeededFresh = true;
            await connection.InsertAllAsync(new[]
            {
                new Product { Name = "Чеддерман", Description = "Бекон, сирний мікс, чеддер", Category = "Новинки", WeightGrams = 560, SizeName = "Середня", IsNew = true, BasePrice = 219m, Calories = 245, Proteins = 12m, Fats = 10m, Carbs = 27m },
                new Product { Name = "Діжон", Description = "Шинка, гриби, гірчичний соус", Category = "WOW піци", WeightGrams = 590, SizeName = "Середня", IsNew = true, BasePrice = 299m, Calories = 238, Proteins = 14m, Fats = 9m, Carbs = 26m },
                new Product { Name = "Маргарита", Description = "Класична піца з томатами та моцарелою", Category = "Standard", WeightGrams = 520, SizeName = "Середня", BasePrice = 259m, Calories = 220, Proteins = 10m, Fats = 8m, Carbs = 28m }
            });
        }
        else
        {
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

        var hadConfiguredCategories = await connection.Table<ProductCategory>().CountAsync() > 0;
        var hasAnyOptions = await connection.Table<IngredientOption>().CountAsync() > 0;

        if (productsSeededFresh || !hasAnyOptions)
        {
            var catalogProducts = await connection.Table<Product>().ToListAsync();
            foreach (var product in catalogProducts)
            {
                if (string.IsNullOrWhiteSpace(product.Category))
                {
                    product.Category = product.Name switch
                    {
                        "Чеддерман" => "Новинки",
                        "Діжон" => "WOW піци",
                        "Маргарита" => "Standard",
                        _ => "Standard"
                    };
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

        if (!hadConfiguredCategories)
        {
            await connection.InsertAllAsync(new[]
            {
                new ProductCategory { Name = "Новинки", SortOrder = 0 },
                new ProductCategory { Name = "WOW піци", SortOrder = 1 },
                new ProductCategory { Name = "Standard", SortOrder = 2 },
                new ProductCategory { Name = "Закуски", SortOrder = 3 },
                new ProductCategory { Name = "Соуси", SortOrder = 4 },
                new ProductCategory { Name = "Напої", SortOrder = 5 }
            });

            var existingProducts = await connection.Table<Product>().ToListAsync();
            if (!existingProducts.Any(p => p.Category is "Закуски" or "Соуси" or "Напої" or "Десерти"))
            {
                await connection.InsertAllAsync(new[]
                {
                    new Product { Name = "Картопля фрі", Description = "Хрустка золотиста картопля фрі з морською сіллю", Category = "Закуски", WeightGrams = 150, SizeName = "Стандарт", BasePrice = 59m, Calories = 312, Proteins = 3.5m, Fats = 15m, Carbs = 41m },
                    new Product { Name = "Курячі стріпси", Description = "Ніжне куряче філе у хрусткій паніровці", Category = "Закуски", WeightGrams = 180, SizeName = "Стандарт", BasePrice = 89m, Calories = 265, Proteins = 22m, Fats = 13m, Carbs = 15m },
                    new Product { Name = "Соус BBQ", Description = "Класичний димний соус барбекю до піци та закусок", Category = "Соуси", WeightGrams = 30, SizeName = "Порція", BasePrice = 15m, Calories = 172, Proteins = 1m, Fats = 0.5m, Carbs = 40m }
                });
            }

            if (await connection.Table<MenuBanner>().CountAsync() == 0)
            {
                await connection.InsertAllAsync(new[]
                {
                    new MenuBanner
                    {
                        Eyebrow = "НОВИНКА · 01–30.11.2026",
                        Title = "Чеддерман",
                        Subtitle = "Більше сиру. Більше смаку.",
                        BadgeText = "219 ₴",
                        BackgroundHex = "#2460A0",
                        TitleColorHex = "#FFD166",
                        BadgeColorHex = "#F4511E",
                        BadgeTextColorHex = "#FFFFFF",
                        SortOrder = 0,
                        IsActive = true
                    },
                    new MenuBanner
                    {
                        Eyebrow = "WOW ПІЦА · ХІТ СЕЗОНУ",
                        Title = "Діжон",
                        Subtitle = "Шинка, гриби та гірчичний соус.",
                        BadgeText = "299 ₴",
                        BackgroundHex = "#B45309",
                        TitleColorHex = "#FFFFFF",
                        BadgeColorHex = "#1C1917",
                        BadgeTextColorHex = "#FFD166",
                        SortOrder = 1,
                        IsActive = true
                    },
                    new MenuBanner
                    {
                        Eyebrow = "АКЦІЯ · ЩОДНЯ",
                        Title = "Доставка 0 ₴",
                        Subtitle = "Гаряча піца за 35 хв до ваших дверей.",
                        BadgeText = "Замовити",
                        BackgroundHex = "#166534",
                        TitleColorHex = "#FFFFFF",
                        BadgeColorHex = "#F7A600",
                        BadgeTextColorHex = "#FFFFFF",
                        SortOrder = 2,
                        IsActive = true
                    }
                });
            }
        }
    }
}
