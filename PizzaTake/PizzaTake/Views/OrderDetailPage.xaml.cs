using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Services;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class OrderDetailPage : ContentPage
{
    private readonly OrderDetailViewModel viewModel;
    private readonly IShakeSensorService? shakeSensorService;
    private readonly ILogger<OrderDetailPage> logger;

    public OrderDetailPage(
        OrderDetailViewModel viewModel,
        IShakeSensorService? shakeSensorService = null,
        ILogger<OrderDetailPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.shakeSensorService = shakeSensorService;
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<OrderDetailPage>.Instance;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
        if (shakeSensorService is not null)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.ShakeDetected += OnShakeDetected;
            shakeSensorService.Start();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopSessionMonitoring();
        if (shakeSensorService is not null)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.Stop();
        }

        logger.LogDebug("Leaving page {PageName}", nameof(OrderDetailPage));
    }

    private void OnShakeDetected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await viewModel.OpenQualityReportAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to open QualityReportPage on shake from OrderDetailPage");
            }
        });
    }
}
