using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for inspecting an order's line items, repeating items into the cart,
/// and administrator-only order status updates and deletion with two-level RBAC.
/// </summary>
public sealed class OrderDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IOrderRepository orderRepository;
    private readonly ICartRepository cartRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly ISyncService syncService;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<OrderDetailViewModel> logger;

    private Order? currentOrder;

    public OrderDetailViewModel(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        IAuthService authService,
        ISessionService sessionService,
        ISyncService syncService,
        IBugReportService? bugReportService = null,
        ILogger<OrderDetailViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.cartRepository = cartRepository;
        this.authService = authService;
        this.sessionService = sessionService;
        this.syncService = syncService;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<OrderDetailViewModel>.Instance;
        Title = "Деталі замовлення";

        RepeatSingleItemCommand = new Command<OrderItem?>(async item => await RepeatSingleItemAsync(item));
        RepeatFullOrderCommand = new Command(async () => await RepeatFullOrderAsync(), () => Items.Count > 0 && !IsBusy);
        AdvanceOrderStatusCommand = new Command(async () => await AdvanceOrderStatusAsync());
        DeleteOrderCommand = new Command(async () => await DeleteOrderAsync());
        OpenQualityReportCommand = new Command(async () => await OpenQualityReportAsync());
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

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

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CanShowAdminPanel));
        });
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

    public ICommand RepeatSingleItemCommand { get; }
    public ICommand RepeatFullOrderCommand { get; }
    public ICommand AdvanceOrderStatusCommand { get; }
    public ICommand DeleteOrderCommand { get; }
    public ICommand OpenQualityReportCommand { get; }
    public ICommand GoBackCommand { get; }

    public Task OpenQualityReportAsync()
    {
        if (CurrentOrder is not null)
        {
            return Shell.Current.GoToAsync("qualityreport", new Dictionary<string, object>
            {
                ["Order"] = CurrentOrder
            });
        }

        return Shell.Current.GoToAsync("qualityreport");
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
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (Shell.Current is not null)
                    {
                        await Shell.Current.DisplayAlertAsync("Замовлення не знайдено", "Запитане замовлення не знайдено в історії.", "OK");
                        await Shell.Current.GoToAsync("..");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load order {OrderId}", orderId);
            await ShowOperationalErrorAsync("Помилка", "Не вдалося завантажити замовлення.", "OrderDetail: LoadOrderByIdAsync");
        }
    }

    private async Task LoadOrderAsync(Order order)
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
            ((Command)RepeatFullOrderCommand).ChangeCanExecute();
        }
    }

    private async Task AdvanceOrderStatusAsync()
    {
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(AdvanceOrderStatusCommand));
            await Shell.Current.DisplayAlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.", "OK");
            return;
        }

        if (CurrentOrder is null || IsBusy)
            return;

        var nextStatus = GetNextOrderStatus(CurrentOrder.Status, CurrentOrder.DeliveryType);
        if (nextStatus is null)
        {
            await Shell.Current.DisplayAlertAsync("Статус замовлення", "Замовлення вже має фінальний статус.", "OK");
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
            await Shell.Current.DisplayAlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.", "OK");
            return;
        }

        if (CurrentOrder is null || IsBusy)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
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
            await Shell.Current.GoToAsync("..");
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
            await Shell.Current.DisplayAlertAsync("Кошик", $"Додано «{item.ProductName}» до кошика.", "OK");
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
        ((Command)RepeatFullOrderCommand).ChangeCanExecute();
        try
        {
            foreach (var item in Items.ToList())
                await AddOrderItemToCartAsync(item);

            logger.LogInformation("Repeated full order {OrderNumber} ({ItemCount} item(s))", CurrentOrder?.OrderNumber, Items.Count);

            // Clear the detail route, then select Cart and clear any stale checkout route there.
            await Shell.Current.Navigation.PopToRootAsync(false);
            await Shell.Current.GoToAsync("///cart");
            await Shell.Current.Navigation.PopToRootAsync(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not repeat full order {OrderNumber}", CurrentOrder?.OrderNumber);
            await ShowOperationalErrorAsync("Не вдалося повторити замовлення", "Спробуйте ще раз.", "OrderDetail: RepeatFullOrderAsync");
        }
        finally
        {
            IsBusy = false;
            ((Command)RepeatFullOrderCommand).ChangeCanExecute();
        }
    }

    private Task ShowOperationalErrorAsync(string title, string message, string context)
    {
        if (bugReportService is not null)
            return bugReportService.ShowErrorWithReportOptionAsync(title, message, context);

        return Shell.Current is not null
            ? Shell.Current.DisplayAlertAsync(title, message, "OK")
            : Task.CompletedTask;
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

    private static OrderStatus? GetNextOrderStatus(OrderStatus current, DeliveryType deliveryType) => current switch
    {
        OrderStatus.Pending => OrderStatus.Accepted,
        OrderStatus.Accepted => OrderStatus.Preparing,
        OrderStatus.Preparing => OrderStatus.Ready,
        OrderStatus.Ready => deliveryType == DeliveryType.Pickup ? OrderStatus.Completed : OrderStatus.Delivering,
        OrderStatus.Delivering => OrderStatus.Completed,
        _ => null
    };
}
