using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel viewModel;
    private readonly IShakeSensorService? shakeSensorService;
    private readonly ILogger<MenuPage> logger;

    public MenuPage(
        MenuViewModel viewModel,
        IShakeSensorService? shakeSensorService = null,
        ILogger<MenuPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.shakeSensorService = shakeSensorService;
        this.logger = logger ?? NullLogger<MenuPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartConnectivityMonitoring();
        viewModel.LoadCommand.Execute(null);

        if (shakeSensorService is not null)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.ShakeDetected += OnShakeDetected;
            shakeSensorService.Start();
        }
    }

    protected override void OnDisappearing()
    {
        viewModel.StopConnectivityMonitoring();
        if (shakeSensorService is not null)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.Stop();
        }

        logger.LogDebug("Leaving page {PageName}", nameof(MenuPage));
        base.OnDisappearing();
    }

    private void OnShakeDetected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync("qualityreport");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to navigate to QualityReportPage on shake from MenuPage");
            }
        });
    }
}
