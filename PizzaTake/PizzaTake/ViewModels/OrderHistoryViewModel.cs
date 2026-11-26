using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel displaying past and active orders and navigating to order details.
/// </summary>
public sealed class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderRepository orderRepository;
    private readonly ILogger<OrderHistoryViewModel> logger;

    public OrderHistoryViewModel(IOrderRepository orderRepository, ILogger<OrderHistoryViewModel>? logger = null)
    {
        this.orderRepository = orderRepository;
        this.logger = logger ?? NullLogger<OrderHistoryViewModel>.Instance;
        Title = "Історія замовлень";

        LoadHistoryCommand = new Command(async () => await LoadHistoryAsync());
        OpenDetailCommand = new Command<Order?>(async order => await OpenDetailAsync(order));
    }

    public ObservableCollection<Order> PastOrders { get; } = new();
    public bool IsEmpty => PastOrders.Count == 0;

    public ICommand LoadHistoryCommand { get; }
    public ICommand OpenDetailCommand { get; }

    public async Task LoadHistoryAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var orders = await orderRepository.GetHistoryAsync();
            PastOrders.Clear();
            foreach (var order in orders)
                PastOrders.Add(order);

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

    private static async Task OpenDetailAsync(Order? order)
    {
        if (order is null)
            return;

        await Shell.Current.GoToAsync("orderdetail", new Dictionary<string, object>
        {
            ["Order"] = order
        });
    }
}
