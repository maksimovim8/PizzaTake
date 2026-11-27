using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class CitySelectionPage : ContentPage, IQueryAttributable
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

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogInformation("Leaving page {PageName}", nameof(CitySelectionPage));
        base.OnDisappearing();
    }
}
