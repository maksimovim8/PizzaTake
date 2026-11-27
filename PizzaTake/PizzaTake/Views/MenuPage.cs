using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel viewModel;
    private readonly IShakeSensorService? shakeSensorService;
    private readonly IMainThreadDispatcher? mainThreadDispatcher;
    private readonly ILogger<MenuPage> logger;
    private bool isShakeListening;

    public MenuPage(
        MenuViewModel viewModel,
        IShakeSensorService? shakeSensorService = null,
        IMainThreadDispatcher? mainThreadDispatcher = null,
        ILogger<MenuPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.shakeSensorService = shakeSensorService;
        this.mainThreadDispatcher = mainThreadDispatcher;
        this.logger = logger ?? NullLogger<MenuPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartConnectivityMonitoring();
        viewModel.LoadCommand.Execute(null);

        if (shakeSensorService is not null && !isShakeListening)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.ShakeDetected += OnShakeDetected;
            shakeSensorService.Start();
            isShakeListening = true;
        }
    }

    protected override void OnDisappearing()
    {
        viewModel.StopConnectivityMonitoring();
        if (shakeSensorService is not null && isShakeListening)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.Stop();
            isShakeListening = false;
        }

        logger.LogInformation("Leaving page {PageName}", nameof(MenuPage));
        base.OnDisappearing();
    }

    private void OnShakeDetected(object? sender, EventArgs e)
    {
        Action openReport = async () =>
        {
            try
            {
                await viewModel.OpenQualityReportCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to navigate to QualityReportPage on shake from MenuPage");
            }
        };

        if (mainThreadDispatcher is not null)
            mainThreadDispatcher.BeginInvokeOnMainThread(openReport);
        else
            MainThread.BeginInvokeOnMainThread(openReport);
    }
}
