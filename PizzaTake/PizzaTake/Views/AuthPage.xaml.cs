using System.Diagnostics;
using PizzaTake.ViewModels;

namespace PizzaTake.Views;

public partial class AuthPage : ContentPage
{
    public AuthPage(AuthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Debug.WriteLine("[Navigation] Leaving AuthPage");
    }
}
