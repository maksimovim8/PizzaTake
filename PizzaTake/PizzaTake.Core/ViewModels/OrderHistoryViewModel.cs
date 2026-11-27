using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel displaying past and active orders and navigating to order details.
/// Hides personal order history when the active session is locked.
/// </summary>
public sealed class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderRepository orderRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigation;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ILogger<OrderHistoryViewModel> logger;
    private bool isSessionLocked;

    public OrderHistoryViewModel(
        IOrderRepository orderRepository,
        IAuthService authService,
        ISessionService sessionService,
        INavigationService navigation,
        IMainThreadDispatcher dispatcher,
        ILogger<OrderHistoryViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.authService = authService;
        this.sessionService = sessionService;
        this.navigation = navigation;
        this.dispatcher = dispatcher;
        this.logger = logger ?? NullLogger<OrderHistoryViewModel>.Instance;
        Title = "Історія замовлень";

        this.sessionService.SessionChanged += (_, _) =>
            this.dispatcher.BeginInvokeOnMainThread(() => _ = LoadHistoryAsync());

        LoadHistoryCommand = new AsyncRelayCommand(LoadHistoryAsync);
        OpenDetailCommand = new AsyncRelayCommand<Order?>(OpenDetailAsync);
        UnlockSessionCommand = new AsyncRelayCommand(() => navigation.GoToAsync("auth?locked=true"));
    }

    public ObservableCollection<Order> PastOrders { get; } = new();

    public bool IsSessionLocked
    {
        get => isSessionLocked;
        private set
        {
            if (SetProperty(ref isSessionLocked, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => !IsSessionLocked && PastOrders.Count == 0;

    public IAsyncRelayCommand LoadHistoryCommand { get; }
    public IAsyncRelayCommand<Order?> OpenDetailCommand { get; }
    public IAsyncRelayCommand UnlockSessionCommand { get; }

    public async Task LoadHistoryAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var hasSavedCredentials = await authService.HasSavedCredentialsAsync();
            var locked = hasSavedCredentials && !sessionService.IsAuthenticated;
            IsSessionLocked = locked;

            PastOrders.Clear();
            if (!locked)
            {
                var orders = await orderRepository.GetHistoryAsync();
                foreach (var order in orders)
                    PastOrders.Add(order);
            }

            OnPropertyChanged(nameof(IsEmpty));
            logger.LogInformation("Loaded {OrderCount} order(s) in order history", PastOrders.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load order history");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenDetailAsync(Order? order)
    {
        if (IsSessionLocked)
        {
            await navigation.GoToAsync("auth?locked=true");
            return;
        }

        if (order is null)
            return;

        await navigation.GoToAsync("orderdetail", new Dictionary<string, object>
        {
            ["Order"] = order
        });
    }
}
