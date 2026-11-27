using Microsoft.Extensions.DependencyInjection;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;
using PizzaTake.Views;

namespace PizzaTake;

public partial class AppShell : Shell
{
    private static string? coldStartDeepLinkRoute;

    private readonly IAppStateService appState;
    private readonly ICartRepository cartRepository;
    private readonly ShellContent favoritesTab;
    private readonly ShellContent cartTab;
    private readonly ShellContent historyTab;
    private readonly ShellContent settingsTab;

    public static void SetColdStartDeepLink(string route) => coldStartDeepLinkRoute = route;

    public AppShell(
        IServiceProvider services,
        IAuthService authService,
        IAppStateService appState,
        ICartRepository cartRepository)
    {
        this.appState = appState;
        this.cartRepository = cartRepository;
        this.appState.RestoreState();
        InitializeComponent();

        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, true);

        var welcomeTab = new TabBar();
        Shell.SetTabBarIsVisible(welcomeTab, false);
        welcomeTab.Items.Add(new ShellContent
        {
            Title = "Вітаємо",
            Route = "welcome-start",
            Content = services.GetRequiredService<WelcomePage>()
        });
        Items.Add(welcomeTab);

        favoritesTab = CreateTab("Обране", "favorites", "nav_favorites.svg", services.GetRequiredService<FavoritesPage>());
        cartTab = CreateTab("Кошик", "cart", "nav_cart.svg", services.GetRequiredService<CartPage>());
        historyTab = CreateTab("Історія", "history", "nav_history.svg", services.GetRequiredService<OrderHistoryPage>());
        settingsTab = CreateTab("Профіль", "settings", "nav_settings.svg", services.GetRequiredService<SettingsPage>());

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Меню", "menu", "nav_menu.svg", services.GetRequiredService<MenuPage>()));
        tabs.Items.Add(favoritesTab);
        tabs.Items.Add(cartTab);
        tabs.Items.Add(historyTab);
        tabs.Items.Add(settingsTab);
        Items.Add(tabs);

        CurrentItem = this.appState.SelectedCity is null ? welcomeTab : tabs;

        Routing.RegisterRoute("welcome", typeof(WelcomePage));
        Routing.RegisterRoute("cities", typeof(CitySelectionPage));
        Routing.RegisterRoute("pizzerias", typeof(PizzeriaSelectionPage));
        Routing.RegisterRoute("pizzadetail", typeof(PizzaDetailPage));
        Routing.RegisterRoute("auth", typeof(AuthPage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("orderdetail", typeof(OrderDetailPage));
        Routing.RegisterRoute("addressedit", typeof(AddressEditPage));
        Routing.RegisterRoute("adminproductedit", typeof(AdminProductEditPage));
        Routing.RegisterRoute("admincategories", typeof(AdminCategoriesPage));
        Routing.RegisterRoute("adminbanners", typeof(AdminBannersPage));
        Routing.RegisterRoute("logs", typeof(LogsPage));
        Routing.RegisterRoute("qualityreport", typeof(QualityReportPage));
        Routing.RegisterRoute("pizzeriasmap", typeof(PizzeriasMapPage));

        this.appState.StateChanged += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(UpdateTabAvailability);
            _ = RefreshCartBadgeAsync();
        };
        this.cartRepository.CartChanged += (_, _) => _ = RefreshCartBadgeAsync();
        Loaded += async (_, _) => await TryNavigatePendingDeepLinkAsync();
        UpdateTabAvailability();
        _ = RefreshCartBadgeAsync();
    }

    public async Task TryNavigatePendingDeepLinkAsync()
    {
        var route = appState.PendingDeepLinkRoute ?? coldStartDeepLinkRoute;
        if (string.IsNullOrWhiteSpace(route))
            return;

        appState.PendingDeepLinkRoute = null;
        coldStartDeepLinkRoute = null;

        await GoToAsync(route);
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        if (HasSelectedLocation() || args.Source != ShellNavigationSource.ShellSectionChanged)
            return;

        var target = args.Target.Location.OriginalString;
        if (!target.EndsWith("/menu", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, "menu", StringComparison.OrdinalIgnoreCase) &&
            !target.Contains("welcome", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel();
        }
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);

        var location = args.Current?.Location.OriginalString ?? string.Empty;
        var hideTabBar =
            location.Contains("welcome", StringComparison.OrdinalIgnoreCase) ||
            location.Contains("cities", StringComparison.OrdinalIgnoreCase) ||
            location.Contains("pizzerias", StringComparison.OrdinalIgnoreCase) ||
            location.Contains("auth", StringComparison.OrdinalIgnoreCase);

        Shell.SetTabBarIsVisible(this, !hideTabBar);
        if (CurrentPage is not null)
            Shell.SetTabBarIsVisible(CurrentPage, !hideTabBar);
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

    private async Task RefreshCartBadgeAsync()
    {
        try
        {
            var items = await cartRepository.GetAllAsync();
            var totalQty = items.Sum(i => Math.Max(1, i.Quantity));
            MainThread.BeginInvokeOnMainThread(() =>
            {
                cartTab.Title = totalQty > 0 ? $"Кошик ({totalQty})" : "Кошик";
            });
        }
        catch
        {
            // Ignore badge refresh errors during app bootstrap.
        }
    }

    private static ShellContent CreateTab(string title, string route, string icon, Page page) => new()
    {
        Title = title,
        Route = route,
        Icon = icon,
        Content = page
    };
}
