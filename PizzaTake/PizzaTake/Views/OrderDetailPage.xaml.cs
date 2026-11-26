using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class OrderDetailPage : ContentPage, IQueryAttributable
{
    private readonly OrderDetailViewModel viewModel;
    private readonly ILogger<OrderDetailPage> logger;

    public OrderDetailPage(OrderDetailViewModel viewModel, ILogger<OrderDetailPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<OrderDetailPage>.Instance;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopSessionMonitoring();
        logger.LogDebug("Leaving page {PageName}", nameof(OrderDetailPage));
    }
}
