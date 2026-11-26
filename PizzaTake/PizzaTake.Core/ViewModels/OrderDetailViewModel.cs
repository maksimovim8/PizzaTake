using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for inspecting an order's line items, repeating items into the cart,
/// and administrator-only order status updates and deletion with two-level RBAC.
/// </summary>
public sealed class OrderDetailViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IOrderRepository orderRepository;
    private readonly ICartRepository cartRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly ISyncService syncService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<OrderDetailViewModel> logger;

    private Order? currentOrder;

    public OrderDetailViewModel(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        IAuthService authService,
        ISessionService sessionService,
        ISyncService syncService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        IBugReportService? bugReportService = null,
        ILogger<OrderDetailViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.cartRepository = cartRepository;
        this.authService = authService;
        this.sessionService = sessionService;
        this.syncService = syncService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<OrderDetailViewModel>.Instance;
        Title = "Деталі замовлення";

        RepeatSingleItemCommand = new AsyncRelayCommand<OrderItem?>(RepeatSingleItemAsync);
        RepeatFullOrderCommand = new AsyncRelayCommand(RepeatFullOrderAsync, () => Items.Count > 0 && !IsBusy);
        AdvanceOrderStatusCommand = new AsyncRelayCommand(AdvanceOrderStatusAsync);
        DeleteOrderCommand = new AsyncRelayCommand(DeleteOrderAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);
    public bool CanShowAdminPanel => IsAdmin && CurrentOrder is not null;

    public Order? CurrentOrder
    {
        get => currentOrder;
        private set
        {
            if (SetProperty(ref currentOrder, value))
                OnPropertyChanged(nameof(CanShowAdminPanel));
        }
    }

    public ObservableCollection<OrderItem> Items { get; } = new();

    public IAsyncRelayCommand<OrderItem?> RepeatSingleItemCommand { get; }
    public IAsyncRelayCommand RepeatFullOrderCommand { get; }
    public IAsyncRelayCommand AdvanceOrderStatusCommand { get; }
    public IAsyncRelayCommand DeleteOrderCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public void StartSessionMonitoring()
    {
        sessionService.SessionChanged -= OnSessionChanged;
        sessionService.SessionChanged += OnSessionChanged;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanShowAdminPanel));
    }

    public void StopSessionMonitoring()
    {
        sessionService.SessionChanged -= OnSessionChanged;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        OnPropertyChanged(nameof(IsAdmin));
        if (query.TryGetValue("Order", out var orderObj) && orderObj is Order order)
        {
            _ = LoadOrderAsync(order);
            return;
        }

        if (query.TryGetValue("id", out var idObj) && int.TryParse(idObj?.ToString(), out var orderId))
            _ = LoadOrderByIdAsync(orderId);
    }

    public async Task LoadOrderAsync(Order order)
    {
        IsBusy = true;
        try
        {
            if (await authService.HasSavedCredentialsAsync() && !sessionService.IsAuthenticated)
            {
                CurrentOrder = null;
                Items.Clear();
                return;
            }

            CurrentOrder = order;
            var items = await orderRepository.GetItemsByOrderIdAsync(order.Id);
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            logger.LogInformation("Loaded order details for {OrderNumber} ({ItemCount} item(s))", order.OrderNumber, Items.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load items for order {OrderId}", order.Id);
        }
        finally
        {
            IsBusy = false;
            RepeatFullOrderCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        dispatcher.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CanShowAdminPanel));
        });
    }

    private async Task LoadOrderByIdAsync(int orderId)
    {
        try
        {
            var order = await orderRepository.GetByIdAsync(orderId);
            if (order is not null)
            {
                await LoadOrderAsync(order);
            }
            else
            {
                logger.LogWarning("Order with id {OrderId} was not found", orderId);
                await dialogService.AlertAsync("Замовлення не знайдено", "Запитане замовлення не знайдено в історії.");
                await navigation.GoBackAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load order {OrderId}", orderId);
            await ShowOperationalErrorAsync("Помилка", "Не вдалося завантажити замовлення.", "OrderDetail: LoadOrderByIdAsync");
        }
    }

    private async Task AdvanceOrderStatusAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(AdvanceOrderStatusCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        if (CurrentOrder is null || IsBusy)
            return;

        var nextStatus = GetNextOrderStatus(CurrentOrder.Status, CurrentOrder.DeliveryType);
        if (nextStatus is null)
        {
            await dialogService.AlertAsync("Статус замовлення", "Замовлення вже має фінальний статус.");
            return;
        }

        IsBusy = true;
        try
        {
            await orderRepository.UpdateStatusAsync(CurrentOrder.Id, nextStatus.Value);
            CurrentOrder.Status = nextStatus.Value;
            OnPropertyChanged(nameof(CurrentOrder));

            await syncService.EnqueueAsync("Order", "UpdateStatus", new
            {
                CurrentOrder.Id,
                CurrentOrder.OrderNumber,
                Status = nextStatus.Value.ToString(),
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Administrator advanced order {OrderNumber} status to {Status}.",
                CurrentOrder.OrderNumber, nextStatus.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update order status for OrderId={OrderId}.", CurrentOrder.Id);
            await ShowOperationalErrorAsync("Помилка", "Не вдалося оновити статус замовлення.", "OrderDetail: AdvanceOrderStatusAsync");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteOrderAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(DeleteOrderCommand));
            await dialogService.AlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.");
            return;
        }

        if (CurrentOrder is null || IsBusy)
            return;

        var confirmed = await dialogService.ConfirmAsync(
            "Видалення замовлення",
            $"Видалити замовлення №{CurrentOrder.OrderNumber}?",
            "Видалити",
            "Скасувати");

        if (!confirmed)
            return;

        IsBusy = true;
        try
        {
            var deletedId = CurrentOrder.Id;
            var deletedNumber = CurrentOrder.OrderNumber;

            await orderRepository.DeleteOrderAsync(deletedId);
            await syncService.EnqueueAsync("Order", "Delete", new
            {
                Id = deletedId,
                OrderNumber = deletedNumber,
                UpdatedAt = DateTime.UtcNow
            });

            logger.LogInformation("Administrator deleted order {OrderNumber} (Id={OrderId}).", deletedNumber, deletedId);
            await navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete order Id={OrderId}.", CurrentOrder.Id);
            await ShowOperationalErrorAsync("Помилка", "Не вдалося видалити замовлення.", "OrderDetail: DeleteOrderAsync");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RepeatSingleItemAsync(OrderItem? item)
    {
        if (item is null)
            return;

        try
        {
            await AddOrderItemToCartAsync(item);
            logger.LogInformation("Repeated single order item {ProductName} into cart", item.ProductName);
            await dialogService.AlertAsync("Кошик", $"Додано «{item.ProductName}» до кошика.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not repeat order item {ProductName}", item.ProductName);
            await ShowOperationalErrorAsync("Не вдалося додати товар", "Спробуйте ще раз.", "OrderDetail: RepeatSingleItemAsync");
        }
    }

    private async Task RepeatFullOrderAsync()
    {
        if (Items.Count == 0 || IsBusy)
            return;

        IsBusy = true;
        RepeatFullOrderCommand.NotifyCanExecuteChanged();
        try
        {
            foreach (var item in Items.ToList())
                await AddOrderItemToCartAsync(item);

            logger.LogInformation("Repeated full order {OrderNumber} ({ItemCount} item(s))", CurrentOrder?.OrderNumber, Items.Count);

            await navigation.PopToRootAsync(false);
            await navigation.GoToAsync("///cart");
            await navigation.PopToRootAsync(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not repeat full order {OrderNumber}", CurrentOrder?.OrderNumber);
            await ShowOperationalErrorAsync("Не вдалося повторити замовлення", "Спробуйте ще раз.", "OrderDetail: RepeatFullOrderAsync");
        }
        finally
        {
            IsBusy = false;
            RepeatFullOrderCommand.NotifyCanExecuteChanged();
        }
    }

    private Task ShowOperationalErrorAsync(string title, string message, string context)
    {
        if (bugReportService is not null)
            return bugReportService.ShowErrorWithReportOptionAsync(title, message, context);

        return dialogService.AlertAsync(title, message);
    }

    private Task AddOrderItemToCartAsync(OrderItem item)
    {
        var cartItem = new CartItem
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            Quantity = Math.Max(1, item.Quantity),
            UnitPrice = item.UnitPrice,
            OptionsSummary = item.OptionsSummary
        };

        return cartRepository.SaveAsync(cartItem);
    }

    public static OrderStatus? GetNextOrderStatus(OrderStatus current, DeliveryType deliveryType) => current switch
    {
        OrderStatus.Pending => OrderStatus.Accepted,
        OrderStatus.Accepted => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.Ready,
        OrderStatus.Ready => deliveryType == DeliveryType.Pickup ? OrderStatus.Completed : OrderStatus.Delivering,
        OrderStatus.Delivering => OrderStatus.Completed,
        _ => null
    };
}
