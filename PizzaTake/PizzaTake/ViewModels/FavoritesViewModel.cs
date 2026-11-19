using System.Collections.ObjectModel;
using System.Windows.Input;
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
    public ObservableCollection<Product> Favorites { get; } = [];
    public ICommand LoadCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand AddToCartCommand { get; }
    public ICommand OpenProductCommand { get; }

    public FavoritesViewModel(IFavoriteRepository favorites, ICartRepository cart)
    {
        this.favorites = favorites;
        this.cart = cart;
        LoadCommand = new Command(async () => await LoadAsync());
        ToggleFavoriteCommand = new Command<Product>(async product =>
        {
            if (product is null) return;
            await favorites.ToggleFavoriteAsync(product.Id);
            await LoadAsync();
        });
        AddToCartCommand = new Command<Product>(async product =>
        {
            if (product is null) return;
            await cart.SaveAsync(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = 1,
                UnitPrice = product.GetEffectivePrice()
            });
        });
        OpenProductCommand = new Command<Product>(async product =>
        {
            if (product is not null)
                await Shell.Current.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
    }

    private async Task LoadAsync()
    {
        var items = await favorites.GetFavoriteProductsAsync();
        Favorites.Clear();
        foreach (var product in items) Favorites.Add(product);
    }
}
