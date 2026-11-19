using Microsoft.Extensions.DependencyInjection;
using PizzaTake.Services;
using PizzaTake.Views;

namespace PizzaTake;

public partial class AppShell : Shell
{
    private readonly IAppStateService appState;
    private readonly ShellContent favoritesTab;
    private readonly ShellContent cartTab;
    private readonly ShellContent historyTab;
    private readonly ShellContent settingsTab;

    public AppShell(IServiceProvider services, IAuthService authService, IAppStateService appState)
    {
        this.appState = appState;
        InitializeComponent();

        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, true);

        TabBar? welcomeTab = null;
        if (!authService.IsAuthenticated)
        {
            welcomeTab = new TabBar();
            welcomeTab.Items.Add(new ShellContent
            {
                Title = "Вітаємо",
                Route = "welcome-start",
                Content = services.GetRequiredService<WelcomePage>()
            });
            Items.Add(welcomeTab);
        }

        favoritesTab = CreateTab("Обране", "favorites", "nav_favorites.svg", services.GetRequiredService<FavoritesPage>());
        cartTab = CreateTab("Кошик", "cart", "nav_cart.svg", services.GetRequiredService<CartPage>());
        historyTab = CreateTab("Історія", "history", "nav_history.svg", services.GetRequiredService<OrderHistoryPage>());
        settingsTab = CreateTab("Налаштування", "settings", "nav_settings.svg", services.GetRequiredService<SettingsPage>());

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Меню", "menu", "nav_menu.svg", services.GetRequiredService<MenuPage>()));
        tabs.Items.Add(favoritesTab);
        tabs.Items.Add(cartTab);
        tabs.Items.Add(historyTab);
        tabs.Items.Add(settingsTab);
        Items.Add(tabs);

        if (welcomeTab is not null)
            CurrentItem = welcomeTab;

        Routing.RegisterRoute("welcome", typeof(WelcomePage));
        Routing.RegisterRoute("cities", typeof(CitySelectionPage));
        Routing.RegisterRoute("pizzerias", typeof(PizzeriaSelectionPage));
        Routing.RegisterRoute("pizzadetail", typeof(PizzaDetailPage));
        Routing.RegisterRoute("auth", typeof(AuthPage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("orderdetail", typeof(OrderDetailPage));
        Routing.RegisterRoute("addressedit", typeof(AddressEditPage));

        this.appState.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateTabAvailability);
        UpdateTabAvailability();
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        if (HasSelectedLocation() || args.Source != ShellNavigationSource.ShellSectionChanged)
            return;

        var target = args.Target.Location.OriginalString;
        if (!target.EndsWith("/menu", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, "menu", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel();
        }
    }

    private bool HasSelectedLocation() =>
        appState.SelectedCity is not null && appState.SelectedPizzeria is not null;

    private void UpdateTabAvailability()
    {
        var enabled = HasSelectedLocation();
        favoritesTab.IsEnabled = enabled;
        cartTab.IsEnabled = enabled;
        historyTab.IsEnabled = enabled;
        settingsTab.IsEnabled = enabled;
    }

    private static ShellContent CreateTab(string title, string route, string icon, Page page) => new()
    {
        Title = title,
        Route = route,
        Icon = icon,
        Content = page
    };
}
