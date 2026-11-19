using System.Collections.ObjectModel;
using System.Windows.Input;
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

    private Order? currentOrder;

    public OrderDetailViewModel(IOrderRepository orderRepository, ICartRepository cartRepository)
    {
        this.orderRepository = orderRepository;
        this.cartRepository = cartRepository;
        Title = "Деталі замовлення";

        RepeatSingleItemCommand = new Command<OrderItem?>(async item => await RepeatSingleItemAsync(item));
        RepeatFullOrderCommand = new Command(async () => await RepeatFullOrderAsync(), () => Items.Count > 0);
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

            ((Command)RepeatFullOrderCommand).ChangeCanExecute();
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

        await AddOrderItemToCartAsync(item);
        await Shell.Current.DisplayAlertAsync("Кошик", $"Додано «{item.ProductName}» до кошика.", "OK");
    }

    private async Task RepeatFullOrderAsync()
    {
        if (Items.Count == 0)
            return;

        foreach (var item in Items)
            await AddOrderItemToCartAsync(item);

        await Shell.Current.GoToAsync("cart");
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
