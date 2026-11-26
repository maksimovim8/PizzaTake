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

public sealed class MenuViewModel : BaseViewModel
{
    private readonly IProductRepository products;
    private readonly ICartRepository cart;
    private readonly IOrderRepository orders;
    private readonly IAppStateService appState;
    private readonly ILogger<MenuViewModel> logger;
    private string searchText = string.Empty;
    private string? selectedCategory;
    private string statusMessage = string.Empty;
    private string selectedPizzeriaText = "Оберіть піцерію";
    private Order? activeOrder;

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public Order? ActiveOrder
    {
        get => activeOrder;
        private set
        {
            if (SetProperty(ref activeOrder, value))
                OnPropertyChanged(nameof(HasActiveOrder));
        }
    }
    public bool HasActiveOrder => ActiveOrder is not null;
    public string SelectedPizzeriaText { get => selectedPizzeriaText; private set => SetProperty(ref selectedPizzeriaText, value); }
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
    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) _ = ApplyFilterAsync(); }
    }
    public string? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (SetProperty(ref selectedCategory, value))
                _ = ApplyFilterAsync();
        }
    }

    public ICommand LoadCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand QuickAddCommand { get; }
    public ICommand OpenProductCommand { get; }
    public ICommand OpenPizzeriaSelectorCommand { get; }
    public ICommand OpenActiveOrderCommand { get; }

    public MenuViewModel(
        IProductRepository products,
        ICartRepository cart,
        IOrderRepository orders,
        IAppStateService appState,
        ILogger<MenuViewModel>? logger = null)
    {
        this.products = products;
        this.cart = cart;
        this.orders = orders;
        this.appState = appState;
        this.logger = logger ?? NullLogger<MenuViewModel>.Instance;
        LoadCommand = new Command(async () => await LoadAsync());
        SelectCategoryCommand = new Command<string?>(category =>
        {
            if (!string.IsNullOrWhiteSpace(category))
                SelectedCategory = category;
        });
        QuickAddCommand = new Command<Product>(async product => await QuickAddAsync(product));
        OpenProductCommand = new Command<Product>(async product =>
        {
            if (product is not null)
                await Shell.Current.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
        OpenPizzeriaSelectorCommand = new Command(async () => await OpenPizzeriaSelectorAsync());
        OpenActiveOrderCommand = new Command(async () => await OpenActiveOrderAsync());
    }

    private async Task OpenActiveOrderAsync()
    {
        if (ActiveOrder is null)
            return;

        logger.LogInformation("Opening active order {OrderNumber} from MenuPage banner", ActiveOrder.OrderNumber);
        await Shell.Current.GoToAsync("orderdetail", new Dictionary<string, object>
        {
            ["Order"] = ActiveOrder
        });
    }

    private async Task OpenPizzeriaSelectorAsync()
    {
        appState.SelectedDeliveryType = DeliveryType.Pickup;
        appState.SaveState();
        var route = appState.SelectedCity is null ? "cities?returnTo=menu" : "pizzerias?returnTo=menu";
        await Shell.Current.GoToAsync(route);
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            SelectedPizzeriaText = appState.SelectedPizzeria?.StoreNumber ?? "Оберіть піцерію";
            ActiveOrder = await orders.GetActiveAsync();
            var all = await products.GetAllAsync();
            Categories.Clear();
            Categories.Add("Усі категорії");
            foreach (var category in all.Select(product => product.Category).Where(category => !string.IsNullOrWhiteSpace(category)).Distinct().OrderBy(category => category))
                Categories.Add(category);
            if (string.IsNullOrWhiteSpace(selectedCategory))
            {
                selectedCategory = "Усі категорії";
                OnPropertyChanged(nameof(SelectedCategory));
            }
            await ShowProductsAsync(Filter(all));
            logger.LogInformation("Loaded menu catalog with {ProductCount} product(s)", all.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load menu catalog");
            StatusMessage = "Не вдалося завантажити меню.";
        }
        finally { IsBusy = false; }
    }

    private async Task ApplyFilterAsync()
    {
        var all = await products.GetAllAsync();
        await ShowProductsAsync(Filter(all));
    }

    private IEnumerable<Product> Filter(IEnumerable<Product> all)
    {
        var query = all;
        if (!string.IsNullOrWhiteSpace(SelectedCategory) &&
            !string.Equals(SelectedCategory, "Усі категорії", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(product => string.Equals(product.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(product => product.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return query;
    }

    private Task ShowProductsAsync(IEnumerable<Product> values)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            Products.Clear();
            string? previousCategory = null;
            foreach (var product in values.OrderBy(CategorySortOrder).ThenBy(product => product.Name))
            {
                product.ShowCategoryHeader = product.Category != previousCategory;
                Products.Add(product);
                previousCategory = product.Category;
            }
            StatusMessage = Products.Count == 0 ? "Нічого не знайдено." : string.Empty;
        });
    }

    private static int CategorySortOrder(Product product) => product.Category switch
    {
        "Новинки" => 0,
        "WOW піци" => 1,
        "Standard" => 2,
        _ => product.IsNew ? 0 : 3
    };

    private async Task QuickAddAsync(Product? product)
    {
        if (product is null) return;
        await cart.SaveAsync(new CartItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = product.GetEffectivePrice()
        });
        logger.LogInformation("Quick-added product {ProductName} (Id={ProductId}) to cart", product.Name, product.Id);
        StatusMessage = $"{product.Name} додано до кошика.";
    }
}
