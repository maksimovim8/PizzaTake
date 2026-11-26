using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel displaying past and active orders and navigating to order details.
/// Hides personal order history when the active session is locked.
/// </summary>
public sealed class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderRepository orderRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly ILogger<OrderHistoryViewModel> logger;
    private bool isSessionLocked;

    public OrderHistoryViewModel(
        IOrderRepository orderRepository,
        IAuthService authService,
        ISessionService sessionService,
        ILogger<OrderHistoryViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.authService = authService;
        this.sessionService = sessionService;
        this.logger = logger ?? NullLogger<OrderHistoryViewModel>.Instance;
        Title = "Історія замовлень";

        this.sessionService.SessionChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() => _ = LoadHistoryAsync());

        LoadHistoryCommand = new Command(async () => await LoadHistoryAsync());
        OpenDetailCommand = new Command<Order?>(async order => await OpenDetailAsync(order));
        UnlockSessionCommand = new Command(async () => await Shell.Current.GoToAsync("auth?locked=true"));
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

    public ICommand LoadHistoryCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand UnlockSessionCommand { get; }

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
            await Shell.Current.GoToAsync("auth?locked=true");
            return;
        }

        if (order is null)
            return;

        await Shell.Current.GoToAsync("orderdetail", new Dictionary<string, object>
        {
            ["Order"] = order
        });
    }
}
