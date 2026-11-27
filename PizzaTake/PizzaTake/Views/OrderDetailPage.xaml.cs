using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Services;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class OrderDetailPage : ContentPage, IQueryAttributable
{
    private readonly OrderDetailViewModel viewModel;
    private readonly IShakeSensorService? shakeSensorService;
    private readonly IMainThreadDispatcher? mainThreadDispatcher;
    private readonly ILogger<OrderDetailPage> logger;
    private bool isShakeListening;

    public OrderDetailPage(
        OrderDetailViewModel viewModel,
        IShakeSensorService? shakeSensorService = null,
        IMainThreadDispatcher? mainThreadDispatcher = null,
        ILogger<OrderDetailPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.shakeSensorService = shakeSensorService;
        this.mainThreadDispatcher = mainThreadDispatcher;
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<OrderDetailPage>.Instance;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
        if (shakeSensorService is not null && !isShakeListening)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.ShakeDetected += OnShakeDetected;
            shakeSensorService.Start();
            isShakeListening = true;
        }
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _ = viewModel.EnsureSessionAccessAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopSessionMonitoring();
        if (shakeSensorService is not null && isShakeListening)
        {
            shakeSensorService.ShakeDetected -= OnShakeDetected;
            shakeSensorService.Stop();
            isShakeListening = false;
        }

        logger.LogInformation("Leaving page {PageName}", nameof(OrderDetailPage));
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
                logger.LogWarning(ex, "Failed to open QualityReportPage on shake from OrderDetailPage");
            }
        };

        if (mainThreadDispatcher is not null)
            mainThreadDispatcher.BeginInvokeOnMainThread(openReport);
        else
            MainThread.BeginInvokeOnMainThread(openReport);
    }
}
