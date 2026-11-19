using Microsoft.Extensions.DependencyInjection;
using PizzaTake.Views;

namespace PizzaTake;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, true);
        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Меню", "menu", "nav_menu.svg", services.GetRequiredService<MenuPage>()));
        tabs.Items.Add(CreateTab("Обране", "favorites", "nav_favorites.svg", services.GetRequiredService<FavoritesPage>()));
        tabs.Items.Add(CreateTab("Кошик", "cart", "nav_cart.svg", services.GetRequiredService<CartPage>()));
        tabs.Items.Add(CreateTab("Історія", "history", "nav_history.svg", services.GetRequiredService<OrderHistoryPage>()));
        tabs.Items.Add(CreateTab("Налаштування", "settings", "nav_settings.svg", services.GetRequiredService<SettingsPage>()));
        Items.Add(tabs);

        Routing.RegisterRoute("welcome", typeof(WelcomePage));
        Routing.RegisterRoute("cities", typeof(CitySelectionPage));
        Routing.RegisterRoute("pizzerias", typeof(PizzeriaSelectionPage));
        Routing.RegisterRoute("pizzadetail", typeof(PizzaDetailPage));
        Routing.RegisterRoute("auth", typeof(AuthPage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("orderdetail", typeof(OrderDetailPage));
        Routing.RegisterRoute("addressedit", typeof(AddressEditPage));
    }

    private static ShellContent CreateTab(string title, string route, string icon, Page page) => new()
    {
        Title = title,
        Route = route,
        Icon = icon,
        Content = page
    };
}
