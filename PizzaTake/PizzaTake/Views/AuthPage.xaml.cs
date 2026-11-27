using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AuthPage : ContentPage, IQueryAttributable
{
    private readonly AuthViewModel viewModel;
    private readonly ILogger<AuthPage> logger;

    public AuthPage(AuthViewModel viewModel, ILogger<AuthPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<AuthPage>.Instance;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.InitializeCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        logger.LogDebug("Leaving page {PageName}", nameof(AuthPage));
    }
}
