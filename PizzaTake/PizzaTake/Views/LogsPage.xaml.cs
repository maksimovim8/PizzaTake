using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class LogsPage : ContentPage
{
    private readonly LogsViewModel viewModel;
    private readonly ILogger<LogsPage> logger;

    public LogsPage(LogsViewModel viewModel, ILogger<LogsPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<LogsPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.StartSessionMonitoring();
        viewModel.LoadLogsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        viewModel.StopSessionMonitoring();
        logger.LogInformation("Leaving page {PageName}", nameof(LogsPage));
        base.OnDisappearing();
    }
}
