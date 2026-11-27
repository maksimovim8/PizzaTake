using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AdminProductEditPage : ContentPage, IQueryAttributable
{
    private readonly AdminProductEditViewModel viewModel;
    private readonly ILogger<AdminProductEditPage> logger;

    public AdminProductEditPage(AdminProductEditViewModel viewModel, ILogger<AdminProductEditPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<AdminProductEditPage>.Instance;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopSessionMonitoring();
        logger.LogInformation("Leaving page {PageName}", nameof(AdminProductEditPage));
    }
}
