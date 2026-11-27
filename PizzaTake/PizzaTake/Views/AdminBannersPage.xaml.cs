using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminBannersPage : ContentPage
{
    private readonly AdminBannersViewModel viewModel;
    private readonly ILogger<AdminBannersPage> logger;

    public AdminBannersPage(AdminBannersViewModel viewModel, ILogger<AdminBannersPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<AdminBannersPage>.Instance;
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
        logger.LogInformation("Leaving page {PageName}", nameof(AdminBannersPage));
        base.OnDisappearing();
    }
}
