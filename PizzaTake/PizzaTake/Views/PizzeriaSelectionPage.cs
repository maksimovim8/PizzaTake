using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class PizzeriaSelectionPage : ContentPage, IQueryAttributable
{
    private readonly PizzeriaSelectionViewModel viewModel;
    private readonly ILogger<PizzeriaSelectionPage> logger;

    public PizzeriaSelectionPage(PizzeriaSelectionViewModel viewModel, ILogger<PizzeriaSelectionPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<PizzeriaSelectionPage>.Instance;
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
        logger.LogDebug("Leaving page {PageName}", nameof(PizzeriaSelectionPage));
        base.OnDisappearing();
    }
}
