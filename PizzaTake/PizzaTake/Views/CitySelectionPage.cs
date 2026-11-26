using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class CitySelectionPage : ContentPage
{
    private readonly CitySelectionViewModel viewModel;
    private readonly ILogger<CitySelectionPage> logger;

    public CitySelectionPage(CitySelectionViewModel viewModel, ILogger<CitySelectionPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<CitySelectionPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(CitySelectionPage));
        base.OnDisappearing();
    }
}
