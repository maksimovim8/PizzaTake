using System.Collections.ObjectModel;
using System.Windows.Input;
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
    private string searchText = string.Empty;
    private string? selectedCategory;
    private string statusMessage = string.Empty;
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
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) _ = ApplyFilterAsync(); }
    }
    public string? SelectedCategory
    {
        get => selectedCategory;
        set => SetProperty(ref selectedCategory, value);
    }

    public ICommand LoadCommand { get; }
    public ICommand QuickAddCommand { get; }
    public ICommand OpenProductCommand { get; }

    public MenuViewModel(IProductRepository products, ICartRepository cart, IOrderRepository orders)
    {
        this.products = products;
        this.cart = cart;
        this.orders = orders;
        LoadCommand = new Command(async () => await LoadAsync());
        QuickAddCommand = new Command<Product>(async product => await QuickAddAsync(product));
        OpenProductCommand = new Command<Product>(async product =>
        {
            if (product is not null)
                await Shell.Current.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            ActiveOrder = await orders.GetActiveAsync();
            var all = await products.GetAllAsync();
            Categories.Clear();
            Categories.Add("Усі категорії");
            foreach (var category in all.Select(product => product.Category).Where(category => !string.IsNullOrWhiteSpace(category)).Distinct().OrderBy(category => category))
                Categories.Add(category);
            SelectedCategory ??= "Новинки";
            await ShowProductsAsync(Filter(all));
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
        StatusMessage = $"{product.Name} додано до кошика.";
    }
}
