using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminCategoriesPage : ContentPage
{
    private readonly AdminCategoriesViewModel viewModel;
    private readonly ILogger<AdminCategoriesPage> logger;

    public AdminCategoriesPage(AdminCategoriesViewModel viewModel, ILogger<AdminCategoriesPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<AdminCategoriesPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
        viewModel.LoadCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        viewModel.StopSessionMonitoring();
        logger.LogInformation("Leaving page {PageName}", nameof(AdminCategoriesPage));
        base.OnDisappearing();
    }
}
