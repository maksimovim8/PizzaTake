using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class MenuCategoryChip : BaseViewModel
{
    private bool isSelected;

    public MenuCategoryChip(string name, string displayName, bool isSelected = false)
    {
        Name = name;
        DisplayName = displayName;
        this.isSelected = isSelected;
    }

    public string Name { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

public sealed class MenuViewModel : BaseViewModel
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
    private readonly IConnectivityService? connectivityService;
    private readonly ISyncService? syncService;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<MenuViewModel> logger;
    private readonly ICategoryRepository? categoryRepository;
    private readonly IMenuBannerRepository? bannerRepository;

    private string searchText = string.Empty;
    private string? selectedCategory;
    private string statusMessage = string.Empty;
    private string selectedPizzeriaText = "Оберіть піцерію";
    private Order? activeOrder;
    private bool isOffline;
    private int pendingSyncCount;
    private Dictionary<string, int> categoryOrderMap = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<MenuCategoryChip> CategoryChips { get; } = [];
    public ObservableCollection<MenuBanner> Banners { get; } = [];
    public bool HasBanners => Banners.Count > 0;
    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);

    public bool IsOffline
    {
        get => isOffline;
        private set
        {
            if (SetProperty(ref isOffline, value))
                OnPropertyChanged(nameof(OfflineBannerText));
        }
    }

    public int PendingSyncCount
    {
        get => pendingSyncCount;
        private set
        {
            if (SetProperty(ref pendingSyncCount, value))
                OnPropertyChanged(nameof(OfflineBannerText));
        }
    }

    public string OfflineBannerText => $"Офлайн-режим (у черзі: {PendingSyncCount})";
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

    public string SelectedPizzeriaText
    {
        get => selectedPizzeriaText;
        private set => SetProperty(ref selectedPizzeriaText, value);
    }

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
        set
        {
            if (SetProperty(ref searchText, value))
                _ = ApplyFilterAsync();
        }
    }

    public string? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (SetProperty(ref selectedCategory, value))
            {
                UpdateCategoryChipSelection();
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
        this.connectivityService = connectivityService;
        this.syncService = syncService;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<MenuViewModel>.Instance;
        this.categoryRepository = categoryRepository;
        this.bannerRepository = bannerRepository;

        this.sessionService.SessionChanged += (_, _) =>
            this.dispatcher.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(IsAdmin)));

        Banners.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBanners));

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SelectCategoryCommand = new RelayCommand<string?>(category =>
        {
            if (!string.IsNullOrWhiteSpace(category))
                SelectedCategory = category;
        });
        QuickAddCommand = new AsyncRelayCommand<Product>(QuickAddAsync);
        OpenProductCommand = new AsyncRelayCommand<Product>(async product =>
        {
            if (product is not null)
                await this.navigation.GoToAsync("pizzadetail", new Dictionary<string, object> { ["Product"] = product });
        });
        OpenPizzeriaSelectorCommand = new AsyncRelayCommand(OpenPizzeriaSelectorAsync);
        OpenAdminProductCommand = new AsyncRelayCommand<Product?>(OpenAdminProductAsync);
        OpenAdminCategoriesCommand = new AsyncRelayCommand(OpenAdminCategoriesAsync);
        OpenAdminBannersCommand = new AsyncRelayCommand(OpenAdminBannersAsync);
        OpenActiveOrderCommand = new AsyncRelayCommand(OpenActiveOrderAsync);
        OpenMapCommand = new AsyncRelayCommand(() => this.navigation.GoToAsync("pizzeriasmap"));
        OpenQualityReportCommand = new AsyncRelayCommand(() => this.navigation.GoToAsync("qualityreport"));
    }

    public void StartConnectivityMonitoring()
    {
        if (connectivityService is not null)
        {
            connectivityService.StartMonitoring();
            IsOffline = !connectivityService.IsConnected;
            connectivityService.ConnectivityChanged -= OnConnectivityChanged;
            connectivityService.ConnectivityChanged += OnConnectivityChanged;
        }

        if (syncService is not null)
        {
            syncService.SyncCompleted -= OnSyncCompleted;
            syncService.SyncCompleted += OnSyncCompleted;
        }

        _ = RefreshPendingSyncCountAsync();
    }

    public void StopConnectivityMonitoring()
    {
        if (connectivityService is not null)
            connectivityService.ConnectivityChanged -= OnConnectivityChanged;

        if (syncService is not null)
            syncService.SyncCompleted -= OnSyncCompleted;
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        dispatcher.BeginInvokeOnMainThread(() => IsOffline = !isConnected);
        _ = RefreshPendingSyncCountAsync();
    }

    private void OnSyncCompleted(object? sender, EventArgs e)
    {
        _ = RefreshPendingSyncCountAsync();
    }

    private async Task RefreshPendingSyncCountAsync()
    {
        if (syncService is null)
            return;

        try
        {
            var count = await syncService.GetPendingCountAsync();
            dispatcher.BeginInvokeOnMainThread(() => PendingSyncCount = count);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to refresh pending sync count on MenuPage");
        }
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
            var configuredCategories = categoryRepository is not null
                ? await categoryRepository.GetAllAsync()
                : [];
            var activeBanners = bannerRepository is not null
                ? await bannerRepository.GetActiveAsync()
                : [];

            var orderedCategoryNames = new List<string>();
            var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cat in configuredCategories)
            {
                if (!string.IsNullOrWhiteSpace(cat.Name) && orderMap.TryAdd(cat.Name, orderMap.Count))
                    orderedCategoryNames.Add(cat.Name);
            }

            foreach (var productCat in all
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(DefaultCategorySortOrder)
                .ThenBy(c => c))
            {
                if (orderMap.TryAdd(productCat, orderMap.Count))
                    orderedCategoryNames.Add(productCat);
            }

            categoryOrderMap = orderMap;

            await dispatcher.InvokeOnMainThreadAsync(() =>
            {
                Banners.Clear();
                foreach (var banner in activeBanners)
                    Banners.Add(banner);

                Categories.Clear();
                Categories.Add("Усі категорії");
                foreach (var category in orderedCategoryNames)
                    Categories.Add(category);

                if (string.IsNullOrWhiteSpace(selectedCategory) ||
                    !Categories.Any(c => string.Equals(c, selectedCategory, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedCategory = "Усі категорії";
                }

                CategoryChips.Clear();
                CategoryChips.Add(new MenuCategoryChip(
                    "Усі категорії",
                    "Усі",
                    string.Equals(selectedCategory, "Усі категорії", StringComparison.OrdinalIgnoreCase)));
                foreach (var category in orderedCategoryNames)
                {
                    CategoryChips.Add(new MenuCategoryChip(
                        category,
                        category,
                        string.Equals(selectedCategory, category, StringComparison.OrdinalIgnoreCase)));
                }
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
            var all = await products.GetAllAsync();
            await ShowProductsAsync(Filter(all));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to filter menu products");
            StatusMessage = "Не вдалося відфільтрувати меню.";
        }
    }

    private void UpdateCategoryChipSelection()
    {
        foreach (var chip in CategoryChips)
            chip.IsSelected = string.Equals(chip.Name, selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private async Task OpenAdminProductAsync(Product? product)
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(OpenAdminProductCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        if (product is not null)
            await navigation.GoToAsync("adminproductedit", new Dictionary<string, object> { ["Product"] = product });
        else
            await navigation.GoToAsync("adminproductedit");
    }

    private async Task OpenAdminCategoriesAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(OpenAdminCategoriesCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        await navigation.GoToAsync("admincategories");
    }

    private async Task OpenAdminBannersAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(OpenAdminBannersCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        await navigation.GoToAsync("adminbanners");
    }

    private async Task OpenActiveOrderAsync()
    {
        if (ActiveOrder is null)
            return;

        logger.LogInformation("Opening active order {OrderNumber} from MenuPage banner", ActiveOrder.OrderNumber);
        await navigation.GoToAsync("orderdetail", new Dictionary<string, object>
        {
            ["Order"] = ActiveOrder
        });
    }

    private async Task OpenPizzeriaSelectorAsync()
    {
        appState.SelectedDeliveryType = DeliveryType.Pickup;
        appState.SaveState();
        var route = appState.SelectedCity is null ? "cities?returnTo=menu" : "pizzerias?returnTo=menu";
        await navigation.GoToAsync(route);
    }

    private IEnumerable<Product> Filter(IEnumerable<Product> all)
    {
        var query = all;
        if (!string.IsNullOrWhiteSpace(SelectedCategory) &&
            !string.Equals(SelectedCategory, "Усі категорії", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(SelectedCategory, "Новинки", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(product =>
                    product.IsNew ||
                    string.Equals(product.Category, "Новинки", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                query = query.Where(product =>
                    string.Equals(product.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(product => product.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return query;
    }

    private Task ShowProductsAsync(IEnumerable<Product> values)
    {
        return dispatcher.InvokeOnMainThreadAsync(() =>
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

    private int CategorySortOrder(Product product)
    {
        if (!string.IsNullOrWhiteSpace(product.Category) &&
            categoryOrderMap.TryGetValue(product.Category, out var order))
        {
            return order;
        }

        return DefaultCategorySortOrder(product.Category);
    }

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
