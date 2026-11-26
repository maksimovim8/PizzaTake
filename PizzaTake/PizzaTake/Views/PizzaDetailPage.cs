using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class PizzaDetailPage : ContentPage
{
    private readonly PizzaDetailViewModel viewModel;
    private readonly ILogger<PizzaDetailPage> logger;

    public PizzaDetailPage(PizzaDetailViewModel viewModel, ILogger<PizzaDetailPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzaDetailPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(PizzaDetailPage));
        base.OnDisappearing();
    }

    private async void OnKbjuClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(viewModel.KbjuSummary))
            return;

        await DisplayAlertAsync("Харчова цінність", viewModel.KbjuSummary, "Зрозуміло");
    }
}
