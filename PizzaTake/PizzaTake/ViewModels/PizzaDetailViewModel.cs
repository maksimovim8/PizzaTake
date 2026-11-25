using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Controls;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class PizzaDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IProductRepository products;
    private readonly ICartRepository cart;
    private readonly IFavoriteRepository favorites;
    private readonly ILogger<PizzaDetailViewModel> logger;
    private readonly ICrashReportingService? crashReporting;
    private Product? product;
    private decimal calculatedPrice;
    private bool isKbjuVisible;
    private bool isFavorite;
    private readonly HashSet<int> selectedOptionIds = [];

    public Product? Product
    {
        get => product;
        private set
        {
            if (SetProperty(ref product, value))
            {
                OnPropertyChanged(nameof(KbjuSummary));
                OnPropertyChanged(nameof(IsFavorite));
                OnPropertyChanged(nameof(FavoriteButtonText));
            }
        }
    }
    public ObservableCollection<IngredientOption> AddOns { get; } = [];
    public ObservableCollection<IngredientOption> Exclusions { get; } = [];
    public ObservableCollection<IngredientOption> Replacements { get; } = [];
    public decimal CalculatedPrice { get => calculatedPrice; private set => SetProperty(ref calculatedPrice, value); }
    public string KbjuSummary => Product is null
        ? string.Empty
        : $"Ккал: {Product.Calories} · Б: {Product.Proteins:0.#} г · Ж: {Product.Fats:0.#} г · В: {Product.Carbs:0.#} г";
    public string SelectedOptionsSummary => string.Join(", ", AddOns.Concat(Exclusions).Concat(Replacements)
        .Where(option => selectedOptionIds.Contains(option.Id)).Select(option => option.Name));
    public bool IsKbjuVisible { get => isKbjuVisible; set => SetProperty(ref isKbjuVisible, value); }
    public bool IsFavorite
    {
        get => isFavorite;
        private set
        {
            if (SetProperty(ref isFavorite, value)) OnPropertyChanged(nameof(FavoriteButtonText));
        }
    }
    public string FavoriteButtonText => IsFavorite ? "♥" : "♡";
    public ICommand LoadCommand { get; }
    public ICommand ToggleOptionCommand { get; }
    public ICommand ToggleKbjuCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToCartCommand { get; }
    public ICommand GoBackCommand { get; }

    public PizzaDetailViewModel(
        IProductRepository products,
        ICartRepository cart,
        IFavoriteRepository favorites,
        ILogger<PizzaDetailViewModel>? logger = null,
        ICrashReportingService? crashReporting = null)
    {
        this.products = products;
        this.cart = cart;
        this.favorites = favorites;
        this.logger = logger ?? NullLogger<PizzaDetailViewModel>.Instance;
        this.crashReporting = crashReporting;
        LoadCommand = new Command(async () => await LoadAsync());
        ToggleOptionCommand = new Command<IngredientOption>(ToggleOption);
        ToggleKbjuCommand = new Command(() => IsKbjuVisible = !IsKbjuVisible);
        ToggleFavoriteCommand = new Command(async () => await ToggleFavoriteAsync());
        AddToCartCommand = new Command(async () => await AddToCartAsync(), () => Product is not null);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Product", out var value) && value is Product selectedProduct)
        {
            Product = selectedProduct;
            PrepareProduct();
            _ = LoadOptionsAsync();
            _ = RefreshFavoriteAsync();
        }
        else if (query.TryGetValue("id", out value) && int.TryParse(value?.ToString(), out var id))
            _ = LoadProductAsync(id);
    }

    private async Task LoadAsync()
    {
        if (Product is null) return;
        await LoadOptionsAsync();
        await RefreshFavoriteAsync();
    }

    private async Task LoadProductAsync(int id)
    {
        try
        {
            Product = await products.GetByIdAsync(id);
            if (Product is not null)
            {
                PrepareProduct();
                await LoadOptionsAsync();
                await RefreshFavoriteAsync();
            }
            else
            {
                logger.LogWarning("Product with id {ProductId} was not found", id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load product {ProductId}", id);
        }
    }

    private void PrepareProduct()
    {
        if (Product is null) return;
        selectedOptionIds.Clear();
        CalculatedPrice = Product.GetEffectivePrice();
        ((Command)AddToCartCommand).ChangeCanExecute();
        logger.LogInformation("Opened product {ProductName} (Id={ProductId})", Product.Name, Product.Id);
        crashReporting?.TrackEvent(CrashEventNames.ProductOpened, new Dictionary<string, string>
        {
            ["product_id"] = Product.Id.ToString(),
            ["product_name"] = Product.Name,
            ["category"] = Product.Category
        });
    }

    private async Task RefreshFavoriteAsync()
    {
        if (Product is null) return;
        var favoriteProducts = await favorites.GetFavoriteProductsAsync();
        IsFavorite = favoriteProducts.Any(item => item.Id == Product.Id);
    }

    private async Task ToggleFavoriteAsync()
    {
        if (Product is null) return;
        await favorites.ToggleFavoriteAsync(Product.Id);
        IsFavorite = !IsFavorite;
    }

    private async Task LoadOptionsAsync()
    {
        if (Product is null) return;
        var options = await products.GetOptionsByProductIdAsync(Product.Id);
        AddOns.Clear(); Exclusions.Clear(); Replacements.Clear();
        foreach (var option in options)
        {
            switch (option.Kind)
            {
                case OptionKind.AddOn: AddOns.Add(option); break;
                case OptionKind.Exclusion: Exclusions.Add(option); break;
                case OptionKind.Replacement: Replacements.Add(option); break;
            }
        }
        RecalculatePrice();
    }

    private void ToggleOption(IngredientOption? option)
    {
        if (option is null) return;
        if (!selectedOptionIds.Add(option.Id)) selectedOptionIds.Remove(option.Id);
        OnPropertyChanged(nameof(SelectedOptionIds));
        OnPropertyChanged(nameof(SelectedOptionsSummary));
        RecalculatePrice();
    }

    public HashSet<int> SelectedOptionIds => selectedOptionIds;

    private void RecalculatePrice()
    {
        if (Product is null) return;
        var selected = AddOns.Concat(Exclusions).Concat(Replacements).Where(option => selectedOptionIds.Contains(option.Id));
        CalculatedPrice = Product.GetEffectivePrice() + selected.Sum(option => option.Price);
    }

    private async Task AddToCartAsync()
    {
        if (Product is null) return;
        var selected = AddOns.Concat(Exclusions).Concat(Replacements).Where(option => selectedOptionIds.Contains(option.Id)).ToList();
        await cart.SaveAsync(new CartItem
        {
            ProductId = Product.Id,
            ProductName = Product.Name,
            Quantity = 1,
            UnitPrice = CalculatedPrice,
            OptionsSummary = string.Join(", ", selected.Select(option => option.Name))
        });
        logger.LogInformation("Added configured product {ProductName} (Price={Price}) to cart", Product.Name, CalculatedPrice);
        await Shell.Current.GoToAsync("..");
    }
}
