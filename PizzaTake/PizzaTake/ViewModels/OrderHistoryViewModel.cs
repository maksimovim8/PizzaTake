using System.Collections.ObjectModel;
using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel displaying past and active orders and navigating to order details.
/// </summary>
public sealed class OrderHistoryViewModel : BaseViewModel
{
    private readonly IOrderRepository orderRepository;

    public OrderHistoryViewModel(IOrderRepository orderRepository)
    {
        this.orderRepository = orderRepository;
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
