using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class FavoritesViewModel : BaseViewModel
{
    private readonly IFavoriteRepository favorites;
    private readonly ICartRepository cart;
    private readonly INavigationService navigation;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<FavoritesViewModel> logger;
    private string statusMessage = string.Empty;

    public ObservableCollection<Product> Favorites { get; } = [];

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand<Product> ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand<Product> AddToCartCommand { get; }
    public IAsyncRelayCommand<Product> OpenProductCommand { get; }

    public FavoritesViewModel(
        IFavoriteRepository favorites,
        ICartRepository cart,
        INavigationService navigation,
        IBugReportService? bugReportService = null,
        ILogger<FavoritesViewModel>? logger = null)
    {
        this.favorites = favorites;
        this.cart = cart;
        this.navigation = navigation;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<FavoritesViewModel>.Instance;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand<Product>(async product =>
        {
            if (product is null) return;
            try
            {
                await this.favorites.ToggleFavoriteAsync(product.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to toggle favorite for product Id={ProductId}", product.Id);
                if (this.bugReportService is not null)
                    await this.bugReportService.ShowErrorWithReportOptionAsync(
                        "Помилка",
                        "Не вдалося оновити список обраного.",
                        "Favorites: ToggleFavoriteCommand");
            }
        });
        AddToCartCommand = new AsyncRelayCommand<Product>(async product =>
        {
            if (product is null) return;
            try
            {
                await this.cart.SaveAsync(new CartItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = 1,
                    UnitPrice = product.GetEffectivePrice()
                });
                StatusMessage = $"{product.Name} додано до кошика.";
                this.logger.LogInformation("Added favorite product {ProductName} to cart", product.Name);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to add favorite product {ProductName} to cart", product.Name);
                if (this.bugReportService is not null)
                    await this.bugReportService.ShowErrorWithReportOptionAsync(
                        "Помилка",
                        "Не вдалося додати товар до кошика.",
                        "Favorites: AddToCartCommand");
            }
        });
        OpenProductCommand = new AsyncRelayCommand<Product>(async product =>
        {
            if (product is not null)
                await this.navigation.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
    }

    public async Task LoadAsync()
    {
        try
        {
            StatusMessage = string.Empty;
            var items = await favorites.GetFavoriteProductsAsync();
            Favorites.Clear();
            foreach (var product in items)
                Favorites.Add(product);
            logger.LogDebug("Loaded {FavoriteCount} favorite product(s)", Favorites.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load favorite products");
        }
    }
}
