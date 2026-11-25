using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AuthPage : ContentPage
{
    private readonly AuthViewModel viewModel;

    public AuthPage(AuthViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.InitializeCommand.Execute(null);
    }
}
