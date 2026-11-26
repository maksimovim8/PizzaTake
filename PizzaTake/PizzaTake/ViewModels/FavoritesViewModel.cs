using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class FavoritesViewModel : BaseViewModel
{
    private readonly IFavoriteRepository favorites;
    private readonly ICartRepository cart;
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
    public ICommand LoadCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToCartCommand { get; }
    public ICommand OpenProductCommand { get; }

    public FavoritesViewModel(
        IFavoriteRepository favorites,
        ICartRepository cart,
        IBugReportService? bugReportService = null,
        ILogger<FavoritesViewModel>? logger = null)
    {
        this.favorites = favorites;
        this.cart = cart;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<FavoritesViewModel>.Instance;
        LoadCommand = new Command(async () => await LoadAsync());
        ToggleFavoriteCommand = new Command<Product>(async product =>
        {
            if (product is null) return;
            try
            {
                await favorites.ToggleFavoriteAsync(product.Id);
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
        AddToCartCommand = new Command<Product>(async product =>
        {
            if (product is null) return;
            try
            {
                await cart.SaveAsync(new CartItem
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
        OpenProductCommand = new Command<Product>(async product =>
        {
            if (product is not null)
                await Shell.Current.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
    }

    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = string.Empty;
            var items = await favorites.GetFavoriteProductsAsync();
            Favorites.Clear();
            foreach (var product in items) Favorites.Add(product);
            logger.LogDebug("Loaded {FavoriteCount} favorite product(s)", Favorites.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load favorite products");
        }
    }
}
