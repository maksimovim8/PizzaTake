using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class PizzaDetailPage : ContentPage, IQueryAttributable
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

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogInformation("Leaving page {PageName}", nameof(PizzaDetailPage));
        base.OnDisappearing();
    }
}
