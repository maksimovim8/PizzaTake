using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class WelcomePage : ContentPage
{
    private readonly ILogger<WelcomePage> logger;

    public WelcomePage(WelcomeViewModel viewModel, ILogger<WelcomePage>? logger = null)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<WelcomePage>.Instance;
    }

    protected override void OnDisappearing()
    {
        logger.LogDebug("Leaving page {PageName}", nameof(WelcomePage));
        base.OnDisappearing();
    }
}
