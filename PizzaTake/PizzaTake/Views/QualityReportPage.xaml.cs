using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class QualityReportPage : ContentPage
{
    private readonly QualityReportViewModel viewModel;
    private readonly ILogger<QualityReportPage> logger;

    public QualityReportPage(QualityReportViewModel viewModel, ILogger<QualityReportPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<QualityReportPage>.Instance;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadReportsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(QualityReportPage));
        base.OnDisappearing();
    }
}
