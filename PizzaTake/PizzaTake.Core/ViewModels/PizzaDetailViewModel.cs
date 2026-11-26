using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class PizzaDetailViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IProductRepository products;
    private readonly ICartRepository cart;
    private readonly IFavoriteRepository favorites;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly ILogger<PizzaDetailViewModel> logger;
    private readonly ICrashReportingService? crashReporting;
    private readonly IBugReportService? bugReportService;

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

    public decimal CalculatedPrice
    {
        get => calculatedPrice;
        private set => SetProperty(ref calculatedPrice, value);
    }

    public string KbjuSummary => Product is null
        ? string.Empty
        : $"Ккал: {Product.Calories} · Б: {Product.Proteins:0.#} г · Ж: {Product.Fats:0.#} г · В: {Product.Carbs:0.#} г";

    public string SelectedOptionsSummary => string.Join(", ", AddOns.Concat(Exclusions).Concat(Replacements)
        .Where(option => selectedOptionIds.Contains(option.Id)).Select(option => option.Name));

    public bool IsKbjuVisible
    {
        get => isKbjuVisible;
        set => SetProperty(ref isKbjuVisible, value);
    }

    public bool IsFavorite
    {
        get => isFavorite;
        private set
        {
            if (SetProperty(ref isFavorite, value))
                OnPropertyChanged(nameof(FavoriteButtonText));
        }
    }

    public string FavoriteButtonText => IsFavorite ? "♥" : "♡";

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<IngredientOption> ToggleOptionCommand { get; }
    public IRelayCommand ToggleKbjuCommand { get; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; }
    public IAsyncRelayCommand AddToCartCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public PizzaDetailViewModel(
        IProductRepository products,
        ICartRepository cart,
        IFavoriteRepository favorites,
        INavigationService navigation,
        IDialogService dialogService,
        ILogger<PizzaDetailViewModel>? logger = null,
        ICrashReportingService? crashReporting = null,
        IBugReportService? bugReportService = null)
    {
        this.products = products;
        this.cart = cart;
        this.favorites = favorites;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.logger = logger ?? NullLogger<PizzaDetailViewModel>.Instance;
        this.crashReporting = crashReporting;
        this.bugReportService = bugReportService;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        ToggleOptionCommand = new RelayCommand<IngredientOption>(ToggleOption);
        ToggleKbjuCommand = new RelayCommand(() => IsKbjuVisible = !IsKbjuVisible);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
        AddToCartCommand = new AsyncRelayCommand(AddToCartAsync, () => Product is not null);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
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
        {
            _ = LoadProductAsync(id);
        }
    }

    public HashSet<int> SelectedOptionIds => selectedOptionIds;

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
                await dialogService.AlertAsync("Товар не знайдено", "Запитану піцу не знайдено в каталозі.");
                await navigation.GoBackAsync();
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
        AddToCartCommand.NotifyCanExecuteChanged();
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
        try
        {
            await favorites.ToggleFavoriteAsync(Product.Id);
            IsFavorite = !IsFavorite;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle favorite for product Id={ProductId}", Product.Id);
            if (bugReportService is not null)
                await bugReportService.ShowErrorWithReportOptionAsync(
                    "Помилка",
                    "Не вдалося оновити обране. Спробуйте ще раз.",
                    "PizzaDetail: ToggleFavoriteAsync");
        }
    }

    private async Task LoadOptionsAsync()
    {
        if (Product is null) return;
        var options = await products.GetOptionsByProductIdAsync(Product.Id);
        AddOns.Clear();
        Exclusions.Clear();
        Replacements.Clear();
        foreach (var option in options)
        {
            option.IsSelected = selectedOptionIds.Contains(option.Id);
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
        var added = selectedOptionIds.Add(option.Id);
        if (!added) selectedOptionIds.Remove(option.Id);
        option.IsSelected = added;
        OnPropertyChanged(nameof(SelectedOptionIds));
        OnPropertyChanged(nameof(SelectedOptionsSummary));
        RecalculatePrice();
    }

    private void RecalculatePrice()
    {
        if (Product is null) return;
        var selected = AddOns.Concat(Exclusions).Concat(Replacements).Where(option => selectedOptionIds.Contains(option.Id));
        CalculatedPrice = Product.GetEffectivePrice() + selected.Sum(option => option.Price);
    }

    private async Task AddToCartAsync()
    {
        if (Product is null) return;
        try
        {
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
            await navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add product {ProductName} to cart", Product.Name);
            if (bugReportService is not null)
                await bugReportService.ShowErrorWithReportOptionAsync(
                    "Помилка кошика",
                    "Не вдалося додати піцу до кошика. Спробуйте ще раз.",
                    "PizzaDetail: AddToCartAsync");
        }
    }
}
