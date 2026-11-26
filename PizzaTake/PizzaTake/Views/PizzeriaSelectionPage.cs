using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class PizzeriaSelectionPage : ContentPage
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
