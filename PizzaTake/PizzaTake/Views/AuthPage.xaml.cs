using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AuthPage : ContentPage
{
    private readonly ILogger<AuthPage> logger;

    public AuthPage(AuthViewModel viewModel, ILogger<AuthPage>? logger = null)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.logger = logger ?? NullLogger<AuthPage>.Instance;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        logger.LogDebug("Leaving page {PageName}", nameof(AuthPage));
    }
}
