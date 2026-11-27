using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class CheckoutPage : ContentPage
{
    private readonly CheckoutViewModel viewModel;
    private readonly ILogger<CheckoutPage> logger;

    public CheckoutPage(CheckoutViewModel viewModel, ILogger<CheckoutPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<CheckoutPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartConnectivityMonitoring();
        viewModel.LoadCheckoutCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopConnectivityMonitoring();
        viewModel.SaveDraftState();
        logger.LogDebug("Leaving page {PageName} (draft saved)", nameof(CheckoutPage));
    }
}
