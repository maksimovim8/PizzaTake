using System.Collections.ObjectModel;
using System.Windows.Input;
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
    private readonly IAuthService authService;
    private readonly SqliteDatabase database;

    private decimal subtotal;
    private decimal deliveryFee;
    private decimal total;

    public CartViewModel(
        ICartRepository cartRepository,
        IAuthService authService,
        SqliteDatabase database)
    {
        this.cartRepository = cartRepository;
        this.authService = authService;
        this.database = database;
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
            await database.InitializeAsync();

            var cartItems = await cartRepository.GetAllAsync();
            Items.Clear();
            foreach (var item in cartItems)
                Items.Add(item);

            await LoadUpsellItemsAsync();
            RecalculateTotals();
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

        var dbProducts = await database.Connection.Table<Product>()
            .Where(p => p.Category != "Піца")
            .Take(5)
            .ToListAsync();

        if (dbProducts.Count == 0)
        {
            dbProducts =
            [
                new Product { Id = -1, Name = "Картопля фрі", Category = "Закуски", WeightGrams = 150, SizeName = "Стандарт", BasePrice = 59m },
                new Product { Id = -2, Name = "Курячі стріпси", Category = "Закуски", WeightGrams = 180, SizeName = "Стандарт", BasePrice = 89m },
                new Product { Id = -3, Name = "Соус BBQ", Category = "Соуси", WeightGrams = 30, SizeName = "Порція", BasePrice = 15m }
            ];
        }

        UpsellItems.Clear();
        foreach (var product in dbProducts)
            UpsellItems.Add(product);
    }

    private async Task UpdateQtyAsync(CartItem? item, int delta)
    {
        if (item is null)
            return;

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

    private async Task RemoveItemAsync(CartItem? item)
    {
        if (item is null)
            return;

        await cartRepository.DeleteAsync(item);
        Items.Remove(item);
        RecalculateTotals();
    }

    private async Task ClearCartAsync()
    {
        if (Items.Count == 0)
            return;

        await cartRepository.ClearAsync();
        Items.Clear();
        RecalculateTotals();
    }

    private async Task AddUpsellAsync(Product? product)
    {
        if (product is null)
            return;

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

    private bool CanProceedCheckout() => Items.Count > 0;

    private async Task ProceedCheckoutAsync()
    {
        if (!CanProceedCheckout())
            return;

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
