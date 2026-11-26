using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AdminProductEditPage : ContentPage
{
    public AdminProductEditPage(AdminProductEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
