using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AddressEditPage : ContentPage, IQueryAttributable
{
    private readonly AddressEditViewModel viewModel;
    private readonly ILogger<AddressEditPage> logger;

    public AddressEditPage(AddressEditViewModel viewModel, ILogger<AddressEditPage>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.logger = logger ?? NullLogger<AddressEditPage>.Instance;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.SaveDraftState();
        logger.LogInformation("Leaving page {PageName}", nameof(AddressEditPage));
    }
}
