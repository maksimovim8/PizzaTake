using PizzaTake.Core.ViewModels;

namespace PizzaTake.Views;

public partial class AddressEditPage : ContentPage, IQueryAttributable
{
    private readonly AddressEditViewModel viewModel;

    public AddressEditPage(AddressEditViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.ApplyQueryAttributes(query);

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.SaveDraftState();
    }
}
