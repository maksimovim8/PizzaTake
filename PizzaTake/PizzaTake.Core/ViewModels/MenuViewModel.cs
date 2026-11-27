using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class MenuCategoryChip(string name, string displayName, bool isSelected = false) : BaseViewModel
{
    private bool isSelected = isSelected;

    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}

public sealed class MenuViewModel : ConnectivityBannerViewModel
{
    private readonly IProductRepository products;
    private readonly ICartRepository cart;
    private readonly IOrderRepository orders;
    private readonly IAppStateService appState;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<MenuViewModel> logger;
    private readonly ICategoryRepository? categoryRepository;
    private readonly IMenuBannerRepository? bannerRepository;

    private string searchText = string.Empty;
    private string? selectedCategory;
    private string statusMessage = string.Empty;
    private string selectedPizzeriaText = "Оберіть піцерію";
    private Order? activeOrder;
    private Dictionary<string, int> categoryOrderMap = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<MenuCategoryChip> CategoryChips { get; } = [];
    public ObservableCollection<MenuBanner> Banners { get; } = [];
    public bool HasBanners => Banners.Count > 0;
    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);

    public Order? ActiveOrder
    {
        get => activeOrder;
        private set { if (SetProperty(ref activeOrder, value)) OnPropertyChanged(nameof(HasActiveOrder)); }
    }

    public bool HasActiveOrder => ActiveOrder is not null;
    public string SelectedPizzeriaText { get => selectedPizzeriaText; private set => SetProperty(ref selectedPizzeriaText, value); }

    public string StatusMessage
    {
        get => statusMessage;
        private set { if (SetProperty(ref statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
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
            {
                foreach (var chip in CategoryChips)
                    chip.IsSelected = string.Equals(chip.Name, selectedCategory, StringComparison.OrdinalIgnoreCase);
                _ = ApplyFilterAsync();
            }
        }
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<string?> SelectCategoryCommand { get; }
    public IAsyncRelayCommand<Product> QuickAddCommand { get; }
    public IAsyncRelayCommand<Product> OpenProductCommand { get; }
    public IAsyncRelayCommand OpenPizzeriaSelectorCommand { get; }
    public IAsyncRelayCommand<Product?> OpenAdminProductCommand { get; }
    public IAsyncRelayCommand OpenAdminCategoriesCommand { get; }
    public IAsyncRelayCommand OpenAdminBannersCommand { get; }
    public IAsyncRelayCommand OpenActiveOrderCommand { get; }
    public IAsyncRelayCommand OpenMapCommand { get; }
    public IAsyncRelayCommand OpenQualityReportCommand { get; }

    public MenuViewModel(
        IProductRepository products,
        ICartRepository cart,
        IOrderRepository orders,
        IAppStateService appState,
        IAuthService authService,
        ISessionService sessionService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        IConnectivityService? connectivityService = null,
        ISyncService? syncService = null,
        IBugReportService? bugReportService = null,
        ILogger<MenuViewModel>? logger = null,
        ICategoryRepository? categoryRepository = null,
        IMenuBannerRepository? bannerRepository = null)
        : base(connectivityService, syncService, dispatcher, logger ?? NullLogger<MenuViewModel>.Instance)
    {
        this.products = products;
        this.cart = cart;
        this.orders = orders;
        this.appState = appState;
        this.authService = authService;
        this.sessionService = sessionService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<MenuViewModel>.Instance;
        this.categoryRepository = categoryRepository;
        this.bannerRepository = bannerRepository;

        this.sessionService.SessionChanged += (_, _) =>
            this.dispatcher.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(IsAdmin)));
        Banners.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBanners));

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SelectCategoryCommand = new RelayCommand<string?>(cat => { if (!string.IsNullOrWhiteSpace(cat)) SelectedCategory = cat; });
        QuickAddCommand = new AsyncRelayCommand<Product>(QuickAddAsync);
        OpenProductCommand = new AsyncRelayCommand<Product>(async product =>
        {
            if (product is not null)
                await this.navigation.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
        OpenPizzeriaSelectorCommand = new AsyncRelayCommand(OpenPizzeriaSelectorAsync);
        OpenAdminProductCommand = new AsyncRelayCommand<Product?>(OpenAdminProductAsync);
        OpenAdminCategoriesCommand = new AsyncRelayCommand(() => OpenAdminRouteAsync("admincategories", nameof(OpenAdminCategoriesCommand)));
        OpenAdminBannersCommand = new AsyncRelayCommand(() => OpenAdminRouteAsync("adminbanners", nameof(OpenAdminBannersCommand)));
        OpenActiveOrderCommand = new AsyncRelayCommand(OpenActiveOrderAsync);
        OpenMapCommand = new AsyncRelayCommand(() => this.navigation.GoToAsync("pizzeriasmap"));
        OpenQualityReportCommand = new AsyncRelayCommand(() => this.navigation.GoToAsync("qualityreport"));
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            OnPropertyChanged(nameof(IsAdmin));
            SelectedPizzeriaText = appState.SelectedPizzeria?.StoreNumber ?? "Оберіть піцерію";
            var isSessionLocked = await authService.HasSavedCredentialsAsync() && !sessionService.IsAuthenticated;
            ActiveOrder = isSessionLocked ? null : await orders.GetActiveAsync();

            var all = await products.GetAllAsync();
            var configuredCategories = categoryRepository is not null ? await categoryRepository.GetAllAsync() : [];
            var activeBanners = bannerRepository is not null ? await bannerRepository.GetActiveAsync() : [];

            var orderedNames = BuildCategoryOrder(configuredCategories, all, out categoryOrderMap);

            await dispatcher.InvokeOnMainThreadAsync(() =>
            {
                ReplaceCollection(Banners, activeBanners);
                ReplaceCollection(Categories, orderedNames.Prepend("Усі категорії"));

                if (string.IsNullOrWhiteSpace(selectedCategory) ||
                    !Categories.Any(c => string.Equals(c, selectedCategory, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedCategory = "Усі категорії";
                }

                var chips = orderedNames
                    .Select(c => new MenuCategoryChip(c, c, string.Equals(selectedCategory, c, StringComparison.OrdinalIgnoreCase)))
                    .Prepend(new MenuCategoryChip("Усі категорії", "Усі", string.Equals(selectedCategory, "Усі категорії", StringComparison.OrdinalIgnoreCase)));
                ReplaceCollection(CategoryChips, chips);
            });

            OnPropertyChanged(nameof(SelectedCategory));
            await ShowProductsAsync(Filter(all));
            logger.LogInformation("Loaded menu catalog with {ProductCount} product(s) and {BannerCount} active banner(s)", all.Count, activeBanners.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load menu catalog");
            StatusMessage = "Не вдалося завантажити меню.";
        }
        finally { IsBusy = false; }
    }

    public async Task ApplyFilterAsync()
    {
        try
        {
            await ShowProductsAsync(Filter(await products.GetAllAsync()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to filter menu products");
            StatusMessage = "Не вдалося відфільтрувати меню.";
        }
    }

    private async Task OpenAdminProductAsync(Product? product)
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(OpenAdminProductCommand)))
            return;

        if (product is not null)
            await navigation.GoToAsync("adminproductedit", new Dictionary<string, object> { ["Product"] = product });
        else
            await navigation.GoToAsync("adminproductedit");
    }

    private async Task OpenAdminRouteAsync(string route, string commandName)
    {
        if (await RequireAdminAsync(sessionService, dialogService, logger, commandName))
            await navigation.GoToAsync(route);
    }

    private async Task OpenActiveOrderAsync()
    {
        if (ActiveOrder is null) return;
        logger.LogInformation("Opening active order {OrderNumber} from MenuPage banner", ActiveOrder.OrderNumber);
        await navigation.GoToAsync("orderdetail", new Dictionary<string, object> { ["Order"] = ActiveOrder });
    }

    private async Task OpenPizzeriaSelectorAsync()
    {
        appState.SelectedDeliveryType = DeliveryType.Pickup;
        appState.SaveState();
        await navigation.GoToAsync(appState.SelectedCity is null ? "cities?returnTo=menu" : "pizzerias?returnTo=menu");
    }

    private IEnumerable<Product> Filter(IEnumerable<Product> all)
    {
        var query = all;
        if (!string.IsNullOrWhiteSpace(SelectedCategory) &&
            !string.Equals(SelectedCategory, "Усі категорії", StringComparison.OrdinalIgnoreCase))
        {
            query = string.Equals(SelectedCategory, "Новинки", StringComparison.OrdinalIgnoreCase)
                ? query.Where(p => p.IsNew || string.Equals(p.Category, "Новинки", StringComparison.OrdinalIgnoreCase))
                : query.Where(p => string.Equals(p.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(p => p.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return query;
    }

    private Task ShowProductsAsync(IEnumerable<Product> values) =>
        dispatcher.InvokeOnMainThreadAsync(() =>
        {
            Products.Clear();
            string? previousCategory = null;
            foreach (var product in values.OrderBy(CategorySortOrder).ThenBy(p => p.Name))
            {
                product.ShowCategoryHeader = product.Category != previousCategory;
                Products.Add(product);
                previousCategory = product.Category;
            }
            StatusMessage = Products.Count == 0 ? "Нічого не знайдено." : string.Empty;
        });

    private static List<string> BuildCategoryOrder(
        IEnumerable<ProductCategory> configured,
        IEnumerable<Product> allProducts,
        out Dictionary<string, int> orderMap)
    {
        var ordered = new List<string>();
        orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in configured.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            if (orderMap.TryAdd(cat.Name, orderMap.Count))
                ordered.Add(cat.Name);

        foreach (var cat in allProducts
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(DefaultCategorySortOrder)
            .ThenBy(c => c))
        {
            if (orderMap.TryAdd(cat, orderMap.Count))
                ordered.Add(cat);
        }

        return ordered;
    }

    private int CategorySortOrder(Product product) =>
        !string.IsNullOrWhiteSpace(product.Category) && categoryOrderMap.TryGetValue(product.Category, out var order)
            ? order
            : DefaultCategorySortOrder(product.Category);

    private static int DefaultCategorySortOrder(string? category) => category switch
    {
        "Новинки" => 0,
        "WOW піци" => 1,
        "Standard" => 2,
        "Закуски" => 3,
        "Соуси" => 4,
        "Напої" => 5,
        _ => 100
    };

    private async Task QuickAddAsync(Product? product)
    {
        if (product is null) return;
        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to quick-add product {ProductName} (Id={ProductId}) to cart", product.Name, product.Id);
            if (bugReportService is not null)
                await bugReportService.ShowErrorWithReportOptionAsync(
                    "Помилка додавання",
                    "Не вдалося додати піцу до кошика. Спробуйте ще раз.",
                    "Menu: QuickAddAsync");
        }
    }
}
