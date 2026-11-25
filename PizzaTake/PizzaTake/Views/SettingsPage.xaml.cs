using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel viewModel;
    private readonly ILogger<SettingsPage> logger;

    public SettingsPage(SettingsViewModel viewModel, ILogger<SettingsPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<SettingsPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadSettingsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        logger.LogDebug("Leaving page {PageName}", nameof(SettingsPage));
    }
}
