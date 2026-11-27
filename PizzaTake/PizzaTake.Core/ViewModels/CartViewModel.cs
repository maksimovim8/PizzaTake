using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel managing shopping cart items, upsell suggestions, totals, and checkout navigation.
/// </summary>
public sealed class CartViewModel : ConnectivityBannerViewModel
{
    private readonly ICartRepository cartRepository;
    private readonly IProductRepository productRepository;
    private readonly IAuthService authService;
    private readonly IAppStateService appState;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<CartViewModel> logger;

    private decimal subtotal;
    private decimal deliveryFee;
    private decimal total;

    public CartViewModel(
        ICartRepository cartRepository,
        IProductRepository productRepository,
        IAuthService authService,
        IAppStateService appState,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher? dispatcher = null,
        IConnectivityService? connectivityService = null,
        ISyncService? syncService = null,
        IBugReportService? bugReportService = null,
        ILogger<CartViewModel>? logger = null)
        : base(connectivityService, syncService, dispatcher, logger ?? NullLogger<CartViewModel>.Instance)
    {
        this.cartRepository = cartRepository;
        this.productRepository = productRepository;
        this.authService = authService;
        this.appState = appState;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<CartViewModel>.Instance;
        Title = "Кошик";

        LoadCartCommand = new AsyncRelayCommand(LoadCartAsync);
        UpdateQtyCommand = new AsyncRelayCommand<CartItem?>(item => UpdateQtyAsync(item, 1));
        DecreaseQtyCommand = new AsyncRelayCommand<CartItem?>(item => UpdateQtyAsync(item, -1));
        RemoveItemCommand = new AsyncRelayCommand<CartItem?>(RemoveItemAsync);
        ClearCartCommand = new AsyncRelayCommand(ClearCartAsync, () => Items.Count > 0);
        AddUpsellCommand = new AsyncRelayCommand<Product?>(AddUpsellAsync);
        ProceedCheckoutCommand = new AsyncRelayCommand(ProceedCheckoutAsync, () => Items.Count > 0);
    }

    public ObservableCollection<CartItem> Items { get; } = [];
    public ObservableCollection<Product> UpsellItems { get; } = [];

    public decimal Subtotal { get => subtotal; private set => SetProperty(ref subtotal, value); }
    public decimal DeliveryFee { get => deliveryFee; private set => SetProperty(ref deliveryFee, value); }
    public decimal Total { get => total; private set => SetProperty(ref total, value); }
    public bool IsEmpty => Items.Count == 0;
    public bool HasItems => Items.Count > 0;

    public IAsyncRelayCommand LoadCartCommand { get; }
    public IAsyncRelayCommand<CartItem?> UpdateQtyCommand { get; }
    public IAsyncRelayCommand<CartItem?> DecreaseQtyCommand { get; }
    public IAsyncRelayCommand<CartItem?> RemoveItemCommand { get; }
    public IAsyncRelayCommand ClearCartCommand { get; }
    public IAsyncRelayCommand<Product?> AddUpsellCommand { get; }
    public IAsyncRelayCommand ProceedCheckoutCommand { get; }

    public async Task LoadCartAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            ReplaceCollection(Items, await cartRepository.GetAllAsync());
            await LoadUpsellItemsAsync();
            RecalculateTotals();
            logger.LogInformation("Cart loaded with {ItemCount} line(s), Total={Total}", Items.Count, Total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load shopping cart");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadUpsellItemsAsync()
    {
        var allProducts = await productRepository.GetAllAsync();
        var upsells = allProducts
            .Where(p => p.Category is "Закуски" or "Соуси" or "Напої" or "Десерти")
            .Take(6)
            .ToList();

        if (upsells.Count == 0)
        {
            upsells =
            [
                new Product { Id = -1, Name = "Картопля фрі", Category = "Закуски", WeightGrams = 150, SizeName = "Стандарт", BasePrice = 59m },
                new Product { Id = -2, Name = "Курячі стріпси", Category = "Закуски", WeightGrams = 180, SizeName = "Стандарт", BasePrice = 89m },
                new Product { Id = -3, Name = "Соус BBQ", Category = "Соуси", WeightGrams = 30, SizeName = "Порція", BasePrice = 15m }
            ];
        }

        ReplaceCollection(UpsellItems, upsells);
    }

    private async Task UpdateQtyAsync(CartItem? item, int delta)
    {
        if (item is null) return;
        try
        {
            var nextQty = item.Quantity + delta;
            if (nextQty <= 0)
            {
                await RemoveItemAsync(item);
                return;
            }

            var updated = new CartItem
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = nextQty,
                UnitPrice = item.UnitPrice,
                OptionsSummary = item.OptionsSummary
            };
            await cartRepository.SaveAsync(updated);

            var index = Items.IndexOf(item);
            if (index >= 0)
                Items[index] = updated;

            RecalculateTotals();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update quantity for cart item {ProductName}", item.ProductName);
            await ShowCartErrorAsync("Не вдалося оновити кількість товару.", "Cart: UpdateQtyAsync");
        }
    }

    private async Task RemoveItemAsync(CartItem? item)
    {
        if (item is null) return;
        try
        {
            await cartRepository.DeleteAsync(item);
            Items.Remove(item);
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove cart item {ProductName}", item.ProductName);
            await ShowCartErrorAsync("Не вдалося видалити товар із кошика.", "Cart: RemoveItemAsync");
        }
    }

    private async Task ClearCartAsync()
    {
        if (Items.Count == 0) return;
        try
        {
            await cartRepository.ClearAsync();
            Items.Clear();
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear shopping cart");
            await ShowCartErrorAsync("Не вдалося очистити кошик.", "Cart: ClearCartAsync");
        }
    }

    private async Task AddUpsellAsync(Product? product)
    {
        if (product is null) return;
        try
        {
            var existing = Items.FirstOrDefault(i => i.ProductId == product.Id && i.ProductName == product.Name);
            if (existing is not null)
            {
                await UpdateQtyAsync(existing, 1);
                return;
            }

            var newItem = new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = 1,
                UnitPrice = product.GetEffectivePrice(),
                OptionsSummary = string.IsNullOrWhiteSpace(product.SizeName) ? "Стандарт" : product.SizeName
            };
            await cartRepository.SaveAsync(newItem);
            Items.Add(newItem);
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add upsell product {ProductName} to cart", product.Name);
            await ShowCartErrorAsync("Не вдалося додати товар до кошика.", "Cart: AddUpsellAsync");
        }
    }

    private Task ShowCartErrorAsync(string message, string context) =>
        bugReportService is not null
            ? bugReportService.ShowErrorWithReportOptionAsync("Помилка кошика", message, context)
            : dialogService.AlertAsync("Помилка кошика", message);

    private async Task ProceedCheckoutAsync()
    {
        if (Items.Count == 0) return;

        if (appState.SelectedPizzeria is null)
        {
            await dialogService.AlertAsync("Оберіть піцерію", "Перед оформленням замовлення оберіть піцерію.", "Обрати");
            await navigation.GoToAsync(appState.SelectedCity is null ? "cities?returnTo=cart" : "pizzerias?returnTo=cart");
            return;
        }

        await navigation.GoToAsync(authService.IsAuthenticated ? "checkout" : "auth?next=checkout");
    }

    private void RecalculateTotals()
    {
        Subtotal = Items.Sum(i => i.CalculateSubtotal());
        DeliveryFee = 0m;
        Total = Subtotal + DeliveryFee;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasItems));
        ClearCartCommand.NotifyCanExecuteChanged();
        ProceedCheckoutCommand.NotifyCanExecuteChanged();
    }
}
