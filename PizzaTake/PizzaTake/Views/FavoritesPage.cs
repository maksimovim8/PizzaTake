using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class FavoritesPage : ContentPage
{
    private readonly FavoritesViewModel viewModel;
    private readonly ILogger<FavoritesPage> logger;

    public FavoritesPage(FavoritesViewModel viewModel, ILogger<FavoritesPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<FavoritesPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(FavoritesPage));
        base.OnDisappearing();
    }
}
