using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class OrderDetailPage : ContentPage
{
    private readonly ILogger<OrderDetailPage> logger;

    public OrderDetailPage(OrderDetailViewModel viewModel, ILogger<OrderDetailPage>? logger = null)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<OrderDetailPage>.Instance;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        logger.LogDebug("Leaving page {PageName}", nameof(OrderDetailPage));
    }
}
