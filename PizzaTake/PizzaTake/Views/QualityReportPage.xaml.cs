using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class QualityReportPage : ContentPage, IQueryAttributable
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

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.LoadReportsCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        logger.LogInformation("Leaving page {PageName}", nameof(QualityReportPage));
        base.OnDisappearing();
    }
}
