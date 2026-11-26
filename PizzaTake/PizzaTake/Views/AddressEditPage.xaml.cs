using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AddressEditPage : ContentPage
{
    private readonly AddressEditViewModel viewModel;

    public AddressEditPage(AddressEditViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.SaveDraftState();
    }
}
