using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel managing shopping cart items, upsell suggestions, totals, and checkout navigation.
/// </summary>
public sealed class CartViewModel : BaseViewModel
{
    private readonly ICartRepository cartRepository;
    private readonly IProductRepository productRepository;
    private readonly IAuthService authService;
    private readonly IAppStateService appState;
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
        IBugReportService? bugReportService = null,
        ILogger<CartViewModel>? logger = null)
    {
        this.cartRepository = cartRepository;
        this.productRepository = productRepository;
        this.authService = authService;
        this.appState = appState;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<CartViewModel>.Instance;
        Title = "Кошик";

        LoadCartCommand = new Command(async () => await LoadCartAsync());
        UpdateQtyCommand = new Command<CartItem?>(async item => await UpdateQtyAsync(item, 1));
        DecreaseQtyCommand = new Command<CartItem?>(async item => await UpdateQtyAsync(item, -1));
        RemoveItemCommand = new Command<CartItem?>(async item => await RemoveItemAsync(item));
        ClearCartCommand = new Command(async () => await ClearCartAsync(), () => Items.Count > 0);
        AddUpsellCommand = new Command<Product?>(async product => await AddUpsellAsync(product));
        ProceedCheckoutCommand = new Command(async () => await ProceedCheckoutAsync(), CanProceedCheckout);
    }

    public ObservableCollection<CartItem> Items { get; } = new();
    public ObservableCollection<Product> UpsellItems { get; } = new();

    public decimal Subtotal
    {
        get => subtotal;
        private set => SetProperty(ref subtotal, value);
    }

    public decimal DeliveryFee
    {
        get => deliveryFee;
        private set => SetProperty(ref deliveryFee, value);
    }

    public decimal Total
    {
        get => total;
        private set => SetProperty(ref total, value);
    }

    public bool IsEmpty => Items.Count == 0;
    public bool HasItems => Items.Count > 0;

    public ICommand LoadCartCommand { get; }
    public ICommand UpdateQtyCommand { get; }
    public ICommand DecreaseQtyCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ClearCartCommand { get; }
    public ICommand AddUpsellCommand { get; }
    public ICommand ProceedCheckoutCommand { get; }

    public async Task LoadCartAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var cartItems = await cartRepository.GetAllAsync();
            Items.Clear();
            foreach (var item in cartItems)
                Items.Add(item);

            await LoadUpsellItemsAsync();
            RecalculateTotals();
            logger.LogInformation("Cart loaded with {ItemCount} line(s), Total={Total}", Items.Count, Total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load shopping cart");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadUpsellItemsAsync()
    {
        if (UpsellItems.Count > 0)
            return;

        var allProducts = await productRepository.GetAllAsync();
        var upsellProducts = allProducts
            .Where(p => p.Category is "Закуски" or "Соуси" or "Напої" or "Десерти")
            .Take(5)
            .ToList();

        if (upsellProducts.Count == 0)
        {
            upsellProducts =
            [
                new Product { Id = -1, Name = "Картопля фрі", Category = "Закуски", WeightGrams = 150, SizeName = "Стандарт", BasePrice = 59m },
                new Product { Id = -2, Name = "Курячі стріпси", Category = "Закуски", WeightGrams = 180, SizeName = "Стандарт", BasePrice = 89m },
                new Product { Id = -3, Name = "Соус BBQ", Category = "Соуси", WeightGrams = 30, SizeName = "Порція", BasePrice = 15m }
            ];
        }

        UpsellItems.Clear();
        foreach (var product in upsellProducts)
            UpsellItems.Add(product);
    }

    private async Task UpdateQtyAsync(CartItem? item, int delta)
    {
        if (item is null)
            return;

        try
        {
            var nextQty = item.Quantity + delta;
            if (nextQty <= 0)
            {
                await RemoveItemAsync(item);
                return;
            }

            var updatedItem = new CartItem
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = nextQty,
                UnitPrice = item.UnitPrice,
                OptionsSummary = item.OptionsSummary
            };
            await cartRepository.SaveAsync(updatedItem);

            // RemoveAt/Insert with a new instance forces BindableLayout to rebind Quantity on POCO CartItem.
            var index = Items.IndexOf(item);
            if (index >= 0)
            {
                Items.RemoveAt(index);
                Items.Insert(index, updatedItem);
            }

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
        if (item is null)
            return;

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
        if (Items.Count == 0)
            return;

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
        if (product is null)
            return;

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

    private Task ShowCartErrorAsync(string message, string context)
    {
        if (bugReportService is not null)
            return bugReportService.ShowErrorWithReportOptionAsync("Помилка кошика", message, context);

        return Shell.Current is not null
            ? Shell.Current.DisplayAlertAsync("Помилка кошика", message, "OK")
            : Task.CompletedTask;
    }

    private bool CanProceedCheckout() => Items.Count > 0;

    private async Task ProceedCheckoutAsync()
    {
        if (!CanProceedCheckout())
            return;

        if (appState.SelectedPizzeria is null)
        {
            await Shell.Current.DisplayAlertAsync("Оберіть піцерію", "Перед оформленням замовлення оберіть піцерію.", "Обрати");
            var selectionRoute = appState.SelectedCity is null
                ? "cities?returnTo=cart"
                : "pizzerias?returnTo=cart";
            await Shell.Current.GoToAsync(selectionRoute);
            return;
        }

        var route = authService.IsAuthenticated
            ? "checkout"
            : "auth?next=checkout";
        await Shell.Current.GoToAsync(route);
    }

    private void RecalculateTotals()
    {
        Subtotal = Items.Sum(i => i.CalculateSubtotal());
        DeliveryFee = 0m;
        Total = Subtotal + DeliveryFee;

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasItems));
        ((Command)ClearCartCommand).ChangeCanExecute();
        ((Command)ProceedCheckoutCommand).ChangeCanExecute();
    }
}
