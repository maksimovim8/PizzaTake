using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class CartPage : ContentPage
{
    private readonly CartViewModel viewModel;
    private readonly ILogger<CartPage> logger;

    public CartPage(CartViewModel viewModel, ILogger<CartPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<CartPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartConnectivityMonitoring();
        viewModel.LoadCartCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopConnectivityMonitoring();
        logger.LogDebug("Leaving page {PageName}", nameof(CartPage));
    }
}
