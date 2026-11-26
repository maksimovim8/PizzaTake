using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AddressEditPage : ContentPage
{
    public AddressEditPage(AddressEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
