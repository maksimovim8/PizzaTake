using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class OrderHistoryPage : ContentPage
{
    private readonly OrderHistoryViewModel viewModel;
    private readonly ILogger<OrderHistoryPage> logger;

    public OrderHistoryPage(OrderHistoryViewModel viewModel, ILogger<OrderHistoryPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<OrderHistoryPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadHistoryCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        logger.LogDebug("Leaving page {PageName}", nameof(OrderHistoryPage));
    }
}
