using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

public interface ICityRepository
{
    Task<List<City>> GetAllAsync();
    Task<List<City>> GetPopularAsync();
    Task<List<City>> SearchAsync(string query);
    Task SaveAsync(City city);
}

public interface IPizzeriaRepository
{
    Task<List<Pizzeria>> GetByCityIdAsync(int cityId);
    Task<Pizzeria?> GetByIdAsync(int id);
    Task SaveAsync(Pizzeria pizzeria);
}

public interface IProductRepository
{
    Task<List<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<List<Product>> GetByCategoryAsync(string category);
    Task<List<Product>> SearchByNameAsync(string query);
    Task<List<IngredientOption>> GetOptionsByProductIdAsync(int productId);
    Task SaveAsync(Product product);
    Task DeleteAsync(Product product);
}

public interface IFavoriteRepository
{
    Task<List<Product>> GetFavoriteProductsAsync();
    Task ToggleFavoriteAsync(int productId);
}

public sealed class CityRepository(SqliteDatabase database, ILogger<CityRepository>? logger = null) : ICityRepository
{
    private readonly ILogger<CityRepository> log = logger ?? NullLogger<CityRepository>.Instance;

    public async Task<List<City>> GetAllAsync()
    {
        await database.InitializeAsync();
        return await database.Connection.Table<City>().OrderBy(city => city.Name).ToListAsync();
    }

    public async Task<List<City>> GetPopularAsync()
    {
        await database.InitializeAsync();
        return await database.Connection.Table<City>().Where(city => city.IsPopular).OrderBy(city => city.Name).ToListAsync();
    }

    public async Task<List<City>> SearchAsync(string query)
    {
        var cities = await GetAllAsync();
        if (string.IsNullOrWhiteSpace(query))
            return cities;

        return cities
            .Where(city => city.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || city.Region.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    public async Task SaveAsync(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        await database.InitializeAsync();
        await database.Connection.InsertOrReplaceAsync(city);
        log.LogInformation("Saved city {CityName} (Id={CityId})", city.Name, city.Id);
    }
}

public sealed class PizzeriaRepository(SqliteDatabase database, ILogger<PizzeriaRepository>? logger = null) : IPizzeriaRepository
{
    private readonly ILogger<PizzeriaRepository> log = logger ?? NullLogger<PizzeriaRepository>.Instance;

    public async Task<List<Pizzeria>> GetByCityIdAsync(int cityId)
    {
        await database.InitializeAsync();
        return await database.Connection.Table<Pizzeria>()
            .Where(pizzeria => pizzeria.CityId == cityId)
            .OrderBy(pizzeria => pizzeria.StoreNumber)
            .ToListAsync();
    }

    public async Task<Pizzeria?> GetByIdAsync(int id)
    {
        await database.InitializeAsync();
        return await database.Connection.Table<Pizzeria>().Where(pizzeria => pizzeria.Id == id).FirstOrDefaultAsync();
    }

    public async Task SaveAsync(Pizzeria pizzeria)
    {
        ArgumentNullException.ThrowIfNull(pizzeria);
        await database.InitializeAsync();
        await database.Connection.InsertOrReplaceAsync(pizzeria);
        log.LogInformation("Saved pizzeria {StoreNumber} (Id={PizzeriaId})", pizzeria.StoreNumber, pizzeria.Id);
    }
}

public sealed class ProductRepository(SqliteDatabase database, ILogger<ProductRepository>? logger = null) : IProductRepository
{
    private readonly ILogger<ProductRepository> log = logger ?? NullLogger<ProductRepository>.Instance;

    public async Task<List<Product>> GetAllAsync()
    {
        await database.InitializeAsync();
        var items = await database.Connection.Table<Product>().OrderBy(product => product.Name).ToListAsync();
        log.LogDebug("Loaded {ProductCount} product(s) from catalog", items.Count);
        return items;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        await database.InitializeAsync();
        return await database.Connection.Table<Product>().Where(product => product.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<Product>> GetByCategoryAsync(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        await database.InitializeAsync();
        return await database.Connection.Table<Product>()
            .Where(product => product.Category == category)
            .OrderBy(product => product.Name)
            .ToListAsync();
    }

    public async Task<List<Product>> SearchByNameAsync(string query)
    {
        var products = await GetAllAsync();
        if (string.IsNullOrWhiteSpace(query))
            return products;

        return products
            .Where(product => product.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    public async Task<List<IngredientOption>> GetOptionsByProductIdAsync(int productId)
    {
        await database.InitializeAsync();
        return await database.Connection.Table<IngredientOption>()
            .Where(option => option.ProductId == productId)
            .OrderBy(option => option.Name)
            .ToListAsync();
    }

    public async Task SaveAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        await database.InitializeAsync();
        await database.Connection.InsertOrReplaceAsync(product);
        log.LogInformation("Saved catalog product {ProductName} (Id={ProductId})", product.Name, product.Id);
    }

    public async Task DeleteAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        await database.InitializeAsync();
        await database.Connection.DeleteAsync(product);
        log.LogInformation("Deleted catalog product {ProductName} (Id={ProductId})", product.Name, product.Id);
    }
}

public sealed class FavoriteRepository(SqliteDatabase database, ILogger<FavoriteRepository>? logger = null) : IFavoriteRepository
{
    private readonly ILogger<FavoriteRepository> log = logger ?? NullLogger<FavoriteRepository>.Instance;

    public async Task<List<Product>> GetFavoriteProductsAsync()
    {
        await database.InitializeAsync();
        var favorites = await database.Connection.Table<FavoriteProduct>().ToListAsync();
        if (favorites.Count == 0)
            return [];

        var favoriteIds = favorites.Select(favorite => favorite.ProductId).ToHashSet();
        var products = await database.Connection.Table<Product>().ToListAsync();
        return products.Where(product => favoriteIds.Contains(product.Id)).OrderBy(product => product.Name).ToList();
    }

    public async Task ToggleFavoriteAsync(int productId)
    {
        if (productId <= 0)
            throw new ArgumentOutOfRangeException(nameof(productId), "Product id must be positive.");

        await database.InitializeAsync();
        var productExists = await database.Connection.Table<Product>().Where(product => product.Id == productId).FirstOrDefaultAsync();
        if (productExists is null)
            throw new KeyNotFoundException($"Product with id {productId} was not found.");

        var existing = await database.Connection.Table<FavoriteProduct>()
            .Where(favorite => favorite.ProductId == productId)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            await database.Connection.InsertAsync(new FavoriteProduct { ProductId = productId });
            log.LogInformation("Added product {ProductId} to favorites", productId);
        }
        else
        {
            await database.Connection.DeleteAsync(existing);
            log.LogInformation("Removed product {ProductId} from favorites", productId);
        }
    }
}
