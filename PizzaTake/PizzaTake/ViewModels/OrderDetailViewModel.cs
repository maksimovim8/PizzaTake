using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for inspecting an order's line items and repeating single items or full orders into the cart.
/// </summary>
public sealed class OrderDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IOrderRepository orderRepository;
    private readonly ICartRepository cartRepository;
    private readonly ILogger<OrderDetailViewModel> logger;

    private Order? currentOrder;

    public OrderDetailViewModel(
        IOrderRepository orderRepository,
        ICartRepository cartRepository,
        ILogger<OrderDetailViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.cartRepository = cartRepository;
        this.logger = logger ?? NullLogger<OrderDetailViewModel>.Instance;
        Title = "Деталі замовлення";

        RepeatSingleItemCommand = new Command<OrderItem?>(async item => await RepeatSingleItemAsync(item));
        RepeatFullOrderCommand = new Command(async () => await RepeatFullOrderAsync(), () => Items.Count > 0 && !IsBusy);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public Order? CurrentOrder
    {
        get => currentOrder;
        private set => SetProperty(ref currentOrder, value);
    }

    public ObservableCollection<OrderItem> Items { get; } = new();

    public ICommand RepeatSingleItemCommand { get; }
    public ICommand RepeatFullOrderCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
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
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is not null)
            await LoadOrderAsync(order);
        else
            logger.LogWarning("Order with id {OrderId} was not found", orderId);
    }

    private async Task LoadOrderAsync(Order order)
    {
        IsBusy = true;
        try
        {
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
            await Shell.Current.DisplayAlertAsync("Не вдалося додати товар", "Спробуйте ще раз.", "OK");
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
            await Shell.Current.DisplayAlertAsync("Не вдалося повторити замовлення", "Спробуйте ще раз.", "OK");
        }
        finally
        {
            IsBusy = false;
            ((Command)RepeatFullOrderCommand).ChangeCanExecute();
        }
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
}
